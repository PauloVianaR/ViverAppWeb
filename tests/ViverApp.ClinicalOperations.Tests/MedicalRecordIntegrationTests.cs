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
        var psychologist = Account($"record-psychologist-{marker}@example.test", ViverAppRoles.Psychologist, "Psicóloga Vinculada");
        var otherPsychologist = Account($"record-other-psychologist-{marker}@example.test", ViverAppRoles.Psychologist, "Psicóloga Sem Vínculo");
        var manager = Account($"record-manager-{marker}@example.test", ViverAppRoles.Manager, "Gestora Registro");
        var administrator = Account($"record-admin-{marker}@example.test", ViverAppRoles.Administrator, "Admin Registro");
        var patient = Account($"record-patient-{marker}@example.test", ViverAppRoles.Patient, "Paciente Registro");
        context.Accounts.AddRange(doctor, otherDoctor, psychologist, otherPsychologist, manager, administrator, patient);
        await context.SaveChangesAsync();
        context.ProfessionalProfiles.AddRange(Doctor(doctor.Id, "18001"), Doctor(otherDoctor.Id, "18002"));
        var psychologistProfile = Doctor(psychologist.Id, "18003");
        psychologistProfile.LicenseTypeCode = "CRP";
        var otherPsychologistProfile = Doctor(otherPsychologist.Id, "18004");
        otherPsychologistProfile.LicenseTypeCode = "CRP";
        context.ProfessionalProfiles.AddRange(psychologistProfile, otherPsychologistProfile);
        context.PatientProfiles.Add(new PatientProfile { AccountId = patient.Id, PreferredName = "Paciente", CreatedAtUtc = now, UpdatedAtUtc = now });
        var type = new AppointmentType
        {
            Name = $"__phase18__{marker}",
            Description = "Teste transacional do prontuário",
            ModalityCode = "both",
            DurationMinutes = 30,
            PriceAmount = 200,
            RequiresPayment = true,
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
            ProfessionalAccountId = doctor.Id,
            AppointmentTypeId = type.Id,
            CreatedByAccountId = manager.Id,
            StatusCode = "in_progress",
            ModalityCode = "in_person",
            StartsAtUtc = now.AddMinutes(-30),
            EndsAtUtc = now,
            PriceAmount = 200,
            RequiresPayment = true,
            CurrencyCode = "BRL",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
            PaymentLocationCode = "clinic",
        };
        var pendingAppointment = new Appointment
        {
            AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63),
            PatientAccountId = patient.Id,
            ProfessionalAccountId = doctor.Id,
            AppointmentTypeId = type.Id,
            CreatedByAccountId = manager.Id,
            StatusCode = "pending",
            ModalityCode = "in_person",
            StartsAtUtc = now.AddDays(1),
            EndsAtUtc = now.AddDays(1).AddMinutes(30),
            PriceAmount = 200,
            RequiresPayment = true,
            CurrencyCode = "BRL",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
            PaymentLocationCode = "clinic",
        };
        context.Appointments.AddRange(appointment, pendingAppointment);
        context.ProfessionalPatientLinks.Add(new ProfessionalPatientLink
        {
            ProfessionalAccountId = doctor.Id,
            PatientAccountId = patient.Id,
            CreatedByAccountId = manager.Id,
            StatusCode = "active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        });
        context.ProfessionalPatientLinks.Add(new ProfessionalPatientLink
        {
            ProfessionalAccountId = psychologist.Id,
            PatientAccountId = patient.Id,
            CreatedByAccountId = manager.Id,
            StatusCode = "active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        });
        await context.SaveChangesAsync();

        var service = new MedicalRecordService(context, null!, new NoOpAudit(), new FixedTime(now));
        var selectableAppointments = await service.AppointmentsAsync(
            doctor.Id, ViverAppRoles.Doctor, true, patient.Id, CancellationToken.None);
        Assert.Single(selectableAppointments);
        Assert.Equal(appointment.Id, selectableAppointments[0].Id);

        var ophthalmologySpecialty = await context.Specialties.SingleAsync(
            x => x.NormalizedName == "OFTALMOLOGIA");
        context.ProfessionalSpecialties.Add(new ProfessionalSpecialty
        {
            ProfessionalAccountId = doctor.Id,
            SpecialtyId = ophthalmologySpecialty.Id,
            IsPrimary = true,
        });
        await context.SaveChangesAsync();
        var appointmentReports = await service.AppointmentReportsAsync(
            manager.Id, ViverAppRoles.Manager, true, patient.Id, 1, 50, CancellationToken.None);
        Assert.Single(appointmentReports.Items);
        Assert.Equal(appointment.Id, appointmentReports.Items[0].Id);
        Assert.True(appointmentReports.Items[0].IsOphthalmology);
        Assert.DoesNotContain(appointmentReports.Items, x => x.Id == pendingAppointment.Id);
        var appointmentSnapshot = await service.AppointmentReportPdfAsync(
            manager.Id, ViverAppRoles.Manager, true, patient.Id, appointment.Id, CancellationToken.None);
        var pdfBytes = new ClinicalPdfRenderer().RenderAppointment(appointmentSnapshot);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdfBytes[..4]));
        var populatedPdf = new ClinicalPdfRenderer().RenderAppointment(appointmentSnapshot with
        {
            Appointment = appointmentSnapshot.Appointment with
            {
                Ophthalmology = new OphthalmologyReportFields(
                    "Histórico clínico da consulta", "OD 20/20", null, null, null, null),
            },
        });
        Assert.Contains("Acuidade visual: OD 20/20", System.Text.Encoding.Latin1.GetString(populatedPdf));
        var administratorReports = await service.AppointmentReportsAsync(
            administrator.Id, ViverAppRoles.Administrator, true, patient.Id, 1, 50, CancellationToken.None);
        Assert.Single(administratorReports.Items);
        var pendingPdf = await Assert.ThrowsAsync<MedicalRecordRuleException>(() =>
            service.AppointmentReportPdfAsync(manager.Id, ViverAppRoles.Manager, true,
                patient.Id, pendingAppointment.Id, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.NotFound, pendingPdf.StatusCode);
        var adminWithoutStepUpPdf = await Assert.ThrowsAsync<MedicalRecordRuleException>(() =>
            service.AppointmentReportPdfAsync(administrator.Id, ViverAppRoles.Administrator, false,
                patient.Id, appointment.Id, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.Forbidden, adminWithoutStepUpPdf.StatusCode);

        var firstContent = OptionalContent();
        var draft = await service.SaveDraftAsync(doctor.Id, ViverAppRoles.Doctor, patient.Id, appointment.Id,
            new MedicalRecordDraftWriteRequest(0, firstContent), CancellationToken.None);
        Assert.Equal(1UL, draft.RowVersion);
        Assert.Null(draft.Content.ChiefComplaint);
        Assert.Equal((ushort)120, draft.Content.SystolicPressureMmhg);
        Assert.Equal((ushort)80, draft.Content.DiastolicPressureMmhg);
        Assert.Null(draft.Content.HeartRateBpm);
        Assert.Equal(170m, draft.Content.HeightCm);
        var stale = await Assert.ThrowsAsync<MedicalRecordRuleException>(() => service.SaveDraftAsync(doctor.Id, ViverAppRoles.Doctor, patient.Id,
            appointment.Id, new MedicalRecordDraftWriteRequest(0, firstContent), CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.Conflict, stale.StatusCode);

        var finalized = await service.FinalizeAsync(doctor.Id, ViverAppRoles.Doctor, patient.Id, appointment.Id,
            new MedicalRecordFinalizeRequest(draft.RowVersion), CancellationToken.None);
        Assert.Equal(1U, finalized.VersionNumber);
        Assert.True(finalized.IsCurrent);
        Assert.Equal("in_progress", (await context.Appointments.FindAsync(appointment.Id))!.StatusCode);

        var managerWithoutPurpose = await service.EntriesAsync(manager.Id,
            ViverAppRoles.Manager, true, patient.Id, null, true, CancellationToken.None);
        Assert.Single(managerWithoutPurpose);
        Assert.Equal(ViverAppRoles.Doctor, managerWithoutPurpose[0].AuthorRoleCode);
        var administratorWithoutStepUp = await Assert.ThrowsAsync<MedicalRecordRuleException>(() => service.EntriesAsync(administrator.Id,
            ViverAppRoles.Administrator, false, patient.Id, "Auditoria clínica autorizada", true, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.Forbidden, administratorWithoutStepUp.StatusCode);
        var otherDoctorDenied = await Assert.ThrowsAsync<MedicalRecordRuleException>(() => service.SummaryAsync(otherDoctor.Id,
            ViverAppRoles.Doctor, true, patient.Id, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.NotFound, otherDoctorDenied.StatusCode);
        var psychologistSummary = await service.SummaryAsync(psychologist.Id,
            ViverAppRoles.Psychologist, true, patient.Id, CancellationToken.None);
        Assert.Equal(patient.Id, psychologistSummary.PatientAccountId);
        var otherPsychologistDenied = await Assert.ThrowsAsync<MedicalRecordRuleException>(() => service.SummaryAsync(
            otherPsychologist.Id, ViverAppRoles.Psychologist, true, patient.Id, CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.NotFound, otherPsychologistDenied.StatusCode);
        Assert.Equal(2, await context.ClinicalAccessEvents.CountAsync(x => x.PatientAccountId == patient.Id
            && x.ActorRoleCode == ViverAppRoles.Psychologist));

        var managerRead = await service.EntriesAsync(manager.Id, ViverAppRoles.Manager, true, patient.Id,
            "Continuidade do atendimento na clínica", true, CancellationToken.None);
        Assert.Single(managerRead);
        await context.ApplicationSettings.Where(x => x.SettingKey == "manager.medical_records_write_enabled")
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.ValueJson, "false"));
        var managerDisabled = await Assert.ThrowsAsync<MedicalRecordRuleException>(() => service.SaveDraftAsync(
            manager.Id, ViverAppRoles.Manager, patient.Id, appointment.Id,
            new MedicalRecordDraftWriteRequest(0, firstContent), CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.Forbidden, managerDisabled.StatusCode);
        await context.ApplicationSettings.Where(x => x.SettingKey == "manager.medical_records_write_enabled")
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.ValueJson, "true"));
        var managerContent = firstContent with { AdditionalNotes = "Registro complementar realizado pela gestão autorizada." };
        var managerDraft = await service.SaveDraftAsync(manager.Id, ViverAppRoles.Manager, patient.Id, appointment.Id,
            new MedicalRecordDraftWriteRequest(0, managerContent), CancellationToken.None);
        var managerVersion = await service.FinalizeAsync(manager.Id, ViverAppRoles.Manager, patient.Id, appointment.Id,
            new MedicalRecordFinalizeRequest(managerDraft.RowVersion), CancellationToken.None);
        Assert.Equal(2U, managerVersion.VersionNumber);
        Assert.Equal(manager.Id, managerVersion.AuthorProfessionalAccountId);
        Assert.Equal("Gestor da clínica", managerVersion.LicenseLabel);
        var entriesAfterManagerVersion = await service.EntriesAsync(manager.Id, ViverAppRoles.Manager, true, patient.Id,
            null, true, CancellationToken.None);
        Assert.Equal(ViverAppRoles.Manager, entriesAfterManagerVersion[0].AuthorRoleCode);
        var secondContent = firstContent with { ClinicalEvolution = "Paciente sem sinais de alarme e com evolução estável." };
        var rectified = await service.RectifyAsync(doctor.Id, ViverAppRoles.Doctor, patient.Id, managerRead[0].Id,
            new MedicalRecordRectifyRequest(managerVersion.Id, "Correção de informação relatada pelo paciente.", secondContent),
            CancellationToken.None);
        Assert.Equal(3U, rectified.VersionNumber);
        Assert.Equal(managerVersion.Id, rectified.SupersedesVersionId);
        Assert.Equal(3, await context.MedicalRecordVersions.CountAsync(x => x.MedicalRecordEntryId == rectified.EntryId));
        Assert.Equal(4, await context.ClinicalAccessEvents.CountAsync(x => x.PatientAccountId == patient.Id && x.OutcomeCode == "denied"));

        var mutation = await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE medical_record_versions SET chief_complaint = 'alteracao proibida' WHERE id = {finalized.Id}"));
        Assert.True(mutation.ToString().Contains("append-only", StringComparison.OrdinalIgnoreCase), mutation.ToString());
        await transaction.RollbackAsync();
    }

    private static MedicalRecordContent OptionalContent() => new(
        null, null, null, null, null, null, null, null, null, null, null, null, null,
        12, 8, 0, 0, 0, 1.70m);

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

    private static ProfessionalProfile Doctor(ulong id, string license) => new()
    {
        AccountId = id,
        ProfessionalTitle = "Dr.",
        LicenseStateCode = "MG",
        LicenseTypeCode = "CRM",
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
