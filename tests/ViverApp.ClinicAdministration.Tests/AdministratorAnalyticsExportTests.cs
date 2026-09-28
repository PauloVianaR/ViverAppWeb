using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ViverApp.Api.Features.AdministratorExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.ClinicAdministration.Tests;

public sealed class AdministratorAnalyticsExportTests
{
    [Fact]
    public void CsvContainsAllAggregateSectionsAndEscapesSpreadsheetFormulas()
    {
        var empty = Array.Empty<AdministratorMetricPoint>();
        var response = new AdministratorAnalyticsResponse(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 24),
            250m, 2, 125m, 4.5m, 100m, 1, empty, empty, empty,
            [new AdministratorMetricPoint("=HYPERLINK(\"https://invalid.example\")", 4.5m, 2)],
            [], [], [], [], [], []);

        var csv = Encoding.UTF8.GetString(AdministratorAnalyticsExportService.RenderCsv(response));

        Assert.Contains("Receita por tipo de usuário", csv);
        Assert.Contains("Tipos de atendimento", csv);
        Assert.Contains("\"'=HYPERLINK", csv);
        Assert.DoesNotContain(";=HYPERLINK", csv);
        Assert.Contains("\"250.00\"", csv);
    }

    [Fact]
    public void PeriodOutsideOneYearIsRejected()
    {
        Assert.Throws<AdministratorRuleException>(() => AdministratorAnalyticsExportService.ValidatePeriod(
            new DateOnly(2025, 1, 1), new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public async Task ExportBelongsToRequesterRequiresReadyStateAndExpires()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authentication:Delivery:Enabled"] = "false" }));
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        var exports = scope.ServiceProvider.GetRequiredService<AdministratorAnalyticsExportService>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var email = $"phase24-export-{Guid.NewGuid():N}@example.test";
        var actor = new Account
        {
            RoleCode = ViverAppRoles.Administrator, StatusCode = "active", FullName = "Administrador sintético de exportação",
            Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailVerified = true,
            SecurityStamp = RandomNumberGenerator.GetBytes(32), CreatedAtUtc = now, UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Accounts.Add(actor);
        await database.SaveChangesAsync();

        var created = await exports.RequestAsync(actor.Id,
            new AdministratorAnalyticsExportRequest(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2)), default);
        Assert.Equal("queued", created.Status);
        await Assert.ThrowsAsync<AdministratorRuleException>(() => exports.DownloadAsync(actor.Id, created.Id, default));
        await Assert.ThrowsAsync<AdministratorRuleException>(() => exports.DownloadAsync(actor.Id + 1, created.Id, default));

        Assert.True(await exports.ProcessOneAsync(created.Id, default));
        var item = await database.AdministratorAnalyticsExports.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal("ready", item.StatusCode);
        Assert.NotNull(item.ProtectedContent);
        Assert.Contains(await exports.ListAsync(actor.Id, default), x => x.Id == created.Id && x.Status == "ready");
        var content = await exports.DownloadAsync(actor.Id, created.Id, default);
        Assert.Contains("Relatório analítico ViverApp", Encoding.UTF8.GetString(content));

        await database.AdministratorAnalyticsExports.Where(x => x.Id == created.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.CreatedAtUtc, now.AddDays(-2))
             .SetProperty(x => x.ExpiresAtUtc, now.AddDays(-1)));
        await Assert.ThrowsAsync<AdministratorRuleException>(() => exports.DownloadAsync(actor.Id, created.Id, default));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task CriticalAccountStatusRequiresTypedTargetAndWritesAuditAtomically()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authentication:Delivery:Enabled"] = "false" }));
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<AdministratorExperienceService>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        Account Create(string role, string name)
        {
            var email = $"phase24-confirm-{Guid.NewGuid():N}@example.test";
            return new Account
            {
                RoleCode = role, StatusCode = "active", FullName = name,
                Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailVerified = true,
                SecurityStamp = RandomNumberGenerator.GetBytes(32), CreatedAtUtc = now, UpdatedAtUtc = now,
                RowVersion = 1,
            };
        }
        var admin = Create(ViverAppRoles.Administrator, "Admin confirmação sintético");
        var patient = Create(ViverAppRoles.Patient, "Paciente confirmação sintético");
        database.Accounts.AddRange(admin, patient);
        await database.SaveChangesAsync();

        await Assert.ThrowsAsync<AdministratorRuleException>(() => service.SetAccountStatusAsync(admin.Id, patient.Id,
            new AdministratorAccountStatusRequest("blocked", "Bloqueio autorizado do teste", patient.RowVersion, "nome incorreto"), default));
        Assert.Equal("active", (await database.Accounts.AsNoTracking().SingleAsync(x => x.Id == patient.Id)).StatusCode);

        await service.SetAccountStatusAsync(admin.Id, patient.Id,
            new AdministratorAccountStatusRequest("blocked", "Bloqueio autorizado do teste", patient.RowVersion, patient.FullName), default);
        Assert.Equal("blocked", (await database.Accounts.AsNoTracking().SingleAsync(x => x.Id == patient.Id)).StatusCode);
        Assert.True(await database.AuditEvents.AsNoTracking().AnyAsync(x => x.EventCode == "administrator.user.status_changed"
            && x.EntityId == patient.Id.ToString()));

        var maintenance = await database.ApplicationSettings.SingleAsync(x => x.SettingKey == "web.maintenance_mode");
        var target = maintenance.ValueJson.Trim() == "true" ? "false" : "true";
        var phrase = target == "true" ? "ATIVAR MANUTENCAO" : "DESATIVAR MANUTENCAO";
        await Assert.ThrowsAsync<AdministratorRuleException>(() => service.UpdateSettingAsync(admin.Id,
            maintenance.SettingKey, new AdministratorSettingUpdateRequest(target, maintenance.RowVersion), default));
        var changed = await service.UpdateSettingAsync(admin.Id, maintenance.SettingKey,
            new AdministratorSettingUpdateRequest(target, maintenance.RowVersion, phrase), default);
        Assert.Equal(target, changed.ValueJson);
        Assert.True(await database.AuditEvents.AsNoTracking().AnyAsync(x => x.EventCode == "administrator.setting.updated"
            && x.EntityId == "web.maintenance_mode"));
        await transaction.RollbackAsync();
    }
}
