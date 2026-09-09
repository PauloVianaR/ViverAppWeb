using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.DoctorExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.ClinicalOperations.Tests;

public sealed class ClinicalOperationsIntegrationTests : IAsyncLifetime
{
    private IConfiguration configuration = null!;
    private ClinicalFixture fixture = null!;

    public async Task InitializeAsync()
    {
        configuration = LoadConfiguration();
        await CleanupAsync(configuration);
        fixture = await CreateFixtureAsync(configuration);
    }

    public Task DisposeAsync() => CleanupAsync(configuration);

    [Fact]
    public async Task DoctorSeesOnlyOwnedAgendaAndPatients_ManagerSeesClinicButNotReportContent()
    {
        await using var context = CreateContext(configuration);
        var service = CreateService(context, fixture.NowUtc);
        var doctorAgenda = await service.GetAppointmentsAsync(
            fixture.DoctorId,
            ViverAppRoles.Doctor,
            DateOnly.FromDateTime(fixture.NowUtc.AddDays(-2)),
            DateOnly.FromDateTime(fixture.NowUtc.AddDays(2)),
            null,
            null,
            null,
            1,
            100,
            CancellationToken.None);
        Assert.Single(doctorAgenda.Items);
        Assert.Equal(fixture.DoctorId, doctorAgenda.Items[0].DoctorAccountId);

        var doctorPatients = await service.GetPatientsAsync(
            fixture.DoctorId, ViverAppRoles.Doctor, null, 1, 100, CancellationToken.None);
        Assert.Single(doctorPatients.Items);
        Assert.Equal(fixture.PatientId, doctorPatients.Items[0].AccountId);

        var managerAgenda = await service.GetAppointmentsAsync(
            fixture.ManagerId,
            ViverAppRoles.Manager,
            DateOnly.FromDateTime(fixture.NowUtc.AddDays(-2)),
            DateOnly.FromDateTime(fixture.NowUtc.AddDays(2)),
            null,
            null,
            null,
            1,
            100,
            CancellationToken.None);
        Assert.Contains(managerAgenda.Items, item => item.Id == fixture.AppointmentId);
        Assert.Contains(managerAgenda.Items, item => item.Id == fixture.OtherAppointmentId);
        Assert.All(managerAgenda.Items, item => Assert.Null(item.PatientNotes));
    }

    [Fact]
    public async Task AssignedDoctorCanDraftThenComplete_PublishedReportIsVisibleToPatientAndRedactedForManager()
    {
        await using var context = CreateContext(configuration);
        var service = CreateService(context, fixture.NowUtc);
        var draft = await service.SaveDraftAsync(
            fixture.DoctorId,
            fixture.AppointmentId,
            new MedicalReportWriteRequest(0, "Paciente avaliado sem sinais de alarme no momento.", "Manter acompanhamento clínico."),
            CancellationToken.None);
        Assert.Equal("draft", draft.StatusCode);
        Assert.True(draft.ContentVisible);
        await context.Appointments.Where(x => x.Id == fixture.AppointmentId)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.StatusCode, "in_progress").SetProperty(a => a.RowVersion, a => a.RowVersion + 1));
        context.ChangeTracker.Clear();

        var completed = await service.CompleteAsync(
            fixture.DoctorId,
            fixture.AppointmentId,
            new CompleteAppointmentRequest(
                2,
                draft.RowVersion,
                "Paciente avaliado sem sinais de alarme no momento.",
                "Manter acompanhamento clínico."),
            CancellationToken.None);
        Assert.Equal("completed", completed.StatusCode);
        Assert.Equal("published", completed.MedicalReport!.StatusCode);

        var managerView = await service.GetAppointmentAsync(
            fixture.ManagerId, ViverAppRoles.Manager, fixture.AppointmentId, CancellationToken.None);
        Assert.False(managerView.MedicalReport!.ContentVisible);
        Assert.Null(managerView.MedicalReport.ClinicalSummary);

        var patientView = await service.GetPublishedPatientReportAsync(
            fixture.PatientId, fixture.AppointmentId, CancellationToken.None);
        Assert.Contains("sem sinais de alarme", patientView.ClinicalSummary, StringComparison.Ordinal);
        Assert.Equal(fixture.AppointmentId, patientView.AppointmentId);

        Assert.Equal(1, await context.AppointmentStatusHistories.CountAsync(
            item => item.AppointmentId == fixture.AppointmentId && item.ToStatusCode == "completed"));
    }

    [Fact]
    public async Task DifferentDoctorCannotReadOrCompleteAnotherDoctorsAppointment()
    {
        await using var context = CreateContext(configuration);
        var service = CreateService(context, fixture.NowUtc);
        var read = await Assert.ThrowsAsync<ClinicalRuleException>(() => service.GetAppointmentAsync(
            fixture.OtherDoctorId, ViverAppRoles.Doctor, fixture.AppointmentId, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.NotFound, read.StatusCode);

        var completion = await Assert.ThrowsAsync<ClinicalRuleException>(() => service.CompleteAsync(
            fixture.OtherDoctorId,
            fixture.AppointmentId,
            new CompleteAppointmentRequest(1, 0, new string('A', 20), null),
            CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.Forbidden, completion.StatusCode);

        var nonClinicalRole = await Assert.ThrowsAsync<ClinicalRuleException>(() => service.GetContextAsync(
            fixture.PatientId, ViverAppRoles.Patient, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.Forbidden, nonClinicalRole.StatusCode);
    }

    [Fact]
    public async Task ManagerCanRecordNoShow_WithOptimisticConcurrencyAndHistory()
    {
        await using var context = CreateContext(configuration);
        var service = CreateService(context, fixture.NowUtc);
        var updated = await service.RecordNoShowAsync(
            fixture.ManagerId,
            ViverAppRoles.Manager,
            fixture.OtherAppointmentId,
            new RecordNoShowRequest(1),
            CancellationToken.None);
        Assert.Equal("no_show", updated.StatusCode);
        Assert.Equal(2UL, updated.RowVersion);
        Assert.Equal(1, await context.AppointmentStatusHistories.CountAsync(
            item => item.AppointmentId == fixture.OtherAppointmentId && item.ToStatusCode == "no_show"));
    }

    [Fact]
    public async Task DoctorExperience_DoesNotExposeAnotherDoctorsAppointmentOrPatient()
    {
        await using var context = CreateContext(configuration);
        var service = new DoctorExperienceService(context, null!, null!, new NoOpAuditWriter(), new FixedTimeProvider(fixture.NowUtc));

        var agenda = await service.AgendaAsync(fixture.DoctorId,
            DateOnly.FromDateTime(fixture.NowUtc.AddDays(-2)), DateOnly.FromDateTime(fixture.NowUtc.AddDays(2)),
            null, null, null, null, 1, 100, CancellationToken.None);
        Assert.Single(agenda.Page.Items);
        Assert.Equal(fixture.AppointmentId, agenda.Page.Items[0].Id);

        var appointment = await Assert.ThrowsAsync<DoctorRuleException>(() =>
            service.AppointmentAsync(fixture.DoctorId, fixture.OtherAppointmentId, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.NotFound, appointment.StatusCode);
        var patient = await Assert.ThrowsAsync<DoctorRuleException>(() =>
            service.PatientAsync(fixture.DoctorId, fixture.OtherPatientId, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.NotFound, patient.StatusCode);
    }

    [Fact]
    public async Task PublishedReport_RectificationCreatesImmutableVersionAndPreservesFirstVersion()
    {
        await using var context = CreateContext(configuration);
        var clinical = CreateService(context, fixture.NowUtc);
        await context.Appointments.Where(x => x.Id == fixture.AppointmentId)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.StatusCode, "in_progress").SetProperty(a => a.RowVersion, a => a.RowVersion + 1));
        context.ChangeTracker.Clear();
        var completed = await clinical.CompleteAsync(fixture.DoctorId, fixture.AppointmentId,
            new CompleteAppointmentRequest(2, 0, "Primeira versão clínica completa e validada.", "Recomendação inicial."), CancellationToken.None);
        var service = new DoctorExperienceService(context, null!, null!, new NoOpAuditWriter(), new FixedTimeProvider(fixture.NowUtc.AddMinutes(5)));
        var versions = await service.RectifyAsync(fixture.DoctorId, fixture.AppointmentId,
            new DoctorReportRectificationRequest(completed.MedicalReport!.RowVersion,
                "Segunda versão clínica completa e devidamente retificada.", "Recomendação atualizada.", "Correção de informação clínica."),
            CancellationToken.None);

        Assert.Equal(2, versions.Count);
        Assert.Equal(2U, versions[0].VersionNumber);
        Assert.Equal("Primeira versão clínica completa e validada.", versions[1].ClinicalSummary);
        Assert.Equal("Correção de informação clínica.", versions[0].ChangeReason);
    }

    private static ClinicalOperationsService CreateService(ViverAppDbContext context, DateTime nowUtc) =>
        new(context, new NoOpAuditWriter(), new FixedTimeProvider(nowUtc));

    private static async Task<ClinicalFixture> CreateFixtureAsync(IConfiguration configuration)
    {
        await using var context = CreateContext(configuration);
        var now = DateTime.UtcNow;
        var marker = Guid.NewGuid().ToString("N");
        var doctor = Account($"doctor-{marker}@phase8.example.test", ViverAppRoles.Doctor, "Dra. Fase Oito");
        var otherDoctor = Account($"other-{marker}@phase8.example.test", ViverAppRoles.Doctor, "Dr. Outro Médico");
        var manager = Account($"manager-{marker}@phase8.example.test", ViverAppRoles.Manager, "Gestora Fase Oito");
        var patient = Account($"patient-{marker}@phase8.example.test", ViverAppRoles.Patient, "Paciente Vinculado");
        var otherPatient = Account($"patient2-{marker}@phase8.example.test", ViverAppRoles.Patient, "Segundo Paciente");
        context.Accounts.AddRange(doctor, otherDoctor, manager, patient, otherPatient);
        await context.SaveChangesAsync();
        context.DoctorProfiles.AddRange(
            DoctorProfile(doctor.Id, "80001"),
            DoctorProfile(otherDoctor.Id, "80002"));
        context.PatientProfiles.AddRange(
            PatientProfile(patient.Id, "Paciente"),
            PatientProfile(otherPatient.Id, "Segundo"));
        var type = new AppointmentType
        {
            Name = $"__phase8_test__{marker}",
            Description = "Teste de operação clínica",
            ModalityCode = "both",
            DurationMinutes = 30,
            PriceAmount = 100,
            IsActive = true,
            DisplayOrder = 900,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        context.AppointmentTypes.Add(type);
        await context.SaveChangesAsync();
        var appointment = Appointment(patient.Id, doctor.Id, type.Id, now.AddHours(-2));
        var otherAppointment = Appointment(otherPatient.Id, otherDoctor.Id, type.Id, now.AddHours(-1));
        context.Appointments.AddRange(appointment, otherAppointment);
        await context.SaveChangesAsync();
        return new ClinicalFixture(
            doctor.Id, otherDoctor.Id, manager.Id, patient.Id, otherPatient.Id,
            appointment.Id, otherAppointment.Id, type.Id, now);
    }

    private static Account Account(string email, string role, string name) => new()
    {
        RoleCode = role,
        StatusCode = "active",
        FullName = name,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailVerified = true,
        SecurityStamp = RandomNumberGenerator.GetBytes(32),
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = 1,
    };

    private static DoctorProfile DoctorProfile(ulong accountId, string license) => new()
    {
        AccountId = accountId,
        LicenseStateCode = "SP",
        LicenseNumber = license,
        DefaultAppointmentDurationMinutes = 30,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = 1,
    };

    private static PatientProfile PatientProfile(ulong accountId, string preferredName) => new()
    {
        AccountId = accountId,
        PreferredName = preferredName,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    };

    private static Appointment Appointment(ulong patientId, ulong doctorId, uint typeId, DateTime startsAt) => new()
    {
        AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63),
        PatientAccountId = patientId,
        DoctorAccountId = doctorId,
        AppointmentTypeId = typeId,
        CreatedByAccountId = patientId,
        StatusCode = "confirmed",
        ModalityCode = "online",
        StartsAtUtc = startsAt,
        EndsAtUtc = startsAt.AddMinutes(30),
        PriceAmount = 100,
        CurrencyCode = "BRL",
        PatientNotes = "Informação clínica restrita ao médico responsável.",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = 1,
    };

    private static IConfiguration LoadConfiguration() => new ConfigurationBuilder()
        .AddUserSecrets<ClinicalOperationsIntegrationTests>()
        .Build();

    private static ViverAppDbContext CreateContext(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:LocalConnection não configurada.");
        var builder = new MySqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.Database, "viverappweb", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Os testes recusaram um database diferente de viverappweb.");
        }

        var options = new DbContextOptionsBuilder<ViverAppDbContext>()
            .UseMySQL(builder.ConnectionString)
            .Options;
        return new ViverAppDbContext(options);
    }

    private static async Task CleanupAsync(IConfiguration configuration)
    {
        await using var context = CreateContext(configuration);
        var ids = await context.Accounts
            .Where(item => item.Email != null && item.Email.EndsWith("@phase8.example.test"))
            .Select(item => item.Id)
            .ToArrayAsync();
        if (ids.Length > 0)
        {
            var appointmentIds = await context.Appointments
                .Where(item => ids.Contains(item.PatientAccountId) || ids.Contains(item.DoctorAccountId))
                .Select(item => item.Id)
                .ToArrayAsync();
            var reportIds = await context.MedicalReports.Where(item => appointmentIds.Contains(item.AppointmentId)).Select(item => item.Id).ToArrayAsync();
            await context.MedicalReportVersions.Where(item => reportIds.Contains(item.MedicalReportId)).ExecuteDeleteAsync();
            await context.MedicalReports.Where(item => appointmentIds.Contains(item.AppointmentId)).ExecuteDeleteAsync();
            await context.AppointmentStatusHistories.Where(item => appointmentIds.Contains(item.AppointmentId)).ExecuteDeleteAsync();
            await context.Appointments.Where(item => appointmentIds.Contains(item.Id)).ExecuteDeleteAsync();
            await context.DoctorWeeklyHours.Where(item => ids.Contains(item.DoctorAccountId)).ExecuteDeleteAsync();
            await context.DoctorAvailabilityExceptions.Where(item => ids.Contains(item.DoctorAccountId)).ExecuteDeleteAsync();
            await context.DoctorServices.Where(item => ids.Contains(item.DoctorAccountId)).ExecuteDeleteAsync();
            await context.DoctorPreferences.Where(item => ids.Contains(item.DoctorAccountId)).ExecuteDeleteAsync();
            await context.DoctorPatientLinks.Where(item => ids.Contains(item.DoctorAccountId) || ids.Contains(item.PatientAccountId)).ExecuteDeleteAsync();
            await context.DoctorSpecialties.Where(item => ids.Contains(item.DoctorAccountId)).ExecuteDeleteAsync();
            await context.DoctorProfiles.Where(item => ids.Contains(item.AccountId)).ExecuteDeleteAsync();
            await context.PatientProfiles.Where(item => ids.Contains(item.AccountId)).ExecuteDeleteAsync();
            await context.Accounts.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
        }

        await context.AppointmentTypes.Where(item => item.Name.StartsWith("__phase8_test__")).ExecuteDeleteAsync();
    }

    private sealed class NoOpAuditWriter : IClinicalOperationsAuditWriter
    {
        public Task WriteAsync(string eventCode, ulong actorAccountId, string entityType, string entityId, IReadOnlyDictionary<string, string>? safeData, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed record ClinicalFixture(
        ulong DoctorId,
        ulong OtherDoctorId,
        ulong ManagerId,
        ulong PatientId,
        ulong OtherPatientId,
        ulong AppointmentId,
        ulong OtherAppointmentId,
        uint AppointmentTypeId,
        DateTime NowUtc);
}
