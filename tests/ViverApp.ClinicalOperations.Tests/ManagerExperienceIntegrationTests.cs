using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.ManagerExperience;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.ClinicalOperations.Tests;

public sealed class ManagerExperienceIntegrationTests : IAsyncLifetime
{
    private const string Marker = "__phase13_test__";
    private IConfiguration configuration = null!;
    private ulong managerId, appointmentId;
    private DateTime now;
    private DateOnly appointmentLocalDate;

    public async Task InitializeAsync()
    {
        configuration = new ConfigurationBuilder().AddUserSecrets<ManagerExperienceIntegrationTests>().Build();
        await CleanupAsync(); now = DateTime.UtcNow;
        await using var db = CreateContext();
        var manager = Account($"manager-{Guid.NewGuid():N}@phase13.example.test", ViverAppRoles.Manager, "Gestora Fase Treze");
        var doctor = Account($"doctor-{Guid.NewGuid():N}@phase13.example.test", ViverAppRoles.Doctor, "Dra. Operação");
        var patient = Account($"patient-{Guid.NewGuid():N}@phase13.example.test", ViverAppRoles.Patient, "Paciente Operacional");
        db.Accounts.AddRange(manager, doctor, patient); await db.SaveChangesAsync(); managerId = manager.Id;
        db.ManagerPreferences.Add(new ManagerPreference { ManagerAccountId = manager.Id, EmailEnabled = true, SmsEnabled = true, UpdatedAtUtc = now, RowVersion = 1 });
        db.DoctorProfiles.Add(new DoctorProfile { AccountId = doctor.Id, ProfessionalTitle = "Dra.", LicenseStateCode = "SP", LicenseNumber = "130001", DefaultAppointmentDurationMinutes = 30, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 });
        db.PatientProfiles.Add(new PatientProfile { AccountId = patient.Id, PreferredName = "Paciente", CreatedAtUtc = now, UpdatedAtUtc = now });
        var type = new AppointmentType { Name = Marker + Guid.NewGuid().ToString("N"), CategoryCode = "consultation", Description = "Teste do Gestor", ModalityCode = "in_person", DurationMinutes = 30, PriceAmount = 180, IsActive = true, DisplayOrder = 999, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 };
        db.AppointmentTypes.Add(type); await db.SaveChangesAsync();
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        appointmentLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, timezone)).AddDays(1);
        var startsAtUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(appointmentLocalDate.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Unspecified),
            timezone);
        var appointment = new Appointment { PatientAccountId = patient.Id, DoctorAccountId = doctor.Id, AppointmentTypeId = type.Id, CreatedByAccountId = manager.Id, StatusCode = "pending", ModalityCode = "in_person", StartsAtUtc = startsAtUtc, EndsAtUtc = startsAtUtc.AddMinutes(30), PriceAmount = 180, BasePriceAmount = 180, DiscountPercent = 0, PaymentLocationCode = "clinic", CurrencyCode = "BRL", PatientNotes = "Observação operacional sintética", CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 };
        db.Appointments.Add(appointment); await db.SaveChangesAsync(); appointmentId = appointment.Id;
    }

    public Task DisposeAsync() => CleanupAsync();

    [Fact]
    public async Task ManagerReadsClinicMetadataAndConfirmsManualPaymentIdempotently()
    {
        await using var db = CreateContext(); var service = CreateService(db);
        var appointment = await service.AppointmentAsync(appointmentId, CancellationToken.None);
        Assert.Equal("unpaid", appointment.Payment.StatusCode); Assert.False(appointment.Report.Exists); Assert.True(appointment.CanConfirmPayment);
        var key = Guid.NewGuid().ToString("N"); var request = new ManagerPaymentConfirmRequest("credit_card", now, "1234", "AUTH-PHASE13", appointment.RowVersion);
        var first = await service.ConfirmPaymentAsync(managerId, appointmentId, key, request, CancellationToken.None);
        var replay = await service.ConfirmPaymentAsync(managerId, appointmentId, key, request, CancellationToken.None);
        Assert.Equal(first.Id, replay.Id); Assert.Equal("paid", replay.StatusCode); Assert.Equal("1234", replay.CardLastFour);
        Assert.Equal(1, await db.PaymentEvents.CountAsync(x => x.PaymentId == first.Id && x.SourceCode == "manual"));
        Assert.Equal(managerId, await db.Payments.Where(x => x.Id == first.Id).Select(x => x.ConfirmedByAccountId).SingleAsync());
    }

    [Fact]
    public async Task ManagerCannotConfirmClinicPaymentTwiceWithAnotherKey()
    {
        await using var db = CreateContext(); var service = CreateService(db);
        var request = new ManagerPaymentConfirmRequest("pix", now, null, null, 1);
        await service.ConfirmPaymentAsync(managerId, appointmentId, Guid.NewGuid().ToString("N"), request, CancellationToken.None);
        var error = await Assert.ThrowsAsync<ManagerRuleException>(() => service.ConfirmPaymentAsync(managerId, appointmentId, Guid.NewGuid().ToString("N"), request, CancellationToken.None));
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task ManagerAgendaAppliesTimeFilterInClinicTimezone()
    {
        await using var db = CreateContext(); var service = CreateService(db);
        var included = await service.AgendaAsync(appointmentLocalDate, appointmentLocalDate, null, null, null, null,
            null, new TimeOnly(9, 30), new TimeOnly(10, 30), null, "date_asc", 1, 20, CancellationToken.None);
        var excluded = await service.AgendaAsync(appointmentLocalDate, appointmentLocalDate, null, null, null, null,
            null, new TimeOnly(11, 0), new TimeOnly(12, 0), null, "date_asc", 1, 20, CancellationToken.None);

        Assert.Contains(included.Page.Items, x => x.Id == appointmentId);
        Assert.DoesNotContain(excluded.Page.Items, x => x.Id == appointmentId);
    }

    [Fact]
    public async Task ManagerRejectsBlankCardAuthorization()
    {
        await using var db = CreateContext(); var service = CreateService(db);
        var request = new ManagerPaymentConfirmRequest("credit_card", now, "1234", "   ", 1);
        var error = await Assert.ThrowsAsync<ManagerRuleException>(() => service.ConfirmPaymentAsync(
            managerId, appointmentId, Guid.NewGuid().ToString("N"), request, CancellationToken.None));

        Assert.Equal(400, error.StatusCode);
        Assert.False(await db.Payments.AnyAsync(x => x.AppointmentId == appointmentId));
    }

    private ManagerExperienceService CreateService(ViverAppDbContext db) => new(db, null!, null!, new NoOpAuditWriter(), new FixedClock(now));
    private ViverAppDbContext CreateContext()
    {
        var value = configuration.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("LocalConnection ausente.");
        var builder = new MySqlConnectionStringBuilder(value); if (builder.Database != "viverappweb") throw new InvalidOperationException("Database recusado.");
        return new ViverAppDbContext(new DbContextOptionsBuilder<ViverAppDbContext>().UseMySQL(builder.ConnectionString).Options);
    }
    private async Task CleanupAsync()
    {
        if (configuration is null) return; await using var db = CreateContext();
        var accountIds = await db.Accounts.Where(x => x.Email != null && x.Email.EndsWith("@phase13.example.test")).Select(x => x.Id).ToArrayAsync();
        var appointmentIds = await db.Appointments.Where(x => accountIds.Contains(x.PatientAccountId) || accountIds.Contains(x.DoctorAccountId) || accountIds.Contains(x.CreatedByAccountId)).Select(x => x.Id).ToArrayAsync();
        var paymentIds = await db.Payments.Where(x => appointmentIds.Contains(x.AppointmentId)).Select(x => x.Id).ToArrayAsync();
        await db.PaymentEvents.Where(x => paymentIds.Contains(x.PaymentId)).ExecuteDeleteAsync();
        var idempotencyScopes = accountIds.Select(x => $"manager.payment:{x}").ToArray();
        await db.IdempotencyRecords.Where(x => idempotencyScopes.Contains(x.ScopeCode)).ExecuteDeleteAsync();
        await db.Payments.Where(x => paymentIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.AppointmentStatusHistories.Where(x => appointmentIds.Contains(x.AppointmentId)).ExecuteDeleteAsync();
        await db.Appointments.Where(x => appointmentIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ManagerPreferences.Where(x => accountIds.Contains(x.ManagerAccountId)).ExecuteDeleteAsync();
        await db.DoctorProfiles.Where(x => accountIds.Contains(x.AccountId)).ExecuteDeleteAsync();
        await db.PatientProfiles.Where(x => accountIds.Contains(x.AccountId)).ExecuteDeleteAsync();
        await db.Accounts.Where(x => accountIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.AppointmentTypes.Where(x => x.Name.StartsWith(Marker)).ExecuteDeleteAsync();
    }
    private static Account Account(string email, string role, string name) => new() { RoleCode = role, StatusCode = "active", FullName = name, Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailVerified = true, SecurityStamp = RandomNumberGenerator.GetBytes(32), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, RowVersion = 1 };
    private sealed class NoOpAuditWriter : IClinicalOperationsAuditWriter { public Task WriteAsync(string eventCode, ulong actorAccountId, string entityType, string entityId, IReadOnlyDictionary<string, string>? safeData, CancellationToken cancellationToken) => Task.CompletedTask; }
    private sealed class FixedClock(DateTime value) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(value, DateTimeKind.Utc)); }
}
