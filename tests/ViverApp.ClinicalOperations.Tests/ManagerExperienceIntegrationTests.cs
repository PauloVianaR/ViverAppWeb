using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.SignalR;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.ArrivalExperience;
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
    private ulong managerId, doctorId, patientId, appointmentId, offlinePatientId;
    private string offlinePatientTaxId = null!;
    private DateTime now;
    private DateOnly appointmentLocalDate;

    public async Task InitializeAsync()
    {
        configuration = new ConfigurationBuilder().AddUserSecrets<ManagerExperienceIntegrationTests>().Build();
        now = DateTime.UtcNow;
        offlinePatientTaxId = CreateCpf();
        await using var db = CreateContext();
        var manager = Account($"manager-{Guid.NewGuid():N}@phase13.example.test", ViverAppRoles.Manager, "Gestora Fase Treze");
        var doctor = Account($"doctor-{Guid.NewGuid():N}@phase13.example.test", ViverAppRoles.Doctor, "Dra. Operação");
        var patient = Account($"patient-{Guid.NewGuid():N}@phase13.example.test", ViverAppRoles.Patient, "Paciente Operacional");
        db.Accounts.AddRange(manager, doctor, patient); await db.SaveChangesAsync(); managerId = manager.Id;
        doctorId = doctor.Id; patientId = patient.Id;
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
        var appointment = new Appointment { AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63), PatientAccountId = patient.Id, DoctorAccountId = doctor.Id, AppointmentTypeId = type.Id, CreatedByAccountId = manager.Id, StatusCode = "pending", ModalityCode = "in_person", StartsAtUtc = startsAtUtc, EndsAtUtc = startsAtUtc.AddMinutes(30), PriceAmount = 180, BasePriceAmount = 180, DiscountPercent = 0, PaymentLocationCode = "clinic", CurrencyCode = "BRL", PatientNotes = "Observação operacional sintética", CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 };
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
            null, null, new TimeOnly(9, 30), new TimeOnly(10, 30), null, "date_asc", 1, 20, CancellationToken.None);
        var excluded = await service.AgendaAsync(appointmentLocalDate, appointmentLocalDate, null, null, null, null,
            null, null, new TimeOnly(11, 0), new TimeOnly(12, 0), null, "date_asc", 1, 20, CancellationToken.None);

        Assert.Contains(included.Page.Items, x => x.Id == appointmentId);
        Assert.DoesNotContain(excluded.Page.Items, x => x.Id == appointmentId);
        var byNumber = await service.AgendaAsync(appointmentLocalDate, appointmentLocalDate, null, null, null, null,
            included.Page.Items.Single(x => x.Id == appointmentId).AppointmentNumber, null, null, null, null, "date_asc", 1, 20, CancellationToken.None);
        Assert.Single(byNumber.Page.Items);
        Assert.Equal(appointmentId, byNumber.Page.Items[0].Id);
        Assert.Contains(byNumber.Page.Items[0].AppointmentNumber, byNumber.Sources.Total);
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

    [Fact]
    public async Task ManagerCanRegisterAClinicPatientWithoutContactOrPortalAccess()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        var patient = await service.CreatePatientAsync(managerId,
            new ManagerPatientCreateRequest(
                "Paciente somente da clínica",
                "Paciente interno",
                offlinePatientTaxId,
                new DateOnly(1990, 5, 20),
                null,
                null,
                null,
                false),
            CancellationToken.None);

        Assert.False(patient.PortalAccessEnabled);
        Assert.Equal("active", patient.StatusCode);
        Assert.Null(patient.Email);
        Assert.Null(patient.Phone);
        Assert.Equal(offlinePatientTaxId, patient.TaxId);
        offlinePatientId = patient.AccountId;
        var persisted = await db.Accounts.SingleAsync(x => x.Id == patient.AccountId);
        Assert.False(persisted.PortalAccessEnabled);
    }

    [Fact]
    public async Task ArrivalIsIdempotentAndCreatesOneDurableDoctorNotification()
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, timezone);
        var farTime = localNow.Hour < 12 ? new TimeOnly(23, 30) : new TimeOnly(0, 30);
        var sameDayFarFromScheduledTime = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(DateOnly.FromDateTime(localNow).ToDateTime(farTime), DateTimeKind.Unspecified), timezone);
        await using (var setup = CreateContext())
            await setup.Appointments.Where(x => x.Id == appointmentId).ExecuteUpdateAsync(x => x
                .SetProperty(a => a.StatusCode, "confirmed").SetProperty(a => a.StartsAtUtc, sameDayFarFromScheduledTime)
                .SetProperty(a => a.EndsAtUtc, sameDayFarFromScheduledTime.AddMinutes(30)));
        async Task<ArrivalResponse> Register()
        {
            await using var context = CreateContext();
            return await new ArrivalExperienceService(context, new FixedClock(now), new NoOpAuditWriter(), new NullHubContext(), NullLogger<ArrivalExperienceService>.Instance)
                .RegisterAsync(managerId, appointmentId, new ArrivalRequest(1), CancellationToken.None);
        }
        var arrivals = await Task.WhenAll(Register(), Register());
        var first = arrivals[0]; var replay = arrivals[1];

        Assert.Equal("arrived", first.StatusCode);
        Assert.Equal(first.QueueNumber, replay.QueueNumber);
        Assert.True(first.QueueNumber >= 100);
        await using var verification = CreateContext();
        var notification = await verification.DoctorNotifications.SingleAsync(x => x.DoctorAccountId == doctorId && x.AppointmentId == appointmentId);
        Assert.Equal(1, await verification.AppointmentStatusHistories.CountAsync(x => x.AppointmentId == appointmentId && x.ToStatusCode == "arrived"));

        var service = new ArrivalExperienceService(verification, new FixedClock(now), new NoOpAuditWriter(), new NullHubContext(), NullLogger<ArrivalExperienceService>.Instance);
        var unreadForAnotherDoctor = await service.NotificationsAsync(doctorId + 1, 1, 10, CancellationToken.None);
        Assert.Empty(unreadForAnotherDoctor.Items);
        var readDenied = await Assert.ThrowsAsync<ArrivalRuleException>(() =>
            service.ReadAsync(doctorId + 1, notification.Id, notification.RowVersion, CancellationToken.None));
        Assert.Equal(404, readDenied.StatusCode);
        var startDenied = await Assert.ThrowsAsync<ArrivalRuleException>(() =>
            service.StartAsync(doctorId + 1, appointmentId, new StartAppointmentRequest(first.RowVersion), CancellationToken.None));
        Assert.Equal(404, startDenied.StatusCode);

        var canceled = await service.CancelAsync(managerId, appointmentId,
            new ArrivalCancellationRequest(first.RowVersion, "Paciente desistiu de aguardar"), CancellationToken.None);
        Assert.Equal("confirmed", canceled.StatusCode);
        Assert.Null(canceled.ArrivedAtUtc);
        Assert.Null(canceled.BusinessDate);
        Assert.Null(canceled.QueueNumber);
        verification.ChangeTracker.Clear();
        Assert.NotNull(await verification.DoctorNotifications
            .Where(x => x.DoctorAccountId == doctorId && x.AppointmentId == appointmentId)
            .Select(x => x.ReadAtUtc)
            .SingleAsync());
        Assert.Contains(await verification.AppointmentStatusHistories.Where(x => x.AppointmentId == appointmentId).ToArrayAsync(),
            x => x.FromStatusCode == "arrived" && x.ToStatusCode == "confirmed"
                && x.Reason!.Contains("Paciente desistiu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArrivalRejectsOnlyAnotherClinicDay()
    {
        await using var setup = CreateContext();
        await setup.Appointments.Where(x => x.Id == appointmentId).ExecuteUpdateAsync(x => x
            .SetProperty(a => a.StatusCode, "confirmed").SetProperty(a => a.StartsAtUtc, now.AddDays(1))
            .SetProperty(a => a.EndsAtUtc, now.AddDays(1).AddMinutes(30)));
        var service = new ArrivalExperienceService(setup, new FixedClock(now), new NoOpAuditWriter(), new NullHubContext(), NullLogger<ArrivalExperienceService>.Instance);
        var error = await Assert.ThrowsAsync<ArrivalRuleException>(() =>
            service.RegisterAsync(managerId, appointmentId, new ArrivalRequest(1), CancellationToken.None));
        Assert.Equal(409, error.StatusCode);
        Assert.Contains("data agendada", error.Message, StringComparison.Ordinal);
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
        var accountIds = new[] { managerId, doctorId, patientId, offlinePatientId }.Where(x => x != 0).Distinct().ToArray();
        if (accountIds.Length == 0) return;
        var allAppointmentIds = await db.Appointments.Where(x => accountIds.Contains(x.PatientAccountId) || accountIds.Contains(x.DoctorAccountId) || accountIds.Contains(x.CreatedByAccountId)).Select(x => x.Id).ToArrayAsync();
        var retainedAppointmentIds = await db.CashMovements.Where(x => x.AppointmentId.HasValue && allAppointmentIds.Contains(x.AppointmentId.Value)).Select(x => x.AppointmentId!.Value).Distinct().ToArrayAsync();
        var retainedAccounts = await db.Appointments.Where(x => retainedAppointmentIds.Contains(x.Id)).Select(x => new { x.PatientAccountId, x.DoctorAccountId, x.CreatedByAccountId }).ToArrayAsync();
        var retainedAccountIds = retainedAccounts.SelectMany(x => new[] { x.PatientAccountId, x.DoctorAccountId, x.CreatedByAccountId }).Distinct().ToArray();
        var appointmentIds = allAppointmentIds.Except(retainedAppointmentIds).ToArray();
        var deletableAccountIds = accountIds.Except(retainedAccountIds).ToArray();
        var paymentIds = await db.Payments.Where(x => appointmentIds.Contains(x.AppointmentId)).Select(x => x.Id).ToArrayAsync();
        await db.PaymentEvents.Where(x => paymentIds.Contains(x.PaymentId)).ExecuteDeleteAsync();
        var idempotencyScopes = deletableAccountIds.Select(x => $"manager.payment:{x}").ToArray();
        await db.IdempotencyRecords.Where(x => idempotencyScopes.Contains(x.ScopeCode)).ExecuteDeleteAsync();
        await db.Appointments.Where(x => appointmentIds.Contains(x.Id)).ExecuteUpdateAsync(update => update.SetProperty(x => x.CurrentPaymentId, (ulong?)null));
        await db.Payments.Where(x => paymentIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.DoctorNotifications.Where(x => appointmentIds.Contains(x.AppointmentId)).ExecuteDeleteAsync();
        await db.AppointmentStatusHistories.Where(x => appointmentIds.Contains(x.AppointmentId)).ExecuteDeleteAsync();
        await db.Appointments.Where(x => appointmentIds.Contains(x.Id) && x.RescheduledFromAppointmentId != null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RescheduledFromAppointmentId, (ulong?)null));
        await db.Appointments.Where(x => appointmentIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ManagerPreferences.Where(x => deletableAccountIds.Contains(x.ManagerAccountId)).ExecuteDeleteAsync();
        await db.DoctorProfiles.Where(x => deletableAccountIds.Contains(x.AccountId)).ExecuteDeleteAsync();
        await db.PatientProfiles.Where(x => deletableAccountIds.Contains(x.AccountId)).ExecuteDeleteAsync();
        await db.Accounts.Where(x => deletableAccountIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.AppointmentTypes.Where(x => x.Name.StartsWith(Marker) && !x.Appointments.Any()).ExecuteDeleteAsync();
    }
    private static string CreateCpf()
    {
        string seed;
        do seed = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        while (seed.Distinct().Count() == 1);
        static int Digit(string value, int weight)
        {
            var sum = value.Select((character, index) => (character - '0') * (weight - index)).Sum();
            var remainder = sum % 11;
            return remainder < 2 ? 0 : 11 - remainder;
        }
        var first = Digit(seed, 10);
        return $"{seed}{first}{Digit(seed + first, 11)}";
    }
    private static Account Account(string email, string role, string name) => new() { RoleCode = role, StatusCode = "active", FullName = name, Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailVerified = true, SecurityStamp = RandomNumberGenerator.GetBytes(32), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, RowVersion = 1 };
    private sealed class NoOpAuditWriter : IClinicalOperationsAuditWriter { public Task WriteAsync(string eventCode, ulong actorAccountId, string entityType, string entityId, IReadOnlyDictionary<string, string>? safeData, CancellationToken cancellationToken) => Task.CompletedTask; }
    private sealed class FixedClock(DateTime value) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(value, DateTimeKind.Utc)); }
    private sealed class NullHubContext : IHubContext<DoctorNotificationsHub>
    {
        public IHubClients Clients { get; } = new NullHubClients();
        public IGroupManager Groups { get; } = new NullGroupManager();
    }
    private sealed class NullHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NullClientProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }
    private sealed class NullClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class NullGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
