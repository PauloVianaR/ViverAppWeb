using System.Data;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.ClinicAdministration;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.ClinicalOperations;

public sealed class ClinicalOperationsService(
    ViverAppDbContext database,
    IClinicalOperationsAuditWriter auditWriter,
    TimeProvider timeProvider)
{
    private static readonly string[] AllowedStatuses =
        ["pending", "confirmed", "completed", "canceled", "rescheduled", "no_show"];

    public async Task<ClinicalContextResponse> GetContextAsync(
        ulong actorId,
        string roleCode,
        CancellationToken cancellationToken)
    {
        EnsureClinicalRole(roleCode);
        var actor = await database.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == actorId, cancellationToken)
            ?? throw NotFound("Conta autenticada não encontrada.");
        var doctorsQuery = database.ProfessionalProfiles.AsNoTracking()
            .Where(item => item.Account.StatusCode == "active");
        if (IsClinicalProfessional(roleCode))
        {
            doctorsQuery = doctorsQuery.Where(item => item.AccountId == actorId);
        }

        var doctors = await doctorsQuery
            .OrderBy(item => item.Account.FullName)
            .Select(item => new ClinicalDoctorOptionResponse(
                item.AccountId,
                item.Account.FullName,
                $"CRM {item.LicenseStateCode} {item.LicenseNumber}"))
            .ToArrayAsync(cancellationToken);
        return new ClinicalContextResponse(actorId, roleCode, actor.FullName, doctors);
    }

    public async Task<ClinicalPage<ClinicalAppointmentResponse>> GetAppointmentsAsync(
        ulong actorId,
        string roleCode,
        DateOnly from,
        DateOnly to,
        string? status,
        ulong? doctorAccountId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        EnsureClinicalRole(roleCode);
        ValidatePage(page, pageSize);
        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            throw BadRequest("O período da agenda deve ter no máximo 367 dias.");
        }

        if (status is not null && !AllowedStatuses.Contains(status, StringComparer.Ordinal))
        {
            throw BadRequest("Status da agenda inválido.");
        }

        var timezone = await GetClinicTimezoneAsync(cancellationToken);
        var startUtc = StartOfDayUtc(from, timezone);
        var endUtc = StartOfDayUtc(to.AddDays(1), timezone);
        var query = VisibleAppointments(actorId, roleCode)
            .Where(item => item.StartsAtUtc >= startUtc && item.StartsAtUtc < endUtc);
        if (status is not null)
        {
            query = query.Where(item => item.StatusCode == status);
        }

        if (doctorAccountId.HasValue)
        {
            if (IsClinicalProfessional(roleCode) && doctorAccountId.Value != actorId)
            {
                throw Forbidden();
            }

            query = query.Where(item => item.ProfessionalAccountId == doctorAccountId.Value);
        }

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Length > 120)
            {
                throw BadRequest("A busca deve ter no máximo 120 caracteres.");
            }

            query = query.Where(item =>
                item.PatientAccount.FullName.Contains(term)
                || item.ProfessionalAccount.Account.FullName.Contains(term)
                || item.AppointmentType.Name.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var appointments = await query.AsNoTracking()
            .Include(item => item.PatientAccount)
            .Include(item => item.ProfessionalAccount).ThenInclude(item => item.Account)
            .Include(item => item.AppointmentType)
            .Include(item => item.MedicalReport)
            .OrderBy(item => item.StartsAtUtc)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return new ClinicalPage<ClinicalAppointmentResponse>(
            appointments.Select(item => MapAppointment(item, actorId, roleCode, includeReportContent: false)).ToArray(),
            page,
            pageSize,
            total);
    }

    public async Task<ClinicalAppointmentResponse> GetAppointmentAsync(
        ulong actorId,
        string roleCode,
        ulong appointmentId,
        CancellationToken cancellationToken)
    {
        EnsureClinicalRole(roleCode);
        var appointment = await VisibleAppointments(actorId, roleCode).AsNoTracking()
            .Include(item => item.PatientAccount)
            .Include(item => item.ProfessionalAccount).ThenInclude(item => item.Account)
            .Include(item => item.AppointmentType)
            .Include(item => item.MedicalReport)
            .SingleOrDefaultAsync(item => item.Id == appointmentId, cancellationToken)
            ?? throw NotFound("Consulta não encontrada.");
        var includeReportContent = IsClinicalProfessional(roleCode) && appointment.ProfessionalAccountId == actorId;
        if (includeReportContent && appointment.MedicalReport is not null)
        {
            await auditWriter.WriteAsync(
                "medical_report.viewed",
                actorId,
                "medical_report",
                appointment.MedicalReport.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                cancellationToken);
        }

        return MapAppointment(appointment, actorId, roleCode, includeReportContent);
    }

    public async Task<ClinicalPage<ClinicalPatientResponse>> GetPatientsAsync(
        ulong actorId,
        string roleCode,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        EnsureClinicalRole(roleCode);
        ValidatePage(page, pageSize);
        var visibleAppointments = VisibleAppointments(actorId, roleCode);
        var query = database.Accounts.AsNoTracking()
            .Where(item => item.RoleCode == ViverAppRoles.Patient
                && visibleAppointments.Any(appointment => appointment.PatientAccountId == item.Id));
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Length > 120)
            {
                throw BadRequest("A busca deve ter no máximo 120 caracteres.");
            }

            query = query.Where(item => item.FullName.Contains(term)
                || item.Email != null && item.Email.Contains(term)
                || item.PhoneE164 != null && item.PhoneE164.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var patients = await query.Include(item => item.PatientProfile)
            .OrderBy(item => item.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var patientIds = patients.Select(item => item.Id).ToArray();
        var appointments = await visibleAppointments.AsNoTracking()
            .Where(item => patientIds.Contains(item.PatientAccountId))
            .Select(item => new { item.PatientAccountId, item.StartsAtUtc, item.StatusCode })
            .ToArrayAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var items = patients.Select(patient =>
        {
            var history = appointments.Where(item => item.PatientAccountId == patient.Id).ToArray();
            return new ClinicalPatientResponse(
                patient.Id,
                patient.FullName,
                patient.PatientProfile?.PreferredName,
                patient.Email,
                patient.PhoneE164,
                patient.PatientProfile?.BirthDate is { } birth ? DateOnly.FromDateTime(birth) : null,
                history.Length,
                history.Where(item => item.StartsAtUtc < now)
                    .Select(item => (DateTime?)item.StartsAtUtc).Max(),
                history.Where(item => item.StartsAtUtc >= now && item.StatusCode is "pending" or "confirmed")
                    .Select(item => (DateTime?)item.StartsAtUtc).Min());
        }).ToArray();
        return new ClinicalPage<ClinicalPatientResponse>(items, page, pageSize, total);
    }

    public async Task<ClinicalReportResponse> SaveDraftAsync(
        ulong actorId,
        ulong appointmentId,
        MedicalReportWriteRequest request,
        CancellationToken cancellationToken)
    {
        var summary = ValidateSummary(request.ClinicalSummary);
        var recommendations = ValidateRecommendations(request.Recommendations);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var appointment = await database.Appointments
            .Include(item => item.MedicalReport)
            .SingleOrDefaultAsync(item => item.Id == appointmentId && item.ProfessionalAccountId == actorId, cancellationToken)
            ?? throw NotFound("Consulta não encontrada.");
        if (appointment.StatusCode is "pending" or "canceled")
        {
            throw Conflict("O relatório não pode ser alterado no estado atual da consulta.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var report = appointment.MedicalReport;
        if (report is null)
        {
            if (request.RowVersion != 0)
            {
                throw Conflict("O relatório foi alterado por outra sessão. Recarregue e tente novamente.");
            }

            report = new MedicalReport
            {
                AppointmentId = appointmentId,
                AuthorProfessionalAccountId = actorId,
                StatusCode = "published",
                ClinicalSummary = summary,
                Recommendations = recommendations,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                PublishedAtUtc = now,
                RowVersion = 1,
            };
            database.MedicalReports.Add(report);
            await SaveAsync(cancellationToken);
        }
        else
        {
            SetConcurrency(report, request.RowVersion);
            report.ClinicalSummary = summary;
            report.Recommendations = recommendations;
            report.UpdatedAtUtc = now;
            report.StatusCode = "published";
            report.PublishedAtUtc ??= now;
        }

        var nextVersion = await database.MedicalReportVersions
            .Where(x => x.MedicalReportId == report.Id)
            .MaxAsync(x => (uint?)x.VersionNumber, cancellationToken) ?? 0;
        database.MedicalReportVersions.Add(new MedicalReportVersion
        {
            MedicalReportId = report.Id,
            VersionNumber = nextVersion + 1,
            AuthorProfessionalAccountId = actorId,
            ClinicalSummary = summary,
            Recommendations = recommendations,
            ChangeReason = nextVersion == 0 ? null : "Laudo atualizado pelo médico",
            CreatedAtUtc = now,
        });

        await SaveAsync(cancellationToken);
        await auditWriter.WriteAsync(
            "medical_report.version_saved",
            actorId,
            "medical_report",
            report.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["appointmentId"] = appointmentId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapReport(report, includeContent: true);
    }

    public async Task<ClinicalAppointmentResponse> CompleteAsync(
        ulong actorId,
        ulong appointmentId,
        CompleteAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        var hasReport = !string.IsNullOrWhiteSpace(request.ClinicalSummary);
        var summary = hasReport ? ValidateSummary(request.ClinicalSummary!) : null;
        var recommendations = ValidateRecommendations(request.Recommendations);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var appointment = await AppointmentForUpdateAsync(appointmentId, cancellationToken);
        await database.Entry(appointment).Reference(item => item.PatientAccount).LoadAsync(cancellationToken);
        await database.Entry(appointment).Reference(item => item.ProfessionalAccount).LoadAsync(cancellationToken);
        await database.Entry(appointment.ProfessionalAccount).Reference(item => item.Account).LoadAsync(cancellationToken);
        await database.Entry(appointment).Reference(item => item.AppointmentType).LoadAsync(cancellationToken);
        await database.Entry(appointment).Reference(item => item.MedicalReport).LoadAsync(cancellationToken);
        if (appointment.ProfessionalAccountId != actorId)
        {
            throw Forbidden();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (appointment.StatusCode is not ("confirmed" or "arrived" or "in_progress"))
            throw Conflict("Somente um atendimento confirmado, com chegada ou iniciado pode ser concluído.");

        SetAppointmentConcurrency(appointment, request.AppointmentRowVersion);
        var report = appointment.MedicalReport;
        if (hasReport && report is null)
        {
            if (request.ReportRowVersion != 0)
            {
                throw Conflict("O relatório foi alterado por outra sessão. Recarregue e tente novamente.");
            }

            report = new MedicalReport
            {
                AppointmentId = appointmentId,
                AuthorProfessionalAccountId = actorId,
                CreatedAtUtc = now,
                RowVersion = 1,
            };
            database.MedicalReports.Add(report);
        }
        else if (hasReport && report is not null)
        {
            SetConcurrency(report, request.ReportRowVersion);
        }

        if (hasReport)
        {
            report!.StatusCode = "published";
            report.ClinicalSummary = summary!;
            report.Recommendations = recommendations;
            report.UpdatedAtUtc = now;
            report.PublishedAtUtc = now;
            var nextVersion = await database.MedicalReportVersions.Where(x => x.MedicalReportId == report.Id)
                .MaxAsync(x => (uint?)x.VersionNumber, cancellationToken) ?? 0;
            database.MedicalReportVersions.Add(new MedicalReportVersion
            {
                MedicalReport = report,
                VersionNumber = nextVersion + 1,
                AuthorProfessionalAccountId = actorId,
                ClinicalSummary = summary!,
                Recommendations = recommendations,
                ChangeReason = nextVersion == 0 ? null : "Laudo atualizado na finalização do atendimento",
                CreatedAtUtc = now,
            });
        }
        var previousStatus = appointment.StatusCode;
        appointment.StatusCode = "completed";
        appointment.CompletedByAccountId = actorId;
        appointment.CompletedAtUtc = now;
        appointment.UpdatedAtUtc = now;
        database.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatusCode = previousStatus,
            ToStatusCode = "completed",
            ActorAccountId = actorId,
            StartsAtUtc = appointment.StartsAtUtc,
            EndsAtUtc = appointment.EndsAtUtc,
            OccurredAtUtc = now,
        });
        await SaveAsync(cancellationToken);
        var auditData = new Dictionary<string, string>
        {
            ["previousStatus"] = previousStatus,
            ["reportPublished"] = hasReport.ToString(),
        };
        if (report is not null && hasReport)
            auditData["reportId"] = report.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await auditWriter.WriteAsync(
            "appointment.completed",
            actorId,
            "appointment",
            appointment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            auditData,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapAppointment(appointment, actorId, ViverAppRoles.Doctor, includeReportContent: true);
    }

    public async Task<ClinicalAppointmentResponse> RecordNoShowAsync(
        ulong actorId,
        string roleCode,
        ulong appointmentId,
        RecordNoShowRequest request,
        CancellationToken cancellationToken)
    {
        EnsureClinicalRole(roleCode);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var appointment = await AppointmentForUpdateAsync(appointmentId, cancellationToken);
        await database.Entry(appointment).Reference(item => item.PatientAccount).LoadAsync(cancellationToken);
        await database.Entry(appointment).Reference(item => item.ProfessionalAccount).LoadAsync(cancellationToken);
        await database.Entry(appointment.ProfessionalAccount).Reference(item => item.Account).LoadAsync(cancellationToken);
        await database.Entry(appointment).Reference(item => item.AppointmentType).LoadAsync(cancellationToken);
        if (IsClinicalProfessional(roleCode) && appointment.ProfessionalAccountId != actorId)
        {
            throw NotFound("Consulta não encontrada.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (appointment.StatusCode is not ("confirmed" or "arrived"))
        {
            throw Conflict("Somente uma consulta confirmada pode ser marcada como falta.");
        }

        if (appointment.StartsAtUtc > now)
        {
            throw Conflict("A consulta ainda não começou.");
        }

        SetAppointmentConcurrency(appointment, request.AppointmentRowVersion);
        var previousStatus = appointment.StatusCode;
        appointment.StatusCode = "no_show";
        appointment.NoShowRecordedByAccountId = actorId;
        appointment.NoShowRecordedAtUtc = now;
        appointment.UpdatedAtUtc = now;
        database.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatusCode = previousStatus,
            ToStatusCode = "no_show",
            ActorAccountId = actorId,
            StartsAtUtc = appointment.StartsAtUtc,
            EndsAtUtc = appointment.EndsAtUtc,
            OccurredAtUtc = now,
        });
        await SaveAsync(cancellationToken);
        await auditWriter.WriteAsync(
            "appointment.no_show_recorded",
            actorId,
            "appointment",
            appointment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            null,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapAppointment(appointment, actorId, roleCode, includeReportContent: false);
    }

    public async Task<PatientMedicalReportResponse> GetPublishedPatientReportAsync(
        ulong patientId,
        ulong appointmentId,
        CancellationToken cancellationToken)
    {
        var appointment = await database.Appointments.AsNoTracking()
            .Include(item => item.ProfessionalAccount).ThenInclude(item => item.Account)
            .Include(item => item.AppointmentType)
            .Include(item => item.MedicalReport)
            .SingleOrDefaultAsync(item => item.Id == appointmentId
                && item.PatientAccountId == patientId
                && item.StatusCode == "completed"
                && item.MedicalReport != null
                && item.MedicalReport.StatusCode == "published", cancellationToken)
            ?? throw NotFound("Relatório médico não encontrado.");
        var report = appointment.MedicalReport!;
        await auditWriter.WriteAsync(
            "medical_report.patient_viewed",
            patientId,
            "medical_report",
            report.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            null,
            cancellationToken);
        return new PatientMedicalReportResponse(
            appointment.Id,
            appointment.ProfessionalAccount.Account.FullName,
            appointment.AppointmentType.Name,
            appointment.StartsAtUtc,
            report.ClinicalSummary,
            report.Recommendations,
            report.PublishedAtUtc!.Value);
    }

    private IQueryable<Appointment> VisibleAppointments(ulong actorId, string roleCode)
    {
        var query = database.Appointments.AsQueryable();
        return roleCode switch
        {
            ViverAppRoles.Doctor or ViverAppRoles.Psychologist => query.Where(item => item.ProfessionalAccountId == actorId),
            ViverAppRoles.Manager or ViverAppRoles.Administrator => query,
            _ => throw Forbidden(),
        };
    }

    private async Task<Appointment> AppointmentForUpdateAsync(ulong appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await database.Appointments
            .FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        return appointment ?? throw NotFound("Consulta não encontrada.");
    }

    private ClinicalAppointmentResponse MapAppointment(
        Appointment appointment,
        ulong actorId,
        string roleCode,
        bool includeReportContent)
    {
        var isAssignedDoctor = IsClinicalProfessional(roleCode) && appointment.ProfessionalAccountId == actorId;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new ClinicalAppointmentResponse(
            appointment.Id,
            appointment.AppointmentNumber,
            appointment.PatientAccountId,
            appointment.PatientAccount.FullName,
            appointment.PatientAccount.Email,
            appointment.PatientAccount.PhoneE164,
            appointment.ProfessionalAccountId,
            appointment.ProfessionalAccount.Account.FullName,
            appointment.AppointmentType.Name,
            appointment.StatusCode,
            appointment.ModalityCode,
            appointment.StartsAtUtc,
            appointment.EndsAtUtc,
            isAssignedDoctor ? appointment.PatientNotes : null,
            appointment.RowVersion,
            appointment.MedicalReport is null ? null : MapReport(appointment.MedicalReport, includeReportContent),
            isAssignedDoctor && appointment.StatusCode is ("confirmed" or "arrived" or "in_progress"),
            appointment.StatusCode is ("confirmed" or "arrived") && appointment.StartsAtUtc <= now
                && (isAssignedDoctor || roleCode is ViverAppRoles.Manager or ViverAppRoles.Administrator));
    }

    private static ClinicalReportResponse MapReport(MedicalReport report, bool includeContent) => new(
        report.Id,
        report.StatusCode,
        includeContent ? report.ClinicalSummary : null,
        includeContent ? report.Recommendations : null,
        report.UpdatedAtUtc,
        report.PublishedAtUtc,
        report.RowVersion,
        includeContent);

    private static string ValidateSummary(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length is >= 20 and <= 12000
            ? trimmed
            : throw BadRequest("O resumo clínico deve ter entre 20 e 12.000 caracteres.");
    }

    private static string? ValidateRecommendations(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed?.Length > 8000)
        {
            throw BadRequest("As recomendações devem ter no máximo 8.000 caracteres.");
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw BadRequest("Paginação inválida.");
        }
    }

    private static void EnsureClinicalRole(string roleCode)
    {
        if (roleCode is not (ViverAppRoles.Doctor or ViverAppRoles.Psychologist or ViverAppRoles.Manager or ViverAppRoles.Administrator))
        {
            throw Forbidden();
        }
    }

    private static bool IsClinicalProfessional(string roleCode) =>
        roleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist;

    private async Task<TimeZoneInfo> GetClinicTimezoneAsync(CancellationToken cancellationToken)
    {
        var name = await database.Clinics.AsNoTracking()
            .Where(item => item.SingletonId == 1)
            .Select(item => item.TimezoneName)
            .SingleOrDefaultAsync(cancellationToken) ?? "America/Sao_Paulo";
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(name);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException("clinic_timezone_invalid");
        }
    }

    private static DateTime StartOfDayUtc(DateOnly date, TimeZoneInfo timezone) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), timezone);

    private void SetAppointmentConcurrency(Appointment appointment, ulong version)
    {
        if (version == 0)
        {
            throw Conflict("Versão da consulta obrigatória.");
        }

        database.Entry(appointment).Property(item => item.RowVersion).OriginalValue = version;
        appointment.RowVersion = checked(version + 1);
    }

    private void SetConcurrency(MedicalReport report, ulong version)
    {
        if (version == 0)
        {
            throw Conflict("Versão do relatório obrigatória.");
        }

        database.Entry(report).Property(item => item.RowVersion).OriginalValue = version;
        report.RowVersion = checked(version + 1);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict("Os dados foram alterados por outra sessão. Recarregue e tente novamente.");
        }
        catch (DbUpdateException)
        {
            throw Conflict("A operação clínica não pôde ser concluída.");
        }
    }

    private static ClinicalRuleException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
    private static ClinicalRuleException Forbidden() => new(StatusCodes.Status403Forbidden, "Você não pode acessar este recurso.");
    private static ClinicalRuleException NotFound(string message) => new(StatusCodes.Status404NotFound, message);
    private static ClinicalRuleException Conflict(string message) => new(StatusCodes.Status409Conflict, message);
}
