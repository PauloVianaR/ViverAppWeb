using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.MedicalRecords;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.ClinicalOperations.Tests;

public sealed class MedicalRecordIntegrationTests
{
    [Fact]
    public async Task DoctorDraftFinalizeAndRectify_PreserveVersionsAndRejectCrossRoleWrites()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<MedicalRecordIntegrationTests>().Build();
        await using var context = CreateContext(configuration);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var marker = Guid.NewGuid().ToString("N");
        var doctor = Account($"record-doctor-{marker}@example.test", ViverAppRoles.Doctor, "Dra. Registro");
        var otherDoctor = Account($"record-other-{marker}@example.test", ViverAppRoles.Doctor, "Dr. Sem Vínculo");
        var manager = Account($"record-manager-{marker}@example.test", ViverAppRoles.Manager, "Gestora Registro");
        var administrator = Account($"record-admin-{marker}@example.test", ViverAppRoles.Administrator, "Admin Registro");
        var patient = Account($"record-patient-{marker}@example.test", ViverAppRoles.Patient, "Paciente Registro");
        context.Accounts.AddRange(doctor, otherDoctor, manager, administrator, patient);
        await context.SaveChangesAsync();
        context.DoctorProfiles.AddRange(Doctor(doctor.Id, "18001"), Doctor(otherDoctor.Id, "18002"));
        context.PatientProfiles.Add(new PatientProfile { AccountId = patient.Id, PreferredName = "Paciente", CreatedAtUtc = now, UpdatedAtUtc = now });
        var type = new AppointmentType
        {
            Name = $"__phase18__{marker}",
            Description = "Teste transacional do prontuário",
            ModalityCode = "both",
            DurationMinutes = 30,
            PriceAmount = 200,
            IsActive = true,
            DisplayOrder = 980,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        context.AppointmentTypes.Add(type);
        await context.SaveChangesAsync();
        var appointment = new Appointment
        {
            AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63),
            PatientAccountId = patient.Id,
            DoctorAccountId = doctor.Id,
            AppointmentTypeId = type.Id,
            CreatedByAccountId = manager.Id,
            StatusCode = "in_progress",
            ModalityCode = "in_person",
            StartsAtUtc = now.AddMinutes(-30),
            EndsAtUtc = now,
            PriceAmount = 200,
            CurrencyCode = "BRL",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
            PaymentLocationCode = "clinic",
        };
        context.Appointments.Add(appointment);
        context.DoctorPatientLinks.Add(new DoctorPatientLink
        {
            DoctorAccountId = doctor.Id,
            PatientAccountId = patient.Id,
            CreatedByAccountId = manager.Id,
            StatusCode = "active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        });
        await context.SaveChangesAsync();

        var service = new MedicalRecordService(context, null!, new NoOpAudit(), new FixedTime(now));
        var firstContent = Content("Paciente relata melhora progressiva há duas semanas.");
        var draft = await service.SaveDraftAsync(doctor.Id, patient.Id, appointment.Id,
            new MedicalRecordDraftWriteRequest(0, firstContent), CancellationToken.None);
        Assert.Equal(1UL, draft.RowVersion);
        var stale = await Assert.ThrowsAsync<MedicalRecordRuleException>(() => service.SaveDraftAsync(doctor.Id, patient.Id,
            appointment.Id, new MedicalRecordDraftWriteRequest(0, firstContent), CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.Conflict, stale.StatusCode);

        var finalized = await service.FinalizeAsync(doctor.Id, patient.Id, appointment.Id,
            new MedicalRecordFinalizeRequest(draft.RowVersion), CancellationToken.None);
        Assert.Equal(1U, finalized.VersionNumber);
        Assert.True(finalized.IsCurrent);
        Assert.Equal("completed", (await context.Appointments.FindAsync(appointment.Id))!.StatusCode);

        var managerWithoutPurpose = await Assert.ThrowsAsync<MedicalRecordRuleException>(() => service.EntriesAsync(manager.Id,
            ViverAppRoles.Manager, true, patient.Id, null, true, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.Forbidden, managerWithoutPurpose.StatusCode);
        var administratorWithoutStepUp = await Assert.ThrowsAsync<MedicalRecordRuleException>(() => service.EntriesAsync(administrator.Id,
            ViverAppRoles.Administrator, false, patient.Id, "Auditoria clínica autorizada", true, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.Forbidden, administratorWithoutStepUp.StatusCode);
        var otherDoctorDenied = await Assert.ThrowsAsync<MedicalRecordRuleException>(() => service.SummaryAsync(otherDoctor.Id,
            ViverAppRoles.Doctor, true, patient.Id, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.NotFound, otherDoctorDenied.StatusCode);

        var managerRead = await service.EntriesAsync(manager.Id, ViverAppRoles.Manager, true, patient.Id,
            "Continuidade do atendimento na clínica", true, CancellationToken.None);
        Assert.Single(managerRead);
        var secondContent = firstContent with { ClinicalEvolution = "Paciente sem sinais de alarme e com evolução estável." };
        var rectified = await service.RectifyAsync(doctor.Id, patient.Id, managerRead[0].Id,
            new MedicalRecordRectifyRequest(finalized.Id, "Correção de informação relatada pelo paciente.", secondContent),
            CancellationToken.None);
        Assert.Equal(2U, rectified.VersionNumber);
        Assert.Equal(finalized.Id, rectified.SupersedesVersionId);
        Assert.Equal(2, await context.MedicalRecordVersions.CountAsync(x => x.MedicalRecordEntryId == rectified.EntryId));
        Assert.Equal(3, await context.ClinicalAccessEvents.CountAsync(x => x.PatientAccountId == patient.Id && x.OutcomeCode == "denied"));

        var mutation = await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE medical_record_versions SET chief_complaint = 'alteracao proibida' WHERE id = {finalized.Id}"));
        Assert.Contains("append-only", mutation.ToString(), StringComparison.OrdinalIgnoreCase);
        await transaction.RollbackAsync();
    }

    private static MedicalRecordContent Content(string evolution) => new(
        "Retorno para reavaliação clínica.", "Sintomas iniciados há cerca de duas semanas.", null, null,
        "Nega alergias conhecidas.", "Não informou medicamentos de uso contínuo.", null,
        "Paciente em bom estado geral.", "Quadro clínico em acompanhamento.",
        "Manter orientações e retornar se houver piora.", "Retorno em trinta dias.", evolution, null,
        120, 80, 72, 36.5m, 70m, 170m);

    private static Account Account(string email, string role, string name) => new()
    {
        RoleCode = role,
        StatusCode = "active",
        FullName = name,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailVerified = true,
        PortalAccessEnabled = true,
        SecurityStamp = RandomNumberGenerator.GetBytes(32),
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = 1,
    };

    private static DoctorProfile Doctor(ulong id, string license) => new()
    {
        AccountId = id,
        ProfessionalTitle = "Dr.",
        LicenseStateCode = "MG",
        LicenseNumber = license,
        DefaultAppointmentDurationMinutes = 30,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = 1,
    };

    private static ViverAppDbContext CreateContext(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:LocalConnection não configurada.");
        var builder = new MySqlConnectionStringBuilder(connection);
        if (!string.Equals(builder.Database, "viverappweb", StringComparison.Ordinal))
            throw new InvalidOperationException("O teste recusou banco diferente de viverappweb.");
        return new(new DbContextOptionsBuilder<ViverAppDbContext>().UseMySQL(builder.ConnectionString).Options);
    }

    private sealed class NoOpAudit : IClinicalOperationsAuditWriter
    {
        public Task WriteAsync(string eventCode, ulong actorAccountId, string entityType, string entityId,
            IReadOnlyDictionary<string, string>? safeData, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
