using System.Security.Cryptography;
using System.Security.Claims;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.ArrivalExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.ManagerExperience;
using ViverApp.Api.Features.StaffCommunication;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.ClinicalOperations.Tests;

public sealed class ManagerExperienceIntegrationTests : IAsyncLifetime
{
    private const string Marker = "__phase13_test__";
    private IConfiguration configuration = null!;
    private ulong managerId, doctorId, patientId, appointmentId, offlinePatientId, extraManagerId;
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
        db.ProfessionalProfiles.Add(new ProfessionalProfile { AccountId = doctor.Id, ProfessionalTitle = "Dra.", LicenseTypeCode = "CRM", LicenseStateCode = "SP", LicenseNumber = "130001", DefaultAppointmentDurationMinutes = 30, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 });
        db.PatientProfiles.Add(new PatientProfile { AccountId = patient.Id, PreferredName = "Paciente", CreatedAtUtc = now, UpdatedAtUtc = now });
        var type = new AppointmentType { Name = Marker + Guid.NewGuid().ToString("N"), CategoryCode = "consultation", Description = "Teste do Gestor", ModalityCode = "in_person", DurationMinutes = 30, PriceAmount = 180, RequiresPayment = true, IsActive = true, DisplayOrder = 999, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 };
        db.AppointmentTypes.Add(type); await db.SaveChangesAsync();
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        appointmentLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, timezone)).AddDays(1);
        var startsAtUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(appointmentLocalDate.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Unspecified),
            timezone);
        var appointment = new Appointment { AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63), PatientAccountId = patient.Id, ProfessionalAccountId = doctor.Id, AppointmentTypeId = type.Id, CreatedByAccountId = manager.Id, StatusCode = "pending", ModalityCode = "in_person", StartsAtUtc = startsAtUtc, EndsAtUtc = startsAtUtc.AddMinutes(30), PriceAmount = 180, BasePriceAmount = 180, RequiresPayment = true, DiscountPercent = 0, PaymentLocationCode = "clinic", CurrencyCode = "BRL", PatientNotes = "Observação operacional sintética", CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 };
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
                new ManagerPatientAddressRequest("39900000", null, null, null, null, "Almenara", "MG"),
                false,
                "Paciente prefere atendimento pela manhã."),
            CancellationToken.None);

        Assert.False(patient.PortalAccessEnabled);
        Assert.Equal("active", patient.StatusCode);
        Assert.Null(patient.Email);
        Assert.Null(patient.Phone);
        Assert.Equal(offlinePatientTaxId, patient.TaxId);
        Assert.Equal("39900000", patient.Address?.PostalCode);
        Assert.Null(patient.Address?.Street);
        Assert.Equal("Almenara", patient.Address?.City);
        Assert.Equal("MG", patient.Address?.StateCode);
        Assert.Equal("Paciente prefere atendimento pela manhã.", patient.AdministrativeNotes);
        offlinePatientId = patient.AccountId;
        var persisted = await db.Accounts.SingleAsync(x => x.Id == patient.AccountId);
        Assert.False(persisted.PortalAccessEnabled);
        Assert.Equal("Paciente prefere atendimento pela manhã.", (await db.PatientProfiles.SingleAsync(x => x.AccountId == patient.AccountId)).AdministrativeNotes);
    }

    [Fact]
    public async Task ArrivalIsIdempotentAndCreatesOneDurableProfessionalNotification()
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
        var notification = await verification.ProfessionalNotifications.SingleAsync(x => x.ProfessionalAccountId == doctorId && x.AppointmentId == appointmentId);
        Assert.Equal(1, await verification.AppointmentStatusHistories.CountAsync(x => x.AppointmentId == appointmentId && x.ToStatusCode == "arrived"));

        var service = new ArrivalExperienceService(verification, new FixedClock(now), new NoOpAuditWriter(), new NullHubContext(), NullLogger<ArrivalExperienceService>.Instance);
        var patientName = NotificationPatientName.FirstTwo(await verification.Accounts.Where(a => a.Id == patientId).Select(a => a.FullName).SingleAsync());
        Assert.Equal(patientName, (await service.NotificationsAsync(doctorId, 1, 10, default)).Items.Single(x => x.Id == notification.Id).PatientName);
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
        Assert.NotNull(await verification.ProfessionalNotifications
            .Where(x => x.ProfessionalAccountId == doctorId && x.AppointmentId == appointmentId)
            .Select(x => x.ReadAtUtc)
            .SingleAsync());
        Assert.Contains(await verification.AppointmentStatusHistories.Where(x => x.AppointmentId == appointmentId).ToArrayAsync(),
            x => x.FromStatusCode == "arrived" && x.ToStatusCode == "confirmed"
                && x.Reason!.Contains("Paciente desistiu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WebSocketsDeliverArrivalToAssignedProfessionalAndStartToEveryManager()
    {
        await using var db = CreateContext();
        var otherManager = Account($"manager-{Guid.NewGuid():N}@phase31.example.test", ViverAppRoles.Manager, "Gestora WebSocket Dois");
        db.Accounts.Add(otherManager);
        await db.SaveChangesAsync();
        extraManagerId = otherManager.Id;
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, timezone));
        var start = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localDate.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Unspecified), timezone);
        await db.Appointments.Where(x => x.Id == appointmentId).ExecuteUpdateAsync(x => x
            .SetProperty(a => a.StatusCode, "confirmed").SetProperty(a => a.StartsAtUtc, start)
            .SetProperty(a => a.EndsAtUtc, start.AddMinutes(30)));

        var presence = new StaffPresence();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<ViverAppDbContext>(options =>
            options.UseMySQL(configuration.GetConnectionString("LocalConnection")!));
        builder.Services.AddSingleton(presence);
        builder.Services.AddSignalR();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, WebSocketTestAuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(ViverAppPolicies.ClinicalProfessional,
            policy => policy.RequireRole(ViverAppRoles.Doctor, ViverAppRoles.Psychologist)));
        await using var app = builder.Build();
        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<ProfessionalNotificationsHub>("/hubs/doctor-notifications");
        app.MapHub<StaffChatHub>("/hubs/staff-chat");
        await app.StartAsync();
        var server = app.GetTestServer();
        using var doctorSocket = await ConnectHubAsync(server, "/hubs/doctor-notifications", doctorId, ViverAppRoles.Doctor);
        using var doctorChatSocket = await ConnectHubAsync(server, "/hubs/staff-chat", doctorId, ViverAppRoles.Doctor);
        using var managerSocket = await ConnectHubAsync(server, "/hubs/staff-chat", managerId, ViverAppRoles.Manager);
        using var otherManagerSocket = await ConnectHubAsync(server, "/hubs/staff-chat", extraManagerId, ViverAppRoles.Manager);
        for (var attempt = 0; attempt < 50 && (!presence.Online(managerId) || !presence.Online(extraManagerId)); attempt++)
            await Task.Delay(20);
        Assert.True(presence.Online(managerId));
        Assert.True(presence.Online(extraManagerId));
        await Task.Delay(50);

        var staff = new StaffCommunicationService(db, presence, new FixedClock(now),
            app.Services.GetRequiredService<IHubContext<StaffChatHub>>(), NullLogger<StaffCommunicationService>.Instance);
        Assert.Contains(await staff.ContactsAsync(managerId, CancellationToken.None), c => c.Id == doctorId && c.Online);
        Assert.Contains(await staff.ContactsAsync(doctorId, CancellationToken.None), c => c.Id == managerId && c.Online);
        var privateMessage = await staff.SendAsync(managerId,
            new SendStaffMessageRequest(extraManagerId, Guid.NewGuid(), "Mensagem privada ao segundo gestor"), CancellationToken.None);
        using var extraDelivery = await ReadInvocationAsync(otherManagerSocket, "StaffMessage");
        Assert.Equal(privateMessage.Id, extraDelivery.RootElement.GetProperty("arguments")[0].GetProperty("id").GetUInt64());
        var intendedMessage = await staff.SendAsync(managerId,
            new SendStaffMessageRequest(doctorId, Guid.NewGuid(), "Mensagem privada ao médico"), CancellationToken.None);
        using var doctorDelivery = await ReadInvocationAsync(doctorChatSocket, "StaffMessage");
        // Se a mensagem do outro destinatário vazasse para o médico, seria a primeira recebida.
        Assert.Equal(intendedMessage.Id, doctorDelivery.RootElement.GetProperty("arguments")[0].GetProperty("id").GetUInt64());
        var service = new ArrivalExperienceService(db, new FixedClock(now), new NoOpAuditWriter(),
            app.Services.GetRequiredService<IHubContext<ProfessionalNotificationsHub>>(),
            NullLogger<ArrivalExperienceService>.Instance, staff);
        var arrived = await service.RegisterAsync(managerId, appointmentId, new ArrivalRequest(1), CancellationToken.None);
        using var arrivalMessage = await ReadInvocationAsync(doctorSocket, "PatientArrived");
        Assert.Equal(appointmentId, arrivalMessage.RootElement.GetProperty("arguments")[0].GetProperty("appointmentId").GetUInt64());
        var patientName = NotificationPatientName.FirstTwo(await db.Accounts.Where(a => a.Id == patientId).Select(a => a.FullName).SingleAsync());
        Assert.Equal(patientName, arrivalMessage.RootElement.GetProperty("arguments")[0].GetProperty("patientName").GetString());

        await service.StartAsync(doctorId, appointmentId, new StartAppointmentRequest(arrived.RowVersion), CancellationToken.None);
        using var startMessage = await ReadInvocationAsync(managerSocket, "ManagerAlert");
        var alert = startMessage.RootElement.GetProperty("arguments")[0];
        Assert.Equal("appointment_started", alert.GetProperty("type").GetString());
        Assert.Equal(appointmentId, alert.GetProperty("appointmentId").GetUInt64());
        Assert.Equal(patientName, alert.GetProperty("patientName").GetString());
        using var otherStartMessage = await ReadInvocationAsync(otherManagerSocket, "ManagerAlert");
        Assert.Equal("appointment_started", otherStartMessage.RootElement.GetProperty("arguments")[0].GetProperty("type").GetString());
        await doctorSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Teste concluído", CancellationToken.None);
        await doctorChatSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Teste concluído", CancellationToken.None);
        await managerSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Teste concluído", CancellationToken.None);
        await otherManagerSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Teste concluído", CancellationToken.None);
        for (var attempt = 0; attempt < 50 && (presence.Online(managerId) || presence.Online(extraManagerId)); attempt++)
            await Task.Delay(20);
        Assert.False(presence.Online(managerId));
        Assert.False(presence.Online(extraManagerId));
    }

    private static async Task<WebSocket> ConnectHubAsync(TestServer server, string path, ulong accountId, string role)
    {
        var socket = await server.CreateWebSocketClient().ConnectAsync(
            new Uri($"ws://localhost{path}?account={accountId}&role={role}"), CancellationToken.None);
        var handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e");
        await socket.SendAsync(handshake, WebSocketMessageType.Text, true, CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[4096];
        var received = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.Contains("{}", Encoding.UTF8.GetString(buffer, 0, received.Count));
        return socket;
    }

    private static async Task<JsonDocument> ReadInvocationAsync(WebSocket socket, string target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[16384];
        while (true)
        {
            var received = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.NotEqual(WebSocketMessageType.Close, received.MessageType);
            foreach (var frame in Encoding.UTF8.GetString(buffer, 0, received.Count).Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
            {
                var document = JsonDocument.Parse(frame);
                if (document.RootElement.TryGetProperty("target", out var name) && name.GetString() == target)
                    return document;
                document.Dispose();
            }
        }
    }

    private sealed class WebSocketTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var account = Request.Query["account"].ToString();
            var role = Request.Query["role"].ToString();
            if (!ulong.TryParse(account, out _) || role is not (ViverAppRoles.Doctor or ViverAppRoles.Manager))
                return Task.FromResult(AuthenticateResult.Fail("Identidade de teste inválida."));
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account), new Claim(ClaimTypes.Role, role)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
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

    [Fact]
    public async Task DoctorCanStartConfirmedUndoAndManagementCanCompleteThenReopen()
    {
        await using var db = CreateContext();
        await db.Appointments.Where(item => item.Id == appointmentId).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.StatusCode, "confirmed")
            .SetProperty(item => item.RowVersion, 1UL));
        var service = new ArrivalExperienceService(db, new FixedClock(now), new NoOpAuditWriter(),
            new NullHubContext(), NullLogger<ArrivalExperienceService>.Instance);

        var started = await service.StartAsync(doctorId, appointmentId, new StartAppointmentRequest(1), CancellationToken.None);
        Assert.Equal("in_progress", started.StatusCode);
        var restored = await service.RevertStartAsync(doctorId, appointmentId,
            new AppointmentTransitionRequest(started.RowVersion), CancellationToken.None);
        Assert.Equal("confirmed", restored.StatusCode);
        var completed = await service.CompleteForManagementAsync(managerId, appointmentId,
            new AppointmentTransitionRequest(restored.RowVersion), CancellationToken.None);
        Assert.Equal("completed", completed.StatusCode);
        var reopened = await service.ReopenAsync(managerId, appointmentId,
            new AppointmentReopenRequest(completed.RowVersion, "Correção operacional autorizada"), CancellationToken.None);
        Assert.Equal("confirmed", reopened.StatusCode);
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
        var accountIds = new[] { managerId, doctorId, patientId, offlinePatientId, extraManagerId }.Where(x => x != 0).Distinct().ToArray();
        if (accountIds.Length == 0) return;
        var allAppointmentIds = await db.Appointments.Where(x => accountIds.Contains(x.PatientAccountId) || accountIds.Contains(x.ProfessionalAccountId) || accountIds.Contains(x.CreatedByAccountId)).Select(x => x.Id).ToArrayAsync();
        var retainedAppointmentIds = await db.CashMovements.Where(x => x.AppointmentId.HasValue && allAppointmentIds.Contains(x.AppointmentId.Value)).Select(x => x.AppointmentId!.Value).Distinct().ToArrayAsync();
        var retainedAccounts = await db.Appointments.Where(x => retainedAppointmentIds.Contains(x.Id)).Select(x => new { x.PatientAccountId, x.ProfessionalAccountId, x.CreatedByAccountId }).ToArrayAsync();
        var retainedAccountIds = retainedAccounts.SelectMany(x => new[] { x.PatientAccountId, x.ProfessionalAccountId, x.CreatedByAccountId }).Distinct().ToArray();
        var appointmentIds = allAppointmentIds.Except(retainedAppointmentIds).ToArray();
        var deletableAccountIds = accountIds.Except(retainedAccountIds).ToArray();
        var paymentIds = await db.Payments.Where(x => appointmentIds.Contains(x.AppointmentId)).Select(x => x.Id).ToArrayAsync();
        await db.PaymentEvents.Where(x => paymentIds.Contains(x.PaymentId)).ExecuteDeleteAsync();
        var idempotencyScopes = deletableAccountIds.Select(x => $"manager.payment:{x}").ToArray();
        await db.IdempotencyRecords.Where(x => idempotencyScopes.Contains(x.ScopeCode)).ExecuteDeleteAsync();
        await db.Appointments.Where(x => appointmentIds.Contains(x.Id)).ExecuteUpdateAsync(update => update.SetProperty(x => x.CurrentPaymentId, (ulong?)null));
        await db.Payments.Where(x => paymentIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ProfessionalNotifications.Where(x => appointmentIds.Contains(x.AppointmentId)).ExecuteDeleteAsync();
        await db.ManagerNotifications.Where(x => appointmentIds.Contains(x.AppointmentId)).ExecuteDeleteAsync();
        await db.AppointmentStatusHistories.Where(x => appointmentIds.Contains(x.AppointmentId)).ExecuteDeleteAsync();
        await db.Appointments.Where(x => appointmentIds.Contains(x.Id) && x.RescheduledFromAppointmentId != null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RescheduledFromAppointmentId, (ulong?)null));
        await db.Appointments.Where(x => appointmentIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.ManagerPreferences.Where(x => deletableAccountIds.Contains(x.ManagerAccountId)).ExecuteDeleteAsync();
        await db.StaffMessages.Where(x => deletableAccountIds.Contains(x.SenderAccountId) || deletableAccountIds.Contains(x.RecipientAccountId)).ExecuteDeleteAsync();
        await db.ProfessionalProfiles.Where(x => deletableAccountIds.Contains(x.AccountId)).ExecuteDeleteAsync();
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
    private sealed class NullHubContext : IHubContext<ProfessionalNotificationsHub>
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
