using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.AdministratorExperience;

public sealed record AdministratorAnalyticsExportRequest(DateOnly From, DateOnly To);
public sealed record AdministratorAnalyticsExportResponse(
    ulong Id, DateOnly From, DateOnly To, string Status, DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc, uint? SizeBytes);

public sealed class AdministratorAnalyticsExportService(
    ViverAppDbContext database, AdministratorExperienceService analytics,
    IDataProtectionProvider protectionProvider, IdentityAuditWriter audit, TimeProvider clock,
    ILogger<AdministratorAnalyticsExportService> logger)
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("ViverApp.Administrator.AnalyticsExport.v1");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AdministratorAnalyticsExportResponse> RequestAsync(
        ulong actor, AdministratorAnalyticsExportRequest request, CancellationToken ct)
    {
        ValidatePeriod(request.From, request.To);
        await using var transaction = database.Database.CurrentTransaction is null
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        if (await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE id={actor} AND role_code='administrator' AND status_code='active' FOR UPDATE")
            .SingleOrDefaultAsync(ct) is null)
            throw new AdministratorRuleException(403, "A conta administradora não está ativa.");
        var now = Now;
        var active = await database.AdministratorAnalyticsExports.CountAsync(x =>
            x.RequestedByAccountId == actor && x.ExpiresAtUtc > now &&
            (x.StatusCode == "queued" || x.StatusCode == "processing"), ct);
        if (active >= 2)
            throw new AdministratorRuleException(409, "Aguarde a conclusão das exportações já solicitadas.");
        var item = new AdministratorAnalyticsExport
        {
            RequestedByAccountId = actor,
            PeriodFrom = request.From.ToDateTime(TimeOnly.MinValue),
            PeriodTo = request.To.ToDateTime(TimeOnly.MinValue),
            StatusCode = "queued",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(24),
        };
        database.AdministratorAnalyticsExports.Add(item);
        await database.SaveChangesAsync(ct);
        await audit.WriteAsync("administrator.analytics_export.requested", actor, "administrator_analytics_export",
            item.Id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string>
            {
                ["from"] = request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["to"] = request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Map(item);
    }

    public async Task<IReadOnlyList<AdministratorAnalyticsExportResponse>> ListAsync(ulong actor, CancellationToken ct)
    {
        await ExpireAsync(ct);
        var items = await database.AdministratorAnalyticsExports.AsNoTracking()
            .Where(x => x.RequestedByAccountId == actor)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(20)
            .ToArrayAsync(ct);
        return items.Select(Map).ToArray();
    }

    public async Task<byte[]> DownloadAsync(ulong actor, ulong id, CancellationToken ct)
    {
        var item = await database.AdministratorAnalyticsExports.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == id && x.RequestedByAccountId == actor, ct)
            ?? throw new AdministratorRuleException(404, "Exportação não encontrada.");
        if (item.StatusCode != "ready" || item.ExpiresAtUtc <= Now || item.ProtectedContent is null || item.ContentSha256 is null)
            throw new AdministratorRuleException(410, "Esta exportação não está disponível. Solicite uma nova.");
        byte[] content;
        try { content = protector.Unprotect(item.ProtectedContent); }
        catch (CryptographicException) { throw new AdministratorRuleException(410, "Esta exportação não está disponível. Solicite uma nova."); }
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(content), item.ContentSha256))
            throw new AdministratorRuleException(410, "Esta exportação não está disponível. Solicite uma nova.");
        await audit.WriteAsync("administrator.analytics_export.downloaded", actor, "administrator_analytics_export",
            id.ToString(CultureInfo.InvariantCulture), null, ct);
        return content;
    }

    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        await ExpireAsync(ct);
        var now = Now;
        var ids = await database.AdministratorAnalyticsExports.AsNoTracking()
            .Where(x => x.ExpiresAtUtc > now && x.AttemptCount < 3 &&
                (x.StatusCode == "queued" || x.StatusCode == "processing" && x.LeaseUntilUtc < now))
            .OrderBy(x => x.CreatedAtUtc).Select(x => x.Id).Take(3).ToArrayAsync(ct);
        var processed = 0;
        foreach (var id in ids)
        {
            if (await ProcessOneAsync(id, ct)) processed++;
        }
        return processed;
    }

    internal async Task<bool> ProcessOneAsync(ulong id, CancellationToken ct)
    {
        var deadline = Now.AddMinutes(3);
        var leaseUntil = new DateTime(deadline.Ticks - deadline.Ticks % 10, DateTimeKind.Utc);
        var claimed = await database.AdministratorAnalyticsExports.Where(x => x.Id == id &&
                    x.ExpiresAtUtc > Now && x.AttemptCount < 3 &&
                    (x.StatusCode == "queued" || x.StatusCode == "processing" && x.LeaseUntilUtc < Now))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StatusCode, "processing")
                    .SetProperty(x => x.AttemptCount, x => (ushort)(x.AttemptCount + 1))
                    .SetProperty(x => x.LeaseUntilUtc, leaseUntil), ct);
        if (claimed == 0) return false;
        var item = await database.AdministratorAnalyticsExports.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        try
        {
            var data = await analytics.AnalyticsAsync(DateOnly.FromDateTime(item.PeriodFrom), DateOnly.FromDateTime(item.PeriodTo), ct);
            var content = RenderCsv(data);
            if (content.Length > 2_000_000) throw new InvalidOperationException("Exportação acima do tamanho permitido.");
            var protectedContent = protector.Protect(content);
            await database.AdministratorAnalyticsExports.Where(x => x.Id == id && x.StatusCode == "processing" &&
                    x.AttemptCount == item.AttemptCount && x.LeaseUntilUtc == leaseUntil && x.ExpiresAtUtc > Now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StatusCode, "ready")
                    .SetProperty(x => x.ProtectedContent, protectedContent)
                    .SetProperty(x => x.ContentSha256, SHA256.HashData(content))
                    .SetProperty(x => x.ContentSizeBytes, (uint)content.Length)
                    .SetProperty(x => x.LeaseUntilUtc, (DateTime?)null)
                    .SetProperty(x => x.CompletedAtUtc, Now)
                    .SetProperty(x => x.ErrorCode, (string?)null), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Falha ao gerar exportação analítica {ExportId}.", id);
            await database.AdministratorAnalyticsExports.Where(x => x.Id == id && x.StatusCode == "processing" &&
                    x.AttemptCount == item.AttemptCount && x.LeaseUntilUtc == leaseUntil)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StatusCode, item.AttemptCount >= 3 ? "failed" : "queued")
                    .SetProperty(x => x.LeaseUntilUtc, (DateTime?)null)
                    .SetProperty(x => x.ErrorCode, "generation_failed"), ct);
        }
        return true;
    }

    private Task<int> ExpireAsync(CancellationToken ct) => database.AdministratorAnalyticsExports
        .Where(x => x.ExpiresAtUtc <= Now && x.StatusCode != "expired")
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.StatusCode, "expired")
            .SetProperty(x => x.ProtectedContent, (byte[]?)null)
            .SetProperty(x => x.ContentSha256, (byte[]?)null)
            .SetProperty(x => x.LeaseUntilUtc, (DateTime?)null), ct);

    internal static void ValidatePeriod(DateOnly from, DateOnly to)
    {
        if (from.Year < 2000 || to < from || to.DayNumber - from.DayNumber > 366)
            throw new AdministratorRuleException(400, "O período deve ter no máximo 366 dias.");
    }

    internal static byte[] RenderCsv(AdministratorAnalyticsResponse data)
    {
        var output = new StringBuilder("\uFEFF");
        Row("Relatório analítico ViverApp");
        Row("De", data.From.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), "Até", data.To.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        Row("Indicador", "Valor");
        Row("Receita", Number(data.Revenue)); Row("Atendimentos", data.Appointments.ToString(CultureInfo.InvariantCulture));
        Row("Ticket médio", Number(data.AverageTicket)); Row("Satisfação", data.Satisfaction?.ToString("0.0", CultureInfo.InvariantCulture) ?? "");
        Section("Receita mensal", "Mês", "Valor", "Pagamentos");
        foreach (var x in data.RevenueByMonth) Row(x.Label, Number(x.Value), x.Count.ToString(CultureInfo.InvariantCulture));
        Section("Atendimentos por estado", "Estado", "Quantidade");
        foreach (var x in data.AppointmentsByStatus) Row(x.Label, x.Count.ToString(CultureInfo.InvariantCulture));
        Section("Pagamentos por forma", "Forma", "Valor", "Quantidade");
        foreach (var x in data.PaymentsByMethod) Row(x.Label, Number(x.Value), x.Count.ToString(CultureInfo.InvariantCulture));
        Section("Desempenho profissional", "Profissional", "Avaliação média", "Atendimentos");
        foreach (var x in data.DoctorPerformance) Row(x.Label, Number(x.Value), x.Count.ToString(CultureInfo.InvariantCulture));
        Section("Receita por tipo de usuário", "Mês", "Regular", "Premium");
        foreach (var x in data.RevenueByUserType) Row(x.Label, Number(x.Regular), Number(x.Premium));
        Section("Evolução por forma", "Mês", "Cartão", "Pix", "Dinheiro", "Boleto");
        foreach (var x in data.PaymentsByTypeEvolution) Row(x.Label, Number(x.Card), Number(x.Pix), Number(x.Cash), Number(x.BankSlip));
        Section("Local do pagamento", "Mês", "Online", "Clínica");
        foreach (var x in data.PaymentsByLocationTrend) Row(x.Label, x.Online.ToString(CultureInfo.InvariantCulture), x.InClinic.ToString(CultureInfo.InvariantCulture));
        Section("Pagamentos por local", "Local", "Valor", "Quantidade");
        foreach (var x in data.PaymentsByLocation) Row(x.Label, Number(x.Value), x.Count.ToString(CultureInfo.InvariantCulture));
        Section("Serviços", "Serviço", "Valor", "Atendimentos");
        foreach (var x in data.AppointmentsByService) Row(x.Label, Number(x.Value), x.Count.ToString(CultureInfo.InvariantCulture));
        Section("Tipos de atendimento", "Tipo", "Valor", "Atendimentos");
        foreach (var x in data.AppointmentsByCategory) Row(x.Label, Number(x.Value), x.Count.ToString(CultureInfo.InvariantCulture));
        return Encoding.UTF8.GetBytes(output.ToString());

        void Section(params string[] cells) { output.AppendLine(); Row(cells); }
        void Row(params string[] cells) => output.AppendLine(string.Join(';', cells.Select(Cell)));
        static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        static string Cell(string value)
        {
            var safe = value.TrimStart() is { Length: > 0 } trimmed && "=+-@".Contains(trimmed[0]) ? "'" + value : value;
            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }
    }

    private static AdministratorAnalyticsExportResponse Map(AdministratorAnalyticsExport item) => new(item.Id,
        DateOnly.FromDateTime(item.PeriodFrom), DateOnly.FromDateTime(item.PeriodTo), item.StatusCode,
        item.CreatedAtUtc, item.ExpiresAtUtc, item.ContentSizeBytes);
}

internal sealed class AdministratorAnalyticsExportWorker(
    IServiceScopeFactory scopes, ILogger<AdministratorAnalyticsExportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<AdministratorAnalyticsExportService>();
                await service.ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Falha ao processar exportações administrativas."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
