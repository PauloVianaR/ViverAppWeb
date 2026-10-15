using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.AdministratorExperience;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.PatientScheduling;

public sealed record AppointmentPrintRow(ulong Number, string PatientName, string DoctorName, string Services,
    string CategoryCode, string Specialty, DateTime LocalStartsAt, string BillingFormCode,
    decimal BasePriceAmount, decimal PriceAmount, bool RequiresPayment, bool IsPaid, bool PaymentDeferred,
    string? Notes, string StatusCode);
public sealed record AppointmentPrintResponse(string ClinicName, string IssuedBy, DateTime LocalIssuedAt,
    string TimezoneName, string FilterDescription, IReadOnlyList<AppointmentPrintRow> Rows);

public sealed class AppointmentPrintQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public DateOnly? Until { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Status { get; set; }
    public string? Category { get; set; }
    public string? Modality { get; set; }
    public uint? SpecialtyId { get; set; }
    public ulong? DoctorAccountId { get; set; }
    public ulong? AppointmentNumber { get; set; }
    public string? Payment { get; set; }
    public bool? PendingPayment { get; set; }
    public string? Search { get; set; }
    public string Sort { get; set; } = "date_asc";
}

public sealed class AppointmentPrintService(ViverAppDbContext database, TimeProvider clock, IClinicalOperationsAuditWriter audit)
{
    public async Task<AppointmentPrintResponse> PrintAsync(ulong actor, AppointmentPrintQuery filter, CancellationToken ct)
    {
        var account = await database.Accounts.AsNoTracking().Where(a => a.Id == actor && a.StatusCode == "active")
            .Select(a => new { a.RoleCode, a.FullName }).SingleOrDefaultAsync(ct)
            ?? throw Invalid(403, "Sua sessão não pode emitir este relatório.");
        var clinic = await database.Clinics.AsNoTracking().SingleAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(clinic.TimezoneName);
        var query = ApplyFilters(Scope(database.Appointments.AsNoTracking(), actor, account.RoleCode), filter, zone, account.RoleCode);
        // Never silently truncate a report. Avoid an unbounded authenticated export.
        if (await query.CountAsync(ct) > 20000)
            throw Invalid(422, "O relatório possui mais de 20 mil atendimentos. Reduza o período ou refine os filtros para imprimir.");
        var rows = await query.Include(a => a.PatientAccount).Include(a => a.ProfessionalAccount.Account)
            .Include(a => a.AppointmentType).Include(a => a.Specialty).Include(a => a.AppointmentServiceItems)
            .Include(a => a.CurrentPayment).ToArrayAsync(ct);
        if (filter.StartTime.HasValue)
            rows = rows.Where(a => TimeOnly.FromDateTime(Local(a.StartsAtUtc, zone)) >= filter.StartTime
                && TimeOnly.FromDateTime(Local(a.StartsAtUtc, zone)) < filter.EndTime).ToArray();
        var result = rows.Select(a => new AppointmentPrintRow(a.AppointmentNumber, a.PatientAccount.FullName,
            a.ProfessionalAccount.Account.FullName,
            a.AppointmentServiceItems.Count > 0 ? string.Join(" + ", a.AppointmentServiceItems.OrderBy(s => s.Ordinal).Select(s => s.NameSnapshot)) : a.AppointmentType.Name,
            a.AppointmentType.CategoryCode, a.Specialty?.Name ?? "Não informada", Local(a.StartsAtUtc, zone),
            a.BillingFormCode, a.BasePriceAmount ?? a.PriceAmount, a.PriceAmount, a.RequiresPayment,
            a.CurrentPayment?.StatusCode == "paid", a.PaymentDeferred, a.PatientNotes, a.StatusCode)).ToArray();
        await audit.WriteAsync("appointments.list_printed", actor, "appointments", "filtered",
            new Dictionary<string, string> { ["count"] = result.Length.ToString(CultureInfo.InvariantCulture) }, ct);
        return new(clinic.LegalName, account.FullName, Local(clock.GetUtcNow().UtcDateTime, zone), clinic.TimezoneName,
            Describe(filter), result);
    }

    internal static IQueryable<Appointment> Scope(IQueryable<Appointment> query, ulong actor, string role) => role switch
    {
        "patient" => query.Where(a => a.PatientAccountId == actor && a.InverseRescheduledFromAppointment == null),
        "doctor" or "psychologist" => query.Where(a => a.ProfessionalAccountId == actor && a.InverseRescheduledFromAppointment == null),
        "manager" or "administrator" => query.Where(a => a.InverseRescheduledFromAppointment == null),
        _ => throw Invalid(403, "Seu perfil não pode imprimir atendimentos."),
    };

    internal static IQueryable<Appointment> ApplyFilters(IQueryable<Appointment> query, AppointmentPrintQuery filter,
        TimeZoneInfo zone, string role)
    {
        var until = filter.To ?? filter.Until;
        if (until == DateOnly.MaxValue) throw Invalid(400, "A data final do relatório é inválida.");
        if (filter.From > until || filter.From.HasValue && until.HasValue && until.Value.DayNumber - filter.From.Value.DayNumber > 366)
            throw Invalid(400, "O período deve ter no máximo 367 dias.");
        if (filter.StartTime.HasValue != filter.EndTime.HasValue || filter.StartTime >= filter.EndTime)
            throw Invalid(400, "O intervalo de horário é inválido.");
        if (filter.From.HasValue) { var start = TimeZoneInfo.ConvertTimeToUtc(filter.From.Value.ToDateTime(TimeOnly.MinValue), zone); query = query.Where(a => a.StartsAtUtc >= start); }
        if (until.HasValue) { var end = TimeZoneInfo.ConvertTimeToUtc(until.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), zone); query = query.Where(a => a.StartsAtUtc < end); }
        var status = Text(filter.Status);
        if (status is not null && status is not ("pending" or "confirmed" or "arrived" or "in_progress" or "completed" or "canceled" or "rescheduled" or "no_show")) throw Invalid(400, "Estado inválido.");
        if (status == "rescheduled") query = query.Where(a => a.AppointmentRescheduleHistories.Any() || a.RescheduledFromAppointmentId != null);
        else if (status is not null) query = query.Where(a => a.StatusCode == status);
        var category = Text(filter.Category);
        if (category is not null && category is not ("consultation" or "examination" or "surgery" or "procedure")) throw Invalid(400, "Tipo inválido.");
        if (category is not null) query = query.Where(a => a.AppointmentType.CategoryCode == category);
        var modality = Text(filter.Modality);
        if (modality is not null && modality is not ("online" or "in_person")) throw Invalid(400, "Modalidade inválida.");
        if (modality is not null) query = query.Where(a => a.ModalityCode == modality);
        if (filter.SpecialtyId == 0 || filter.DoctorAccountId == 0) throw Invalid(400, "Selecione um filtro válido.");
        if (filter.SpecialtyId.HasValue) query = query.Where(a => a.SpecialtyId == filter.SpecialtyId);
        if (filter.DoctorAccountId.HasValue) query = query.Where(a => a.ProfessionalAccountId == filter.DoctorAccountId);
        if (filter.AppointmentNumber.HasValue) query = query.Where(a => a.AppointmentNumber == filter.AppointmentNumber);
        var payment = Text(filter.Payment);
        if (payment is not null && payment is not ("paid" or "pending")) throw Invalid(400, "Filtro de pagamento inválido.");
        if (payment == "paid") query = query.Where(a => a.CurrentPayment != null && a.CurrentPayment.StatusCode == "paid");
        if (payment == "pending") query = query.Where(a => a.RequiresPayment && (a.CurrentPayment == null || a.CurrentPayment.StatusCode != "paid"));
        query = AppointmentPaymentPolicy.FilterPending(query, filter.PendingPayment);
        var search = Text(filter.Search);
        if (search is not null)
        {
            if (search.Length > 120) throw Invalid(400, "A busca deve ter no máximo 120 caracteres.");
            var isNumber = ulong.TryParse(search, out var number);
            query = role switch
            {
                "patient" => query.Where(a => a.AppointmentType.Name.Contains(search) || a.ProfessionalAccount.Account.FullName.Contains(search) || isNumber && a.AppointmentNumber == number),
                "doctor" or "psychologist" => query.Where(a => a.AppointmentType.Name.Contains(search) || a.PatientAccount.FullName.Contains(search) || isNumber && a.AppointmentNumber == number),
                _ => query.Where(a => a.AppointmentType.Name.Contains(search) || a.PatientAccount.FullName.Contains(search) || a.ProfessionalAccount.Account.FullName.Contains(search) || isNumber && a.AppointmentNumber == number),
            };
        }
        return filter.Sort switch
        {
            "date_desc" => query.OrderByDescending(a => a.StartsAtUtc).ThenByDescending(a => a.Id),
            "patient" => query.OrderBy(a => a.PatientAccount.FullName).ThenBy(a => a.StartsAtUtc).ThenBy(a => a.Id),
            "doctor" => query.OrderBy(a => a.ProfessionalAccount.Account.FullName).ThenBy(a => a.StartsAtUtc).ThenBy(a => a.Id),
            "date_asc" => query.OrderBy(a => a.StartsAtUtc).ThenBy(a => a.Id),
            _ => throw Invalid(400, "Ordenação inválida."),
        };
    }

    private static string Describe(AppointmentPrintQuery f) => string.Join(" · ", new[]
    {
        f.From.HasValue ? $"De {f.From:dd/MM/yyyy}" : null,
        (f.To ?? f.Until).HasValue ? $"Até {f.To ?? f.Until:dd/MM/yyyy}" : null,
        f.StartTime.HasValue ? $"Horário {f.StartTime:HH\\:mm}–{f.EndTime:HH\\:mm}" : null,
        Text(f.Status) is { } status ? $"Estado: {Label(status)}" : null,
        Text(f.Category) is { } category ? $"Tipo: {Label(category)}" : null,
        Text(f.Modality) is { } modality ? $"Modalidade: {Label(modality)}" : null,
        f.SpecialtyId.HasValue ? "Especialidade selecionada" : null,
        f.DoctorAccountId.HasValue ? "Profissional selecionado" : null,
        f.AppointmentNumber.HasValue ? $"Atendimento #{f.AppointmentNumber}" : null,
        Text(f.Payment) is { } payment ? $"Pagamento: {Label(payment)}" : null,
        f.PendingPayment.HasValue ? $"Pagamento Pendente: {(f.PendingPayment.Value ? "Sim" : "Não")}" : null,
        Text(f.Search) is { } search ? $"Busca: {search}" : null,
    }.Where(s => s is not null)) is { Length: > 0 } text ? text : "Sem filtros adicionais";
    private static string Label(string code) => code switch
    {
        "pending" => "Aguardando Pagamento",
        "confirmed" => "Confirmado",
        "arrived" => "Paciente chegou",
        "in_progress" => "Em atendimento",
        "completed" => "Concluído",
        "canceled" => "Cancelado",
        "rescheduled" => "Reagendado",
        "no_show" => "Não compareceu",
        "consultation" => "Consulta",
        "examination" => "Exame",
        "surgery" => "Cirurgia",
        "procedure" => "Procedimento",
        "online" => "Online",
        "in_person" => "Presencial",
        "paid" => "Pago",
        _ => code,
    };
    private static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static DateTime Local(DateTime utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
    private static AdministratorRuleException Invalid(int status, string message) => new(status, message);
}

[ApiController, Route("api/v1/appointment-reports/print")]
[Authorize(Roles = "patient,doctor,psychologist,manager,administrator")]
[ServiceFilter(typeof(AdministratorExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AppointmentPrintController(AppointmentPrintService service) : ControllerBase
{
    [HttpGet, EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<AppointmentPrintResponse> Print([FromQuery] AppointmentPrintQuery filter, CancellationToken ct) =>
        service.PrintAsync(ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), filter, ct);
}
