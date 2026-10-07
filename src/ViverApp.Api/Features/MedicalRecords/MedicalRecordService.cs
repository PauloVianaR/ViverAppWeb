using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientExperience;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.MedicalRecords;

public sealed class MedicalRecordService(
    ViverAppDbContext database,
    PrivateDocumentStore documentStore,
    IClinicalOperationsAuditWriter audit,
    TimeProvider timeProvider)
{
    private static readonly string[] ClinicalScopes = ["clinical", "document", "pdf"];

    public async Task<MedicalRecordPatientSummary> SummaryAsync(
        ulong actorId, string roleCode, bool recentAuthentication, ulong patientId, CancellationToken ct)
    {
        await AuthorizeAsync(actorId, roleCode, recentAuthentication, patientId, "summary", null, ct);
        var patient = await database.Accounts.AsNoTracking()
            .Include(x => x.PatientProfile)
            .Include(x => x.AccountAddress)
            .SingleOrDefaultAsync(x => x.Id == patientId && x.RoleCode == ViverAppRoles.Patient, ct)
            ?? throw Missing();
        var appointments = await database.Appointments.AsNoTracking()
            .Where(x => x.PatientAccountId == patientId)
            .Select(x => new { x.StatusCode, x.StartsAtUtc })
            .ToArrayAsync(ct);
        var now = UtcNow;
        var premium = await database.PremiumMemberships.AsNoTracking().AnyAsync(x => x.AccountId == patientId
            && x.StatusCode == "active" && (x.EndsAtUtc == null || x.EndsAtUtc > now), ct);
        var documents = await database.MedicalRecordDocuments.AsNoTracking()
            .CountAsync(x => x.HealthRecord.PatientAccountId == patientId && x.StatusCode == "available", ct);
        var lastClinical = await database.MedicalRecordVersions.AsNoTracking()
            .Where(x => x.MedicalRecordEntry.HealthRecord.PatientAccountId == patientId)
            .MaxAsync(x => (DateTime?)x.FinalizedAtUtc, ct);
        var birth = patient.PatientProfile?.BirthDate is { } birthValue ? DateOnly.FromDateTime(birthValue) : (DateOnly?)null;
        return new(
            patient.Id,
            patient.FullName,
            patient.PatientProfile?.PreferredName,
            birth,
            birth is null ? null : CalculateAge(birth.Value, DateOnly.FromDateTime(now)),
            IsClinicalProfessional(roleCode) ? null : patient.TaxId,
            IsClinicalProfessional(roleCode) ? null : patient.Email,
            IsClinicalProfessional(roleCode) ? null : patient.PhoneE164,
            IsClinicalProfessional(roleCode) ? null : FormatAddress(patient.AccountAddress),
            patient.StatusCode,
            patient.PortalAccessEnabled,
            premium,
            appointments.Length,
            appointments.Count(x => x.StatusCode == "completed"),
            appointments.Count(x => x.StatusCode == "no_show"),
            appointments.Count(x => x.StatusCode == "canceled"),
            documents,
            appointments.Where(x => x.StartsAtUtc < now).Max(x => (DateTime?)x.StartsAtUtc),
            appointments.Where(x => x.StartsAtUtc >= now && x.StatusCode is "pending" or "confirmed" or "arrived")
                .Min(x => (DateTime?)x.StartsAtUtc),
            lastClinical);
    }

    public async Task<MedicalRecordPage<MedicalRecordTimelineEvent>> TimelineAsync(
        ulong actorId, string roleCode, bool recentAuthentication, ulong patientId,
        DateOnly from, DateOnly to, string? type, string order, int page, int pageSize, CancellationToken ct)
    {
        ValidatePeriod(from, to);
        ValidatePage(page, pageSize);
        await AuthorizeAsync(actorId, roleCode, recentAuthentication, patientId, "timeline", null, ct);
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var operational = await database.AppointmentStatusHistories.AsNoTracking()
            .Where(x => x.Appointment.PatientAccountId == patientId && x.OccurredAtUtc >= start && x.OccurredAtUtc < end)
            .Select(x => new MedicalRecordTimelineEvent(
                "appointment", x.ToStatusCode, AppointmentTitle(x.ToStatusCode), x.Reason,
                x.OccurredAtUtc, x.AppointmentId, x.Appointment.AppointmentNumber,
                x.ActorAccount == null ? null : x.ActorAccount.FullName,
                x.ActorAccount == null ? null : x.ActorAccount.RoleCode, false))
            .ToArrayAsync(ct);
        var financial = await database.CashMovements.AsNoTracking()
            .Where(x => x.Appointment != null && x.Appointment.PatientAccountId == patientId
                && x.OccurredAtUtc >= start && x.OccurredAtUtc < end)
            .Select(x => new MedicalRecordTimelineEvent(
                "financial", x.TypeCode, FinancialTitle(x.TypeCode), null,
                x.OccurredAtUtc, x.AppointmentId, x.Appointment!.AppointmentNumber,
                x.ResponsibleAccount == null ? null : x.ResponsibleAccount.FullName,
                x.ResponsibleAccount == null ? null : x.ResponsibleAccount.RoleCode, false))
            .ToArrayAsync(ct);
        var clinical = await database.MedicalRecordVersions.AsNoTracking()
            .Where(x => x.MedicalRecordEntry.HealthRecord.PatientAccountId == patientId
                && x.FinalizedAtUtc >= start && x.FinalizedAtUtc < end)
            .Select(x => new MedicalRecordTimelineEvent(
                "clinical", x.VersionNumber == 1 ? "finalized" : "rectified",
                IsClinicalProfessional(roleCode) ? (x.VersionNumber == 1 ? "Registro clínico finalizado" : "Registro clínico retificado") : "Registro restrito",
                null, x.FinalizedAtUtc, x.MedicalRecordEntry.AppointmentId,
                x.MedicalRecordEntry.Appointment.AppointmentNumber,
                IsClinicalProfessional(roleCode) ? x.AuthorAccount.FullName : null,
                IsClinicalProfessional(roleCode) ? x.AuthorAccount.RoleCode : null,
                !IsClinicalProfessional(roleCode)))
            .ToArrayAsync(ct);
        var documents = await database.MedicalRecordDocuments.AsNoTracking()
            .Where(x => x.HealthRecord.PatientAccountId == patientId && x.StatusCode == "available"
                && x.CreatedAtUtc >= start && x.CreatedAtUtc < end)
            .Select(x => new MedicalRecordTimelineEvent(
                "document", x.CategoryCode, IsClinicalProfessional(roleCode) ? "Documento clínico adicionado" : "Registro restrito",
                null, x.CreatedAtUtc, x.AppointmentId,
                x.Appointment == null ? null : x.Appointment.AppointmentNumber,
                IsClinicalProfessional(roleCode) ? x.UploadedByAccount.FullName : null,
                IsClinicalProfessional(roleCode) ? x.UploadedByAccount.RoleCode : null,
                !IsClinicalProfessional(roleCode)))
            .ToArrayAsync(ct);
        IEnumerable<MedicalRecordTimelineEvent> merged = operational.Concat(financial).Concat(clinical).Concat(documents);
        if (!string.IsNullOrWhiteSpace(type)) merged = merged.Where(x => x.EventType.Equals(type, StringComparison.OrdinalIgnoreCase));
        merged = order.Equals("asc", StringComparison.OrdinalIgnoreCase)
            ? merged.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.EventType)
            : merged.OrderByDescending(x => x.OccurredAtUtc).ThenBy(x => x.EventType);
        var all = merged.ToArray();
        return new(all.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), page, pageSize, all.Length);
    }

    public async Task<MedicalRecordFinancialSummary> FinancialAsync(
        ulong actorId, string roleCode, bool recentAuthentication, ulong patientId,
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        ValidatePeriod(from, to);
        await AuthorizeAsync(actorId, roleCode, recentAuthentication, patientId, "financial", null, ct);
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var items = await database.Payments.AsNoTracking()
            .Where(x => x.AppointmentNavigation.PatientAccountId == patientId
                && x.AppointmentNavigation.StartsAtUtc >= start && x.AppointmentNavigation.StartsAtUtc < end)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new MedicalRecordFinancialItem(
                x.AppointmentId, x.AppointmentNavigation.AppointmentNumber, x.AppointmentNavigation.StartsAtUtc,
                x.Amount, x.StatusCode,
                IsClinicalProfessional(roleCode) ? "Informação financeira restrita" : PaymentMethod(x.MethodCode, x.ProviderCode),
                x.Id, x.SupersedesPaymentId))
            .ToArrayAsync(ct);
        var received = items.Where(x => x.StatusCode == "paid").Sum(x => x.Amount);
        var reversed = items.Where(x => x.StatusCode is "reversed" or "refunded").Sum(x => x.Amount);
        return new(received, reversed, received - reversed, items);
    }

    public async Task<IReadOnlyList<MedicalRecordAppointmentOption>> AppointmentsAsync(
        ulong actorId, string roleCode, bool recentAuthentication, ulong patientId, CancellationToken ct)
    {
        await AuthorizeAsync(actorId, roleCode, recentAuthentication, patientId, "summary", null, ct);
        var query = database.Appointments.AsNoTracking().Where(x => x.PatientAccountId == patientId
            && (x.StatusCode == "confirmed" || x.StatusCode == "arrived" || x.StatusCode == "in_progress" || x.StatusCode == "completed"));
        if (IsClinicalProfessional(roleCode))
            query = query.Where(x => x.ProfessionalAccountId == actorId);
        return await query.OrderByDescending(x => x.StartsAtUtc).Take(100)
            .Select(x => new MedicalRecordAppointmentOption(x.Id, x.AppointmentNumber, x.StartsAtUtc,
                x.StatusCode, x.AppointmentType.Name)).ToArrayAsync(ct);
    }

    public async Task<MedicalRecordPage<MedicalRecordAppointmentReport>> AppointmentReportsAsync(
        ulong actorId, string roleCode, bool recentAuthentication, ulong patientId,
        int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        await AuthorizeAsync(actorId, roleCode, recentAuthentication, patientId, "clinical", null, ct);
        var query = database.Appointments.AsNoTracking().Where(x => x.PatientAccountId == patientId
            && x.StatusCode != "pending" && x.StatusCode != "canceled");
        if (IsClinicalProfessional(roleCode))
            query = query.Where(x => x.ProfessionalAccountId == actorId);
        var total = await query.CountAsync(ct);
        var appointments = await query
            .Include(x => x.ProfessionalAccount).ThenInclude(x => x.Account)
            .Include(x => x.AppointmentType)
            .Include(x => x.MedicalReport)
            .OrderByDescending(x => x.StartsAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var professionalIds = appointments.Select(x => x.ProfessionalAccountId).Distinct().ToArray();
        var ophthalmologists = (await database.ProfessionalSpecialties.AsNoTracking()
            .Where(x => professionalIds.Contains(x.ProfessionalAccountId)
                && x.Specialty.NormalizedName == "OFTALMOLOGIA")
            .Select(x => x.ProfessionalAccountId).Distinct().ToArrayAsync(ct)).ToHashSet();
        return new(appointments.Select(x => MapAppointmentReport(x,
            ophthalmologists.Contains(x.ProfessionalAccountId))).ToArray(), page, pageSize, total);
    }

    public async Task<MedicalRecordAppointmentPdfSnapshot> AppointmentReportPdfAsync(
        ulong actorId, string roleCode, bool recentAuthentication, ulong patientId,
        ulong appointmentId, CancellationToken ct)
    {
        await AuthorizeAsync(actorId, roleCode, recentAuthentication, patientId, "pdf", null, ct);
        var query = database.Appointments.AsNoTracking()
            .Include(x => x.ProfessionalAccount).ThenInclude(x => x.Account)
            .Include(x => x.AppointmentType).Include(x => x.MedicalReport)
            .Where(x => x.Id == appointmentId && x.PatientAccountId == patientId
                && x.StatusCode != "pending" && x.StatusCode != "canceled");
        if (IsClinicalProfessional(roleCode))
            query = query.Where(x => x.ProfessionalAccountId == actorId);
        var appointment = await query.SingleOrDefaultAsync(ct)
            ?? throw Missing("Atendimento não encontrado neste prontuário.");
        var ophthalmology = await OphthalmologyReportMapping.IsOphthalmologyAsync(
            database, appointment.ProfessionalAccountId, ct);
        var patient = await SummaryWithoutAuditAsync(patientId, roleCode, ct);
        await audit.WriteAsync("medical_record.appointment_pdf_generated", actorId,
            "appointment", appointmentId.ToString(CultureInfo.InvariantCulture), null, ct);
        return new(patient, MapAppointmentReport(appointment, ophthalmology), UtcNow);
    }

    private static MedicalRecordAppointmentReport MapAppointmentReport(Appointment appointment, bool ophthalmology)
    {
        var report = appointment.MedicalReport;
        var fields = ophthalmology && report is not null
            ? OphthalmologyReportMapping.From(report)
                ?? new OphthalmologyReportFields(report.ClinicalSummary, null, null, null, null, null)
            : null;
        return new(appointment.Id, appointment.AppointmentNumber, appointment.StartsAtUtc,
            appointment.StatusCode, appointment.AppointmentType.Name,
            appointment.ProfessionalAccount.Account.FullName, ophthalmology,
            ophthalmology ? null : report?.ClinicalSummary, report?.Recommendations, fields);
    }

    public async Task<IReadOnlyList<MedicalRecordEntryResponse>> EntriesAsync(
        ulong actorId, string roleCode, bool recentAuthentication, ulong patientId,
        string? purpose, bool includeVersions, CancellationToken ct)
    {
        await AuthorizeAsync(actorId, roleCode, recentAuthentication, patientId, "clinical", purpose, ct);
        var query = database.MedicalRecordEntries.AsNoTracking()
            .Include(x => x.Appointment)
            .Include(x => x.AuthorAccount).ThenInclude(x => x.ProfessionalProfile)
            .Include(x => x.CurrentVersion)!.ThenInclude(x => x!.AuthorAccount).ThenInclude(x => x.ProfessionalProfile)
            .Where(x => x.HealthRecord.PatientAccountId == patientId);
        if (IsClinicalProfessional(roleCode)) query = query.Where(x => x.Appointment.ProfessionalAccountId == actorId);
        var entries = await query.OrderByDescending(x => x.Appointment.StartsAtUtc).ToArrayAsync(ct);
        var result = new List<MedicalRecordEntryResponse>(entries.Length);
        foreach (var entry in entries)
        {
            var current = entry.CurrentVersion ?? throw new InvalidOperationException("medical_record_current_version_missing");
            IReadOnlyList<MedicalRecordVersionResponse>? versions = null;
            if (includeVersions)
            {
                var loaded = await database.MedicalRecordVersions.AsNoTracking()
                    .Include(x => x.AuthorAccount).ThenInclude(x => x.ProfessionalProfile)
                    .Include(x => x.MedicalRecordEntry).ThenInclude(x => x.Appointment)
                    .Where(x => x.MedicalRecordEntryId == entry.Id)
                    .OrderByDescending(x => x.VersionNumber).ToArrayAsync(ct);
                versions = loaded.Select(x => MapVersion(x, x.Id == entry.CurrentVersionId)).ToArray();
            }
            result.Add(new(entry.Id, entry.AppointmentId, entry.Appointment.AppointmentNumber,
                entry.Appointment.StartsAtUtc, current.Id, current.VersionNumber,
                current.AuthorAccount.RoleCode,
                current.AuthorAccount.FullName, License(current.AuthorAccount),
                current.FinalizedAtUtc, versions));
        }
        return result;
    }

    public async Task<MedicalRecordDraftResponse?> DraftAsync(
        ulong actorId, string roleCode, ulong patientId, ulong appointmentId, CancellationToken ct)
    {
        await AuthorizeWriteAsync(actorId, roleCode, patientId, ct);
        var draft = await database.MedicalRecordDrafts.AsNoTracking()
            .Include(x => x.Appointment)
            .Include(x => x.AuthorAccount).ThenInclude(x => x.ProfessionalProfile)
            .SingleOrDefaultAsync(x => x.AppointmentId == appointmentId && x.AuthorAccountId == actorId
                && x.HealthRecord.PatientAccountId == patientId, ct);
        return draft is null ? null : MapDraft(draft);
    }

    public async Task<MedicalRecordDraftResponse> SaveDraftAsync(
        ulong actorId, string roleCode, ulong patientId, ulong appointmentId, MedicalRecordDraftWriteRequest request, CancellationToken ct)
    {
        var content = Normalize(request.Content);
        await AuthorizeWriteAsync(actorId, roleCode, patientId, ct);
        var appointment = await database.Appointments
            .SingleOrDefaultAsync(x => x.Id == appointmentId && x.PatientAccountId == patientId
                && (!IsClinicalProfessional(roleCode) || x.ProfessionalAccountId == actorId), ct)
            ?? throw Missing();
        if (appointment.StatusCode is not ("confirmed" or "arrived" or "in_progress" or "completed"))
            throw Conflict("O registro clínico só pode ser escrito em um atendimento confirmado, com chegada, iniciado ou finalizado.");
        var record = await GetOrCreateRecordAsync(patientId, ct);
        var now = UtcNow;
        var draft = await database.MedicalRecordDrafts.SingleOrDefaultAsync(x => x.AppointmentId == appointmentId && x.AuthorAccountId == actorId, ct);
        if (draft is null)
        {
            if (request.RowVersion != 0) throw Conflict("O rascunho foi alterado em outra sessão. Recarregue antes de continuar.");
            draft = new MedicalRecordDraft
            {
                HealthRecordId = record.Id,
                AppointmentId = appointmentId,
                AuthorAccountId = actorId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(30),
                RowVersion = 1,
                Appointment = appointment,
                AuthorAccount = await database.Accounts.SingleAsync(x => x.Id == actorId, ct),
            };
            Assign(draft, content);
            database.MedicalRecordDrafts.Add(draft);
        }
        else
        {
            if (draft.RowVersion != request.RowVersion) throw Conflict("Existe uma versão mais nova deste rascunho. Recarregue para não perder informações.");
            database.Entry(draft).Property(x => x.RowVersion).OriginalValue = request.RowVersion;
            Assign(draft, content);
            draft.UpdatedAtUtc = now;
            draft.ExpiresAtUtc = now.AddDays(30);
            draft.RowVersion++;
            draft.Appointment = appointment;
            draft.AuthorAccount = await database.Accounts.SingleAsync(x => x.Id == actorId, ct);
        }
        await SaveAsync(ct);
        await audit.WriteAsync("medical_record.draft_saved", actorId, "medical_record_draft", draft.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["appointmentId"] = appointmentId.ToString(CultureInfo.InvariantCulture) }, ct);
        return MapDraft(draft);
    }

    public async Task<MedicalRecordVersionResponse> FinalizeAsync(
        ulong actorId, string roleCode, ulong patientId, ulong appointmentId, MedicalRecordFinalizeRequest request, CancellationToken ct)
    {
        await AuthorizeWriteAsync(actorId, roleCode, patientId, ct);
        var ownsTransaction = database.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        var draft = await database.MedicalRecordDrafts
            .Include(x => x.Appointment)
            .Include(x => x.AuthorAccount).ThenInclude(x => x.ProfessionalProfile)
            .SingleOrDefaultAsync(x => x.AppointmentId == appointmentId && x.AuthorAccountId == actorId
                && x.HealthRecord.PatientAccountId == patientId, ct) ?? throw Missing("Rascunho clínico não encontrado.");
        if (draft.RowVersion != request.DraftRowVersion) throw Conflict("Existe uma versão mais nova do rascunho. Revise antes de finalizar.");
        if (draft.Appointment.StatusCode is not ("confirmed" or "arrived" or "in_progress" or "completed"))
            throw Conflict("O registro clínico só pode ser salvo em um atendimento confirmado, com chegada, iniciado ou finalizado.");
        var content = Normalize(ToContent(draft));
        var existing = await database.MedicalRecordEntries.Include(x => x.CurrentVersion)
            .SingleOrDefaultAsync(x => x.AppointmentId == appointmentId, ct);
        var hash = Hash(content);
        if (existing?.CurrentVersion is { } current && CryptographicOperations.FixedTimeEquals(current.ContentSha256, hash))
        {
            if (ownsTransaction) await transaction!.RollbackAsync(ct);
            await database.Entry(current).Reference(x => x.AuthorAccount).LoadAsync(ct);
            await database.Entry(current.AuthorAccount).Reference(x => x.ProfessionalProfile).LoadAsync(ct);
            await database.Entry(current).Reference(x => x.MedicalRecordEntry).LoadAsync(ct);
            await database.Entry(current.MedicalRecordEntry).Reference(x => x.Appointment).LoadAsync(ct);
            return MapVersion(current, true);
        }
        if (existing is not null)
        {
            var next = NewVersion(existing.Id, actorId, existing.CurrentVersion!.VersionNumber + 1,
                existing.CurrentVersionId, "Conteúdo clínico atualizado", content, hash, UtcNow);
            database.MedicalRecordVersions.Add(next);
            await SaveAsync(ct);
            existing.CurrentVersionId = next.Id;
            database.MedicalRecordDrafts.Remove(draft);
            await SaveAsync(ct);
            await audit.WriteAsync("medical_record.version_saved", actorId, "medical_record_version", next.Id.ToString(CultureInfo.InvariantCulture),
                new Dictionary<string, string> { ["appointmentId"] = appointmentId.ToString(CultureInfo.InvariantCulture), ["version"] = next.VersionNumber.ToString(CultureInfo.InvariantCulture) }, ct);
            if (ownsTransaction) await transaction!.CommitAsync(ct);
            next.AuthorAccount = draft.AuthorAccount;
            next.MedicalRecordEntry = existing;
            existing.Appointment = draft.Appointment;
            return MapVersion(next, true);
        }
        var now = UtcNow;
        var entry = new MedicalRecordEntry
        {
            HealthRecordId = draft.HealthRecordId,
            AppointmentId = appointmentId,
            AuthorAccountId = actorId,
            CreatedAtUtc = now,
        };
        database.MedicalRecordEntries.Add(entry);
        await SaveAsync(ct);
        var version = NewVersion(entry.Id, actorId, 1, null, null, content, hash, now);
        database.MedicalRecordVersions.Add(version);
        await SaveAsync(ct);
        entry.CurrentVersionId = version.Id;
        database.MedicalRecordDrafts.Remove(draft);
        await SaveAsync(ct);
        await audit.WriteAsync("medical_record.finalized", actorId, "medical_record_version", version.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["appointmentId"] = appointmentId.ToString(CultureInfo.InvariantCulture), ["version"] = "1" }, ct);
        if (ownsTransaction) await transaction!.CommitAsync(ct);
        version.AuthorAccount = draft.AuthorAccount;
        version.MedicalRecordEntry = entry;
        entry.Appointment = draft.Appointment;
        return MapVersion(version, true);
    }

    public async Task<MedicalRecordVersionResponse> RectifyAsync(
        ulong actorId, string roleCode, ulong patientId, ulong entryId, MedicalRecordRectifyRequest request, CancellationToken ct)
    {
        var content = Normalize(request.Content);
        var reason = request.CorrectionReason.Trim();
        if (reason.Length is < 5 or > 1000) throw Invalid("Informe um motivo de retificação entre 5 e 1.000 caracteres.");
        await AuthorizeWriteAsync(actorId, roleCode, patientId, ct);
        var ownsTransaction = database.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        var entry = await database.MedicalRecordEntries
            .Include(x => x.Appointment)
            .Include(x => x.AuthorAccount).ThenInclude(x => x.ProfessionalProfile)
            .Include(x => x.CurrentVersion)
            .SingleOrDefaultAsync(x => x.Id == entryId && x.HealthRecord.PatientAccountId == patientId
                && (!IsClinicalProfessional(roleCode) || x.Appointment.ProfessionalAccountId == actorId), ct)
            ?? throw Missing();
        if (entry.CurrentVersionId != request.CurrentVersionId || entry.CurrentVersion is null)
            throw Conflict("O registro foi retificado em outra sessão. Recarregue antes de continuar.");
        var now = UtcNow;
        var version = NewVersion(entry.Id, actorId, entry.CurrentVersion.VersionNumber + 1,
            entry.CurrentVersionId, reason, content, Hash(content), now);
        database.MedicalRecordVersions.Add(version);
        await SaveAsync(ct);
        entry.CurrentVersionId = version.Id;
        await SaveAsync(ct);
        await audit.WriteAsync("medical_record.rectified", actorId, "medical_record_version", version.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["entryId"] = entryId.ToString(CultureInfo.InvariantCulture), ["version"] = version.VersionNumber.ToString(CultureInfo.InvariantCulture) }, ct);
        if (ownsTransaction) await transaction!.CommitAsync(ct);
        version.AuthorAccount = entry.AuthorAccount;
        version.MedicalRecordEntry = entry;
        return MapVersion(version, true);
    }

    public async Task<IReadOnlyList<MedicalRecordDocumentResponse>> DocumentsAsync(
        ulong actorId, string roleCode, bool recentAuthentication, ulong patientId, string? purpose, CancellationToken ct)
    {
        await AuthorizeAsync(actorId, roleCode, recentAuthentication, patientId, "document", purpose, ct);
        var query = database.MedicalRecordDocuments.AsNoTracking()
            .Include(x => x.PrivateDocument).Include(x => x.UploadedByAccount).Include(x => x.Appointment)
            .Where(x => x.HealthRecord.PatientAccountId == patientId && x.StatusCode == "available");
        if (IsClinicalProfessional(roleCode)) query = query.Where(x => x.HealthRecord.MedicalRecordEntries.Any(e => e.AuthorAccountId == actorId)
            || x.Appointment != null && x.Appointment.ProfessionalAccountId == actorId);
        return await query.OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new MedicalRecordDocumentResponse(x.Id, x.PrivateDocument.OriginalFileName,
                x.PrivateDocument.ContentType, x.PrivateDocument.SizeBytes, x.CategoryCode, x.AppointmentId,
                x.Appointment == null ? null : x.Appointment.AppointmentNumber, x.MedicalRecordVersionId,
                x.UploadedByAccount.FullName, x.CreatedAtUtc, x.RowVersion)).ToArrayAsync(ct);
    }

    public async Task<MedicalRecordDocumentResponse> UploadAsync(
        ulong actorId, string roleCode, ulong patientId, ulong appointmentId, string categoryCode, IFormFile file, CancellationToken ct)
    {
        await AuthorizeWriteAsync(actorId, roleCode, patientId, ct);
        if (categoryCode is not ("attachment" or "report" or "exam")) throw Invalid("Selecione uma categoria de documento válida.");
        var appointment = await database.Appointments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == appointmentId && x.PatientAccountId == patientId
                && (roleCode == ViverAppRoles.Manager || x.ProfessionalAccountId == actorId), ct)
            ?? throw Missing();
        var record = await GetOrCreateRecordAsync(patientId, ct);
        var document = await documentStore.PrepareClinicalAsync(patientId, file, ct);
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        database.PrivateDocuments.Add(document);
        var link = new MedicalRecordDocument
        {
            HealthRecordId = record.Id,
            AppointmentId = appointmentId,
            PrivateDocument = document,
            UploadedByAccountId = actorId,
            CategoryCode = categoryCode,
            StatusCode = "available",
            CreatedAtUtc = UtcNow,
            RowVersion = 1,
        };
        database.MedicalRecordDocuments.Add(link);
        await SaveAsync(ct);
        await audit.WriteAsync("medical_record.document_uploaded", actorId, "medical_record_document", link.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["appointmentId"] = appointmentId.ToString(CultureInfo.InvariantCulture), ["sizeBytes"] = document.SizeBytes.ToString(CultureInfo.InvariantCulture) }, ct);
        await transaction.CommitAsync(ct);
        return new(link.Id, document.OriginalFileName, document.ContentType, document.SizeBytes,
            categoryCode, appointmentId, appointment.AppointmentNumber, null, string.Empty, link.CreatedAtUtc, link.RowVersion);
    }

    public async Task<(byte[] Content, string Mime, string Name)> DownloadAsync(
        ulong actorId, string roleCode, bool recentAuthentication, ulong patientId, ulong documentId, string? purpose, CancellationToken ct)
    {
        await AuthorizeAsync(actorId, roleCode, recentAuthentication, patientId, "document", purpose, ct);
        var item = await database.MedicalRecordDocuments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == documentId && x.HealthRecord.PatientAccountId == patientId && x.StatusCode == "available", ct)
            ?? throw Missing();
        if (IsClinicalProfessional(roleCode) && !await database.Appointments.AnyAsync(x => x.PatientAccountId == patientId && x.ProfessionalAccountId == actorId, ct))
            throw Missing();
        var result = await documentStore.DownloadAuthorizedClinicalAsync(item.PrivateDocumentId, ct);
        await audit.WriteAsync("medical_record.document_downloaded", actorId, "medical_record_document", documentId.ToString(CultureInfo.InvariantCulture), null, ct);
        return result;
    }

    public async Task DeleteDocumentAsync(ulong actorId, string roleCode, ulong patientId, ulong documentId, ulong rowVersion, CancellationToken ct)
    {
        await AuthorizeWriteAsync(actorId, roleCode, patientId, ct);
        var item = await database.MedicalRecordDocuments.Include(x => x.PrivateDocument)
            .SingleOrDefaultAsync(x => x.Id == documentId && x.HealthRecord.PatientAccountId == patientId
                && x.UploadedByAccountId == actorId && x.StatusCode == "available", ct) ?? throw Missing();
        if (item.RowVersion != rowVersion) throw Conflict("O documento foi alterado em outra sessão.");
        item.StatusCode = "deleted"; item.DeletedAtUtc = UtcNow; item.DeletedByAccountId = actorId; item.RowVersion++;
        item.PrivateDocument.StatusCode = "deleted"; item.PrivateDocument.RowVersion++;
        await SaveAsync(ct);
        await audit.WriteAsync("medical_record.document_deleted", actorId, "medical_record_document", documentId.ToString(CultureInfo.InvariantCulture), null, ct);
    }

    public async Task<IReadOnlyList<MedicalRecordAccessEventResponse>> AccessHistoryAsync(
        ulong actorId, bool recentAuthentication, ulong patientId, string? purpose, CancellationToken ct)
    {
        await AuthorizeAsync(actorId, ViverAppRoles.Administrator, recentAuthentication, patientId, "audit", purpose, ct);
        return await database.ClinicalAccessEvents.AsNoTracking()
            .Where(x => x.PatientAccountId == patientId)
            .OrderByDescending(x => x.OccurredAtUtc).Take(200)
            .Select(x => new MedicalRecordAccessEventResponse(x.Id, x.ActorAccount.FullName, x.ActorRoleCode,
                x.ScopeCode, x.OutcomeCode, x.Purpose, x.OccurredAtUtc)).ToArrayAsync(ct);
    }

    public async Task<MedicalRecordPdfSnapshot> PdfSnapshotAsync(
        ulong actorId, string roleCode, bool recentAuthentication, ulong patientId, MedicalRecordPdfRequest request, CancellationToken ct)
    {
        var purpose = request.Purpose;
        await AuthorizeAsync(actorId, roleCode, recentAuthentication, patientId, "pdf", purpose, ct);
        var from = request.From ?? DateOnly.FromDateTime(UtcNow.AddYears(-1));
        var to = request.To ?? DateOnly.FromDateTime(UtcNow);
        ValidatePeriod(from, to);
        var summary = await SummaryWithoutAuditAsync(patientId, roleCode, ct);
        var timeline = request.IncludeTimeline
            ? (await TimelineAsync(actorId, roleCode, recentAuthentication, patientId, from, to, null, "asc", 1, 200, ct)).Items
            : [];
        var entries = request.IncludeClinical
            ? await EntriesWithoutAuditAsync(actorId, roleCode, patientId, ct)
            : [];
        var financial = request.IncludeFinancial
            ? await FinancialAsync(actorId, roleCode, recentAuthentication, patientId, from, to, ct)
            : null;
        await audit.WriteAsync("medical_record.pdf_generated", actorId, "electronic_health_record", patientId.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["clinical"] = request.IncludeClinical.ToString(), ["timeline"] = request.IncludeTimeline.ToString(), ["financial"] = request.IncludeFinancial.ToString() }, ct);
        return new(summary, from, to, timeline, entries, financial, UtcNow);
    }

    private async Task<MedicalRecordPatientSummary> SummaryWithoutAuditAsync(ulong patientId, string roleCode, CancellationToken ct)
    {
        var patient = await database.Accounts.AsNoTracking().Include(x => x.PatientProfile).Include(x => x.AccountAddress)
            .SingleAsync(x => x.Id == patientId, ct);
        var appointments = await database.Appointments.AsNoTracking().Where(x => x.PatientAccountId == patientId)
            .Select(x => new { x.StatusCode, x.StartsAtUtc }).ToArrayAsync(ct);
        var now = UtcNow;
        var birth = patient.PatientProfile?.BirthDate is { } value ? DateOnly.FromDateTime(value) : (DateOnly?)null;
        return new(patient.Id, patient.FullName, patient.PatientProfile?.PreferredName, birth,
            birth is null ? null : CalculateAge(birth.Value, DateOnly.FromDateTime(now)),
            IsClinicalProfessional(roleCode) ? null : patient.TaxId,
            IsClinicalProfessional(roleCode) ? null : patient.Email,
            IsClinicalProfessional(roleCode) ? null : patient.PhoneE164,
            IsClinicalProfessional(roleCode) ? null : FormatAddress(patient.AccountAddress),
            patient.StatusCode, patient.PortalAccessEnabled,
            await database.PremiumMemberships.AnyAsync(x => x.AccountId == patientId && x.StatusCode == "active"
                && (x.EndsAtUtc == null || x.EndsAtUtc > now), ct),
            appointments.Length, appointments.Count(x => x.StatusCode == "completed"), appointments.Count(x => x.StatusCode == "no_show"),
            appointments.Count(x => x.StatusCode == "canceled"),
            await database.MedicalRecordDocuments.CountAsync(x => x.HealthRecord.PatientAccountId == patientId && x.StatusCode == "available", ct),
            appointments.Where(x => x.StartsAtUtc < now).Max(x => (DateTime?)x.StartsAtUtc),
            appointments.Where(x => x.StartsAtUtc >= now).Min(x => (DateTime?)x.StartsAtUtc),
            await database.MedicalRecordVersions.Where(x => x.MedicalRecordEntry.HealthRecord.PatientAccountId == patientId).MaxAsync(x => (DateTime?)x.FinalizedAtUtc, ct));
    }

    private async Task<IReadOnlyList<MedicalRecordEntryResponse>> EntriesWithoutAuditAsync(ulong actorId, string roleCode, ulong patientId, CancellationToken ct)
    {
        var entries = await database.MedicalRecordEntries.AsNoTracking().Include(x => x.Appointment)
            .Include(x => x.AuthorAccount).ThenInclude(x => x.ProfessionalProfile)
            .Include(x => x.CurrentVersion)!.ThenInclude(x => x!.AuthorAccount).ThenInclude(x => x.ProfessionalProfile)
            .Where(x => x.HealthRecord.PatientAccountId == patientId && (!IsClinicalProfessional(roleCode) || x.Appointment.ProfessionalAccountId == actorId))
            .OrderByDescending(x => x.Appointment.StartsAtUtc).ToArrayAsync(ct);
        return entries.Select(x => new MedicalRecordEntryResponse(x.Id, x.AppointmentId, x.Appointment.AppointmentNumber,
            x.Appointment.StartsAtUtc, x.CurrentVersionId!.Value, x.CurrentVersion!.VersionNumber,
            x.CurrentVersion.AuthorAccount.RoleCode,
            x.CurrentVersion.AuthorAccount.FullName, License(x.CurrentVersion.AuthorAccount), x.CurrentVersion.FinalizedAtUtc,
            new[] { MapVersionForLoadedEntry(x.CurrentVersion, x) })).ToArray();
    }

    private async Task AuthorizeAsync(ulong actorId, string roleCode, bool recentAuthentication, ulong patientId,
        string scope, string? purpose, CancellationToken ct)
    {
        var patientExists = await database.Accounts.AsNoTracking().AnyAsync(x => x.Id == patientId && x.RoleCode == ViverAppRoles.Patient, ct);
        var actorValid = await database.Accounts.AsNoTracking().AnyAsync(x => x.Id == actorId && x.RoleCode == roleCode && x.StatusCode == "active", ct);
        if (!patientExists || !actorValid) throw Missing();
        var normalizedPurpose = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim();
        var allowed = roleCode switch
        {
            ViverAppRoles.Doctor or ViverAppRoles.Psychologist => await database.ProfessionalPatientLinks.AsNoTracking().AnyAsync(x => x.ProfessionalAccountId == actorId
                    && x.PatientAccountId == patientId && x.StatusCode == "active", ct)
                || await database.Appointments.AsNoTracking().AnyAsync(x => x.ProfessionalAccountId == actorId && x.PatientAccountId == patientId, ct),
            ViverAppRoles.Manager => true,
            ViverAppRoles.Administrator => !ClinicalScopes.Contains(scope, StringComparer.Ordinal) && scope != "audit" || recentAuthentication,
            _ => false,
        };
        var storedPurpose = roleCode switch
        {
            ViverAppRoles.Doctor or ViverAppRoles.Psychologist => null,
            ViverAppRoles.Manager => normalizedPurpose ?? "Acesso gerencial autorizado ao prontuário",
            ViverAppRoles.Administrator => "Acesso administrativo ao prontuário",
            _ => normalizedPurpose ?? "Acesso operacional autorizado ao prontuário",
        };
        database.ClinicalAccessEvents.Add(new ClinicalAccessEvent
        {
            PatientAccountId = patientId,
            ActorAccountId = actorId,
            ActorRoleCode = roleCode,
            ScopeCode = scope,
            OutcomeCode = allowed ? "allowed" : "denied",
            Purpose = storedPurpose,
            OccurredAtUtc = UtcNow,
        });
        await database.SaveChangesAsync(ct);
        if (!allowed)
        {
            if (roleCode == ViverAppRoles.Administrator && !recentAuthentication)
                throw Forbidden("Confirme novamente sua identidade antes de acessar conteúdo clínico.");
            throw Missing();
        }
    }

    private async Task AuthorizeWriteAsync(ulong actorId, string roleCode, ulong patientId, CancellationToken ct)
    {
        if (roleCode is not (ViverAppRoles.Doctor or ViverAppRoles.Psychologist or ViverAppRoles.Manager)) throw Missing();
        if (roleCode == ViverAppRoles.Manager)
        {
            var enabled = await database.ApplicationSettings.AsNoTracking()
                .Where(x => x.SettingKey == "manager.medical_records_write_enabled")
                .Select(x => x.ValueJson)
                .SingleOrDefaultAsync(ct);
            if (!string.Equals(enabled?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                throw Forbidden("A escrita do prontuário pelo gestor está desabilitada nas configurações administrativas.");
        }
        await AuthorizeAsync(actorId, roleCode, true, patientId, "clinical", null, ct);
    }

    private async Task<ElectronicHealthRecord> GetOrCreateRecordAsync(ulong patientId, CancellationToken ct)
    {
        var record = await database.ElectronicHealthRecords.SingleOrDefaultAsync(x => x.PatientAccountId == patientId, ct);
        if (record is not null) return record;
        var now = UtcNow;
        record = new ElectronicHealthRecord { PatientAccountId = patientId, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 };
        database.ElectronicHealthRecords.Add(record);
        try { await database.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            database.Entry(record).State = EntityState.Detached;
            record = await database.ElectronicHealthRecords.SingleAsync(x => x.PatientAccountId == patientId, ct);
        }
        return record;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await database.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Conflict("Os dados foram alterados em outra sessão. Recarregue antes de continuar."); }
    }

    private static MedicalRecordVersion NewVersion(ulong entryId, ulong actorId, uint number, ulong? supersedes,
        string? reason, MedicalRecordContent content, byte[] hash, DateTime now)
    {
        var version = new MedicalRecordVersion
        {
            MedicalRecordEntryId = entryId,
            AuthorAccountId = actorId,
            VersionNumber = number,
            SupersedesVersionId = supersedes,
            CorrectionReason = reason,
            ContentSha256 = hash,
            FinalizedAtUtc = now,
        };
        Assign(version, content);
        return version;
    }

    private static MedicalRecordDraftResponse MapDraft(MedicalRecordDraft x) => new(x.Id, x.AppointmentId,
        x.Appointment.AppointmentNumber, x.Appointment.StartsAtUtc, x.AuthorAccountId,
        x.AuthorAccount.FullName, ToContent(x), x.UpdatedAtUtc, x.ExpiresAtUtc, x.RowVersion);

    private static MedicalRecordVersionResponse MapVersion(MedicalRecordVersion x, bool current) => new(x.Id,
        x.MedicalRecordEntryId, x.MedicalRecordEntry.AppointmentId, x.MedicalRecordEntry.Appointment.AppointmentNumber,
        x.VersionNumber, x.SupersedesVersionId, x.CorrectionReason, x.AuthorAccountId,
        x.AuthorAccount.FullName, License(x.AuthorAccount), ToContent(x), x.FinalizedAtUtc, current);

    private static MedicalRecordVersionResponse MapVersionForLoadedEntry(MedicalRecordVersion x, MedicalRecordEntry entry) => new(x.Id,
        entry.Id, entry.AppointmentId, entry.Appointment.AppointmentNumber, x.VersionNumber, x.SupersedesVersionId,
        x.CorrectionReason, x.AuthorAccountId, x.AuthorAccount.FullName,
        License(x.AuthorAccount), ToContent(x), x.FinalizedAtUtc, true);

    private static MedicalRecordContent Normalize(MedicalRecordContent? value)
    {
        if (value is null) throw Invalid("Informe o conteúdo do registro clínico.");
        string? Clean(string? text)
        {
            var result = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (result?.Length > 12000) throw Invalid("Cada campo clínico deve ter no máximo 12.000 caracteres.");
            return result;
        }
        static ushort? OptionalUnsigned(ushort? number) => number == 0 ? null : number;
        static decimal? OptionalDecimal(decimal? number) => number == 0 ? null : number;
        var systolic = OptionalUnsigned(value.SystolicPressureMmhg);
        var diastolic = OptionalUnsigned(value.DiastolicPressureMmhg);
        var heartRate = OptionalUnsigned(value.HeartRateBpm);
        var temperature = OptionalDecimal(value.TemperatureCelsius);
        var weight = OptionalDecimal(value.WeightKg);
        var height = OptionalDecimal(value.HeightCm);
        if (systolic is >= 4 and <= 30) systolic = (ushort)(systolic.Value * 10);
        if (diastolic is >= 2 and <= 20) diastolic = (ushort)(diastolic.Value * 10);
        if (height is > 0 and <= 3) height *= 100;

        if (systolic is < 40 or > 300 || diastolic is < 20 or > 200
            || heartRate is < 20 or > 300 || temperature is < 25 or > 45
            || weight is < 0.1m or > 500 || height is < 20 or > 260)
            throw Invalid("Revise os sinais vitais informados.");
        return value with
        {
            ChiefComplaint = Clean(value.ChiefComplaint),
            PresentIllnessHistory = Clean(value.PresentIllnessHistory),
            PersonalHistory = Clean(value.PersonalHistory),
            FamilyHistory = Clean(value.FamilyHistory),
            Allergies = Clean(value.Allergies),
            Medications = Clean(value.Medications),
            RelevantHabits = Clean(value.RelevantHabits),
            PhysicalExamination = Clean(value.PhysicalExamination),
            DiagnosticHypotheses = Clean(value.DiagnosticHypotheses),
            ConductAndGuidance = Clean(value.ConductAndGuidance),
            FollowUpPlan = Clean(value.FollowUpPlan),
            ClinicalEvolution = Clean(value.ClinicalEvolution),
            AdditionalNotes = Clean(value.AdditionalNotes),
            SystolicPressureMmhg = systolic,
            DiastolicPressureMmhg = diastolic,
            HeartRateBpm = heartRate,
            TemperatureCelsius = temperature,
            WeightKg = weight,
            HeightCm = height,
        };
    }

    private static byte[] Hash(MedicalRecordContent content) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(content));

    private static MedicalRecordContent ToContent(MedicalRecordDraft x) => new(x.ChiefComplaint, x.PresentIllnessHistory,
        x.PersonalHistory, x.FamilyHistory, x.Allergies, x.Medications, x.RelevantHabits, x.PhysicalExamination,
        x.DiagnosticHypotheses, x.ConductAndGuidance, x.FollowUpPlan, x.ClinicalEvolution, x.AdditionalNotes,
        x.SystolicPressureMmhg, x.DiastolicPressureMmhg, x.HeartRateBpm, x.TemperatureCelsius, x.WeightKg, x.HeightCm);

    private static MedicalRecordContent ToContent(MedicalRecordVersion x) => new(x.ChiefComplaint, x.PresentIllnessHistory,
        x.PersonalHistory, x.FamilyHistory, x.Allergies, x.Medications, x.RelevantHabits, x.PhysicalExamination,
        x.DiagnosticHypotheses, x.ConductAndGuidance, x.FollowUpPlan, x.ClinicalEvolution, x.AdditionalNotes,
        x.SystolicPressureMmhg, x.DiastolicPressureMmhg, x.HeartRateBpm, x.TemperatureCelsius, x.WeightKg, x.HeightCm);

    private static void Assign(MedicalRecordDraft x, MedicalRecordContent c)
    {
        x.ChiefComplaint = c.ChiefComplaint; x.PresentIllnessHistory = c.PresentIllnessHistory; x.PersonalHistory = c.PersonalHistory;
        x.FamilyHistory = c.FamilyHistory; x.Allergies = c.Allergies; x.Medications = c.Medications;
        x.RelevantHabits = c.RelevantHabits; x.PhysicalExamination = c.PhysicalExamination; x.DiagnosticHypotheses = c.DiagnosticHypotheses;
        x.ConductAndGuidance = c.ConductAndGuidance; x.FollowUpPlan = c.FollowUpPlan; x.ClinicalEvolution = c.ClinicalEvolution;
        x.AdditionalNotes = c.AdditionalNotes; x.SystolicPressureMmhg = c.SystolicPressureMmhg;
        x.DiastolicPressureMmhg = c.DiastolicPressureMmhg; x.HeartRateBpm = c.HeartRateBpm;
        x.TemperatureCelsius = c.TemperatureCelsius; x.WeightKg = c.WeightKg; x.HeightCm = c.HeightCm;
    }

    private static void Assign(MedicalRecordVersion x, MedicalRecordContent c)
    {
        x.ChiefComplaint = c.ChiefComplaint; x.PresentIllnessHistory = c.PresentIllnessHistory; x.PersonalHistory = c.PersonalHistory;
        x.FamilyHistory = c.FamilyHistory; x.Allergies = c.Allergies; x.Medications = c.Medications;
        x.RelevantHabits = c.RelevantHabits; x.PhysicalExamination = c.PhysicalExamination; x.DiagnosticHypotheses = c.DiagnosticHypotheses;
        x.ConductAndGuidance = c.ConductAndGuidance; x.FollowUpPlan = c.FollowUpPlan; x.ClinicalEvolution = c.ClinicalEvolution;
        x.AdditionalNotes = c.AdditionalNotes; x.SystolicPressureMmhg = c.SystolicPressureMmhg;
        x.DiastolicPressureMmhg = c.DiastolicPressureMmhg; x.HeartRateBpm = c.HeartRateBpm;
        x.TemperatureCelsius = c.TemperatureCelsius; x.WeightKg = c.WeightKg; x.HeightCm = c.HeightCm;
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
    private static bool IsClinicalProfessional(string roleCode) => roleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist;
    private static int CalculateAge(DateOnly birth, DateOnly today) { var age = today.Year - birth.Year; return birth > today.AddYears(-age) ? age - 1 : age; }
    private static string License(Account author) => author.ProfessionalProfile is { } doctor
        ? $"{doctor.LicenseTypeCode} {doctor.LicenseStateCode} {doctor.LicenseNumber}"
        : author.RoleCode == ViverAppRoles.Manager ? "Gestor da clínica" : "Administrador";
    private static string? FormatAddress(AccountAddress? x) => x is null ? null : string.Join(" · ", new[]
    {
        string.Join(", ", new[] { x.Street, x.Number }.Where(v => !string.IsNullOrWhiteSpace(v))), x.Complement,
        x.District, string.Join("/", new[] { x.City, x.StateCode }.Where(v => !string.IsNullOrWhiteSpace(v))),
        x.PostalCode is { Length: 8 } cep ? $"{cep[..5]}-{cep[5..]}" : x.PostalCode,
    }.Where(v => !string.IsNullOrWhiteSpace(v)));
    private static string AppointmentTitle(string x) => x switch { "pending" => "Atendimento pendente", "confirmed" => "Atendimento confirmado", "arrived" => "Paciente chegou", "in_progress" => "Atendimento iniciado", "completed" => "Atendimento concluído", "rescheduled" => "Atendimento reagendado", "canceled" => "Atendimento cancelado", "no_show" => "Falta registrada", _ => "Atendimento atualizado" };
    private static string FinancialTitle(string x) => x switch { "payment_received" => "Pagamento recebido", "payment_reversal" => "Pagamento cancelado", "provider_fee" => "Tarifa financeira", _ => "Movimentação financeira" };
    private static string PaymentMethod(string? method, string provider) => method switch { "cash" => "Dinheiro", "pix" => "Pix", "debit_card" => "Cartão de débito", "credit_card" => "Cartão de crédito", _ when provider == "pagbank" => "Pagamento online", _ => "Não informado" };
    private static void ValidatePeriod(DateOnly from, DateOnly to) { if (to < from || to.DayNumber - from.DayNumber > 366) throw Invalid("O período deve ter no máximo 367 dias."); }
    private static void ValidatePage(int page, int size) { if (page < 1 || size is < 1 or > 200) throw Invalid("Paginação inválida."); }
    internal static MedicalRecordRuleException Invalid(string message) => new(400, message);
    internal static MedicalRecordRuleException Forbidden(string message) => new(403, message);
    internal static MedicalRecordRuleException Missing(string message = "Prontuário não encontrado.") => new(404, message);
    internal static MedicalRecordRuleException Conflict(string message) => new(409, message);
}

public sealed record MedicalRecordPdfSnapshot(
    MedicalRecordPatientSummary Patient,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<MedicalRecordTimelineEvent> Timeline,
    IReadOnlyList<MedicalRecordEntryResponse> Entries,
    MedicalRecordFinancialSummary? Financial,
    DateTime GeneratedAtUtc);
