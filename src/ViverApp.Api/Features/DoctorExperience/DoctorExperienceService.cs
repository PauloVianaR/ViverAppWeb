using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.DoctorExperience;

public sealed class DoctorExperienceService(ViverAppDbContext database, UserManager<ViverAppUser> users,
    IdentityChallengeService challenges, IClinicalOperationsAuditWriter audit, TimeProvider clock)
{
    private static readonly string[] AppointmentStatuses = ["pending", "confirmed", "arrived", "in_progress", "completed", "canceled", "rescheduled", "no_show"];
    private static readonly string[] Modalities = ["in_person", "online"];
    private static readonly string[] Categories = ["consultation", "examination", "surgery", "procedure"];

    public async Task<DoctorCapabilitiesResponse> CapabilitiesAsync(CancellationToken ct) =>
        new(await SettingEnabledAsync("professional.patient_scheduling_enabled", true, ct));

    public async Task EnsurePatientSchedulingEnabledAsync(CancellationToken ct)
    {
        if (!await SettingEnabledAsync("professional.patient_scheduling_enabled", true, ct))
            throw Forbidden("O agendamento de pacientes pelo profissional está desabilitado nas configurações administrativas.");
    }

    public async Task<DoctorHomeResponse> HomeAsync(ulong doctor, CancellationToken ct)
    {
        var profile = await ProfileAsync(doctor, ct);
        var timezone = await TimezoneAsync(ct); var now = clock.GetUtcNow().UtcDateTime;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), timezone);
        var todayStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), timezone);
        var tomorrow = todayStart.AddDays(1); var week = todayStart.AddDays(7);
        var query = database.Appointments.AsNoTracking().Where(x => x.ProfessionalAccountId == doctor && x.StartsAtUtc >= todayStart && x.StartsAtUtc < week && x.InverseRescheduledFromAppointment == null);
        var rows = await query.Include(x => x.PatientAccount).ThenInclude(x => x.PatientProfile).Include(x => x.AppointmentType).Include(x => x.CurrentPayment)
            .Include(x => x.AppointmentReview).Include(x => x.AppointmentRescheduleHistories).OrderBy(x => x.StartsAtUtc).ToArrayAsync(ct);
        var sources = new DoctorHomeSources(rows.Where(x => x.StartsAtUtc < tomorrow).Select(x => x.AppointmentNumber).ToArray(),
            rows.Select(x => x.AppointmentNumber).ToArray(), rows.Where(x => x.ModalityCode == "online").Select(x => x.AppointmentNumber).ToArray(),
            rows.Where(x => x.ModalityCode == "in_person").Select(x => x.AppointmentNumber).ToArray());
        return new(profile, new(rows.Count(x => x.StartsAtUtc < tomorrow), rows.Length,
            rows.Count(x => x.ModalityCode == "online"), rows.Count(x => x.ModalityCode == "in_person")),
            sources, rows.Where(x => x.StartsAtUtc < tomorrow).Select(x => MapAppointment(x, now)).ToArray());
    }

    public async Task<ProfessionalProfileResponse> ProfileAsync(ulong doctor, CancellationToken ct)
    {
        var account = await database.Accounts.AsNoTracking().Where(x => x.Id == doctor && (x.RoleCode == ViverAppRoles.Doctor || x.RoleCode == ViverAppRoles.Psychologist) && x.StatusCode == "active")
            .Include(x => x.ProfessionalProfile)!.ThenInclude(x => x!.ProfessionalSpecialties).ThenInclude(x => x.Specialty)
            .SingleOrDefaultAsync(ct) ?? throw Missing();
        var preference = await database.ProfessionalPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.ProfessionalAccountId == doctor, ct);
        var ratings = await database.AppointmentReviews.AsNoTracking().Where(x => x.Appointment.ProfessionalAccountId == doctor)
            .GroupBy(_ => 1).Select(x => new { Average = x.Average(y => (double)y.Rating), Count = x.Count() }).SingleOrDefaultAsync(ct);
        var profile = account.ProfessionalProfile ?? throw Missing();
        var specialties = profile.ProfessionalSpecialties.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Specialty.Name)
            .Select(x => new ProfessionalSpecialtyResponse(x.SpecialtyId, x.Specialty.Name, x.IsPrimary)).ToArray();
        return new(account.Id, account.FullName, account.Email, account.PhoneE164, account.TaxId,
            profile.ProfessionalTitle, profile.LicenseStateCode, profile.LicenseNumber, profile.Biography,
            profile.YearsExperience, profile.DefaultAppointmentDurationMinutes, specialties,
            preference?.EmailEnabled ?? true, preference?.SmsEnabled ?? true, preference?.OnlineEnabled ?? true,
            preference?.MaxOnlineDaily ?? 8, preference?.MaxInPersonDaily ?? 16,
            ratings?.Average, ratings?.Count ?? 0, account.RowVersion, profile.RowVersion, preference?.RowVersion ?? 0, profile.LicenseTypeCode);
    }

    public async Task<ProfessionalProfileResponse> UpdateProfileAsync(ulong doctor, ProfessionalProfileUpdateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProfessionalTitle)) throw Invalid("Informe um título profissional.");
        var ids = request.SpecialtyIds.Distinct().ToArray();
        if (ids.Length == 0 || !ids.Contains(request.PrimarySpecialtyId)) throw Invalid("Selecione uma especialidade principal válida.");
        var specialties = await database.Specialties.Where(x => x.IsActive && ids.Contains(x.Id)).ToArrayAsync(ct);
        if (specialties.Length != ids.Length) throw Invalid("Uma ou mais especialidades estão inativas ou não existem.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var account = await database.Accounts.Include(x => x.ProfessionalProfile)!.ThenInclude(x => x!.ProfessionalSpecialties)
            .SingleOrDefaultAsync(x => x.Id == doctor && (x.RoleCode == ViverAppRoles.Doctor || x.RoleCode == ViverAppRoles.Psychologist), ct) ?? throw Missing();
        var preference = await database.ProfessionalPreferences.SingleAsync(x => x.ProfessionalAccountId == doctor, ct);
        RequireVersion(account.RowVersion, request.AccountRowVersion); RequireVersion(account.ProfessionalProfile!.RowVersion, request.ProfileRowVersion);
        RequireVersion(preference.RowVersion, request.PreferenceRowVersion);
        var now = clock.GetUtcNow().UtcDateTime;
        account.FullName = request.FullName.Trim(); account.UpdatedAtUtc = now; account.RowVersion++;
        var profile = account.ProfessionalProfile; profile.ProfessionalTitle = request.ProfessionalTitle.Trim();
        profile.Biography = Text(request.Biography); profile.YearsExperience = request.YearsExperience;
        profile.DefaultAppointmentDurationMinutes = request.DefaultAppointmentDurationMinutes; profile.UpdatedAtUtc = now; profile.RowVersion++;
        // Preserve unchanged composite-key links instead of deleting and recreating them
        // on every profile update.
        var existingSpecialties = profile.ProfessionalSpecialties.ToArray();
        foreach (var existing in existingSpecialties)
        {
            if (!ids.Contains(existing.SpecialtyId)) database.ProfessionalSpecialties.Remove(existing);
            else existing.IsPrimary = existing.SpecialtyId == request.PrimarySpecialtyId;
        }
        foreach (var specialty in specialties.Where(x => existingSpecialties.All(existing => existing.SpecialtyId != x.Id)))
            database.ProfessionalSpecialties.Add(new()
            {
                ProfessionalAccountId = doctor,
                SpecialtyId = specialty.Id,
                IsPrimary = specialty.Id == request.PrimarySpecialtyId
            });
        preference.EmailEnabled = request.EmailEnabled; preference.SmsEnabled = request.SmsEnabled; preference.UpdatedAtUtc = now; preference.RowVersion++;
        await SaveAsync(ct); await audit.WriteAsync("professional.profile.updated", doctor, "professional_profile", doctor.ToString(), null, ct);
        await transaction.CommitAsync(ct); return await ProfileAsync(doctor, ct);
    }

    public async Task<IReadOnlyList<ProfessionalServiceResponse>> ServicesAsync(ulong doctor, CancellationToken ct)
    {
        await RequireDoctorAsync(doctor, ct);
        var offered = await database.ProfessionalServices.AsNoTracking().Where(x => x.ProfessionalAccountId == doctor)
            .ToDictionaryAsync(x => x.AppointmentTypeId, ct);
        var types = await database.AppointmentTypes.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToArrayAsync(ct);
        return types.Select(x => new ProfessionalServiceResponse(x.Id, x.Name, x.Description, x.CategoryCode, x.ModalityCode,
            x.DurationMinutes, x.PriceAmount, x.RequiresPayment, x.IsActive, offered.TryGetValue(x.Id, out var link) && link.IsActive,
            offered.TryGetValue(x.Id, out link) ? link.RowVersion : 0)).ToArray();
    }

    public async Task<IReadOnlyList<ProfessionalServiceResponse>> UpdateServicesAsync(ulong doctor, ProfessionalServicesUpdateRequest request, CancellationToken ct)
    {
        var ids = request.AppointmentTypeIds.Distinct().ToArray();
        if (await database.AppointmentTypes.CountAsync(x => ids.Contains(x.Id) && x.IsActive, ct) != ids.Length)
            throw Invalid("Um ou mais serviços estão inativos ou não existem.");
        var existing = await database.ProfessionalServices.Where(x => x.ProfessionalAccountId == doctor).ToArrayAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var row in existing) { row.IsActive = ids.Contains(row.AppointmentTypeId); row.UpdatedAtUtc = now; row.RowVersion++; }
        foreach (var id in ids.Except(existing.Select(x => x.AppointmentTypeId))) database.ProfessionalServices.Add(new()
        { ProfessionalAccountId = doctor, AppointmentTypeId = id, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 });
        await SaveAsync(ct); await audit.WriteAsync("professional.services.updated", doctor, "professional_profile", doctor.ToString(),
            new Dictionary<string, string> { ["activeCount"] = ids.Length.ToString() }, ct);
        return await ServicesAsync(doctor, ct);
    }

    public async Task<DoctorAgendaResponse> AgendaAsync(ulong doctor, DateOnly from, DateOnly to, string? status,
        string? modality, string? category, ulong? appointmentNumber, string? search, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); if (to < from || to.DayNumber - from.DayNumber > 366) throw Invalid("Período inválido.");
        if (status is not null && !AppointmentStatuses.Contains(status)) throw Invalid("Estado inválido.");
        if (modality is not null && !Modalities.Contains(modality)) throw Invalid("Modalidade inválida.");
        if (category is not null && !Categories.Contains(category)) throw Invalid("Tipo inválido.");
        var timezone = await TimezoneAsync(ct); var start = StartUtc(from, timezone); var end = StartUtc(to.AddDays(1), timezone);
        var query = database.Appointments.AsNoTracking().Where(x => x.ProfessionalAccountId == doctor && x.StartsAtUtc >= start && x.StartsAtUtc < end && x.InverseRescheduledFromAppointment == null);
        if (status == "rescheduled") query = query.Where(x => x.AppointmentRescheduleHistories.Any() || x.RescheduledFromAppointmentId != null);
        else if (status is not null) query = query.Where(x => x.StatusCode == status);
        if (modality is not null) query = query.Where(x => x.ModalityCode == modality);
        if (category is not null) query = query.Where(x => x.AppointmentType.CategoryCode == category);
        if (appointmentNumber.HasValue) query = query.Where(x => x.AppointmentNumber == appointmentNumber);
        var term = Text(search); if (term is { Length: > 120 }) throw Invalid("A busca deve ter no máximo 120 caracteres.");
        if (term is not null) { var isNumber = ulong.TryParse(term, out var number); query = query.Where(x => x.PatientAccount.FullName.Contains(term) || x.AppointmentType.Name.Contains(term) || isNumber && x.AppointmentNumber == number); }
        var totalNumbers = await query.Select(x => x.AppointmentNumber).ToArrayAsync(ct);
        var countersSource = database.Appointments.AsNoTracking().Where(x => x.ProfessionalAccountId == doctor && x.StartsAtUtc >= start && x.StartsAtUtc < end && x.InverseRescheduledFromAppointment == null);
        var sourceRows = await countersSource.Select(x => new { x.AppointmentNumber, x.ModalityCode, Rescheduled = x.AppointmentRescheduleHistories.Any() || x.RescheduledFromAppointmentId != null }).ToArrayAsync(ct);
        var sources = new DoctorAgendaSources(totalNumbers,
            sourceRows.Where(x => x.ModalityCode == "online").Select(x => x.AppointmentNumber).ToArray(),
            sourceRows.Where(x => x.ModalityCode == "in_person").Select(x => x.AppointmentNumber).ToArray(),
            sourceRows.Where(x => x.Rescheduled).Select(x => x.AppointmentNumber).ToArray());
        var counters = new DoctorAgendaCounters(sources.Total.Count, sources.Online.Count, sources.InPerson.Count, sources.Rescheduled.Count);
        var total = totalNumbers.Length; var now = clock.GetUtcNow().UtcDateTime;
        var rows = await query.Include(x => x.PatientAccount).ThenInclude(x => x.PatientProfile).Include(x => x.AppointmentType).Include(x => x.CurrentPayment)
            .Include(x => x.AppointmentReview).Include(x => x.AppointmentRescheduleHistories).OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(counters, sources, new(rows.Select(x => MapAppointment(x, now)).ToArray(), page, pageSize, total));
    }

    public async Task<DoctorAppointmentDetailResponse> AppointmentAsync(ulong doctor, ulong id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var item = await AppointmentQuery(doctor).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        var versions = await database.MedicalReportVersions.AsNoTracking().Where(x => x.MedicalReport.AppointmentId == id && x.AuthorProfessionalAccountId == doctor)
            .OrderByDescending(x => x.VersionNumber).Select(x => new DoctorReportVersionResponse(x.VersionNumber, x.ClinicalSummary,
                x.Recommendations, x.ChangeReason, x.CreatedAtUtc, x.AuthorProfessionalAccountId)).ToArrayAsync(ct);
        var documents = await database.AppointmentDocuments.AsNoTracking().Where(x => x.AppointmentId == id && x.StatusCode == "available")
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new DoctorDocumentResponse(x.Id, x.OriginalFileName, x.ContentType,
                x.SizeBytes, x.CreatedAtUtc, x.RowVersion)).ToArrayAsync(ct);
        if (item.MedicalReport is not null) await audit.WriteAsync("medical_report.viewed", doctor, "medical_report", item.MedicalReport.Id.ToString(), null, ct);
        return new(MapAppointment(item, now), versions, documents);
    }

    public async Task<DoctorPatientsResponse> PatientsAsync(ulong doctor, string? search, string? status, bool? premium,
        int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); if (status is not null && status is not ("active" or "blocked")) throw Invalid("Estado inválido.");
        var visible = database.Accounts.AsNoTracking().Where(x => x.RoleCode == ViverAppRoles.Patient);
        var term = Text(search); if (term is { Length: > 120 }) throw Invalid("A busca deve ter no máximo 120 caracteres.");
        if (term is not null) visible = visible.Where(x => x.FullName.Contains(term) || x.Email != null && x.Email.Contains(term) || x.PhoneE164 != null && x.PhoneE164.Contains(term));
        if (status is not null) visible = visible.Where(x => x.StatusCode == status);
        var now = clock.GetUtcNow().UtcDateTime;
        if (premium.HasValue) visible = visible.Where(x => x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now)) == premium.Value);
        var all = database.Accounts.AsNoTracking().Where(x => x.RoleCode == ViverAppRoles.Patient);
        var counters = new DoctorPatientCounters(await all.CountAsync(ct), await all.CountAsync(x => x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now)), ct),
            await all.CountAsync(x => x.StatusCode == "active", ct), await all.CountAsync(x => x.StatusCode == "blocked", ct));
        var total = await visible.CountAsync(ct); var rows = await visible.Include(x => x.PatientProfile).Include(x => x.PremiumMembershipAccounts).ThenInclude(x => x.PremiumPlan)
            .OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray(); var appts = await database.Appointments.AsNoTracking().Where(x => x.ProfessionalAccountId == doctor && ids.Contains(x.PatientAccountId) && x.InverseRescheduledFromAppointment == null).ToArrayAsync(ct);
        var result = rows.Select(x =>
        {
            var history = appts.Where(a => a.PatientAccountId == x.Id).ToArray(); return new DoctorPatientResponse(x.Id, x.FullName,
            x.PatientProfile?.PreferredName, x.Email, x.PhoneE164, x.PatientProfile?.BirthDate is { } birth ? DateOnly.FromDateTime(birth) : null,
            x.StatusCode, x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now)),
            x.PremiumMembershipAccounts.Where(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now)).Select(m => m.PremiumPlan.AppointmentDiscountPercent).FirstOrDefault(),
            history.Length, history.Where(a => a.StartsAtUtc < now).Select(a => (DateTime?)a.StartsAtUtc).Max(),
            history.Where(a => a.StartsAtUtc >= now && a.StatusCode is "pending" or "confirmed").Select(a => (DateTime?)a.StartsAtUtc).Min(), x.RowVersion);
        }).ToArray();
        var sources = new DoctorPatientSources(await all.OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct),
            await all.Where(x => x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now))).OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct),
            await all.Where(x => x.StatusCode == "active").OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct),
            await all.Where(x => x.StatusCode == "blocked").OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct));
        return new(counters, sources, new(result, page, pageSize, total));
    }

    public async Task<DoctorPatientResponse> LinkPatientAsync(ulong doctor, ProfessionalPatientLinkRequest request, CancellationToken ct)
    {
        var email = IdentifierNormalizer.NormalizeEmail(request.Identifier); var phone = IdentifierNormalizer.NormalizePhone(request.Identifier);
        if (email is null && phone is null) throw Invalid("Informe o e-mail ou telefone completo do paciente.");
        var patient = await database.Accounts.SingleOrDefaultAsync(x => x.RoleCode == ViverAppRoles.Patient && x.StatusCode == "active"
            && (email != null && x.NormalizedEmail == email && x.EmailVerified || phone != null && x.PhoneE164 == phone && x.PhoneVerified), ct) ?? throw Missing();
        await EnsureLinkAsync(doctor, patient.Id, doctor, ct);
        return await PatientAsync(doctor, patient.Id, ct);
    }

    public async Task<DoctorPatientResponse> PatientAsync(ulong doctor, ulong patientId, CancellationToken ct)
    {
        if (!await VisiblePatientAsync(doctor, patientId, ct)) throw Missing();
        var patient = await database.Accounts.AsNoTracking().Include(x => x.PatientProfile).Include(x => x.PremiumMembershipAccounts).ThenInclude(x => x.PremiumPlan)
            .SingleAsync(x => x.Id == patientId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var history = await database.Appointments.AsNoTracking().Where(x => x.ProfessionalAccountId == doctor && x.PatientAccountId == patientId && x.InverseRescheduledFromAppointment == null).ToArrayAsync(ct);
        return new(patient.Id, patient.FullName, patient.PatientProfile?.PreferredName, patient.Email, patient.PhoneE164,
            patient.PatientProfile?.BirthDate is { } birth ? DateOnly.FromDateTime(birth) : null, patient.StatusCode,
            patient.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now)),
            patient.PremiumMembershipAccounts.Where(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now)).Select(m => m.PremiumPlan.AppointmentDiscountPercent).FirstOrDefault(),
            history.Length, history.Where(x => x.StartsAtUtc < now).Select(x => (DateTime?)x.StartsAtUtc).Max(),
            history.Where(x => x.StartsAtUtc >= now && x.StatusCode is "pending" or "confirmed").Select(x => (DateTime?)x.StartsAtUtc).Min(), patient.RowVersion);
    }

    public async Task<DoctorPatientResponse> InvitePatientAsync(ulong doctor, DoctorPatientInviteRequest request, CancellationToken ct)
    {
        var email = IdentifierNormalizer.NormalizeEmail(request.Email); var phone = IdentifierNormalizer.NormalizePhone(request.PhoneE164);
        if (email is null && phone is null) throw Invalid("Informe um e-mail ou telefone brasileiro válido.");
        if (await database.Accounts.AnyAsync(x => email != null && x.NormalizedEmail == email || phone != null && x.PhoneE164 == phone, ct))
            throw Conflict("Esse contato já pertence a uma conta. Use a opção de vincular paciente.");
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var user = new ViverAppUser
        {
            UserName = email ?? phone,
            NormalizedUserName = email ?? phone,
            RoleCode = ViverAppRoles.Patient,
            StatusCode = "pending_confirmation",
            FullName = request.FullName.Trim(),
            Email = request.Email?.Trim(),
            NormalizedEmail = email,
            PhoneNumber = phone,
            BirthDate = request.BirthDate?.ToDateTime(TimeOnly.MinValue)
        };
        var created = await users.CreateAsync(user); if (!created.Succeeded) throw Conflict("Não foi possível convidar o paciente.");
        var now = clock.GetUtcNow().UtcDateTime;
        database.PatientProfiles.Add(new() { AccountId = user.Id, BirthDate = user.BirthDate, CreatedAtUtc = now, UpdatedAtUtc = now });
        database.ProfessionalPatientLinks.Add(new()
        {
            ProfessionalAccountId = doctor,
            PatientAccountId = user.Id,
            CreatedByAccountId = doctor,
            StatusCode = "active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        });
        await database.SaveChangesAsync(ct);
        var channel = email is not null ? "email" : "sms";
        await challenges.CreateAsync(user, "contact_verification", channel, email ?? phone!, ct);
        await audit.WriteAsync("doctor.patient.invited", doctor, "account", user.Id.ToString(), new Dictionary<string, string> { ["channel"] = channel }, ct);
        await transaction.CommitAsync(ct);
        return await PatientAsync(doctor, user.Id, ct);
    }

    public async Task<DoctorPatientResponse> UpdatePatientAsync(ulong doctor, ulong patient, DoctorPatientUpdateRequest request, CancellationToken ct)
    {
        if (!await VisiblePatientAsync(doctor, patient, ct)) throw Missing();
        var account = await database.Accounts.Include(x => x.PatientProfile).SingleAsync(x => x.Id == patient, ct);
        RequireVersion(account.RowVersion, request.RowVersion); var now = clock.GetUtcNow().UtcDateTime;
        account.PatientProfile ??= new PatientProfile { AccountId = patient, CreatedAtUtc = now };
        account.PatientProfile.PreferredName = Text(request.PreferredName); account.PatientProfile.UpdatedAtUtc = now;
        account.UpdatedAtUtc = now; account.RowVersion++;
        await SaveAsync(ct); await audit.WriteAsync("doctor.patient.operational_data.updated", doctor, "account", patient.ToString(), null, ct);
        return await PatientAsync(doctor, patient, ct);
    }

    public async Task<DoctorAvailabilityResponse> AvailabilityAsync(ulong doctor, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366) throw Invalid("Período inválido.");
        var preference = await database.ProfessionalPreferences.AsNoTracking().SingleAsync(x => x.ProfessionalAccountId == doctor, ct);
        var hourRows = await database.ProfessionalWeeklyHours.AsNoTracking().Where(x => x.ProfessionalAccountId == doctor).OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime).ToArrayAsync(ct);
        var hours = hourRows.Select(x => new ProfessionalWeeklyHourResponse(x.Id, x.DayOfWeek, TimeOnly.FromTimeSpan(x.StartTime), TimeOnly.FromTimeSpan(x.EndTime),
            x.ValidFrom.HasValue ? DateOnly.FromDateTime(x.ValidFrom.Value) : null, x.ValidUntil.HasValue ? DateOnly.FromDateTime(x.ValidUntil.Value) : null, x.IsActive, x.RowVersion, x.ModalityCode)).ToArray();
        var exceptionRows = await database.ProfessionalAvailabilityExceptions.AsNoTracking().Where(x => x.ProfessionalAccountId == doctor
            && x.ExceptionDate >= from.ToDateTime(TimeOnly.MinValue) && x.ExceptionDate <= to.ToDateTime(TimeOnly.MinValue)).OrderBy(x => x.ExceptionDate).ThenBy(x => x.StartTime)
            .ToArrayAsync(ct);
        var exceptions = exceptionRows.Select(x => new ProfessionalAvailabilityExceptionResponse(x.Id, DateOnly.FromDateTime(x.ExceptionDate), x.ModalityCode, x.IsAvailable,
            x.StartTime.HasValue ? TimeOnly.FromTimeSpan(x.StartTime.Value) : null, x.EndTime.HasValue ? TimeOnly.FromTimeSpan(x.EndTime.Value) : null, x.RowVersion)).ToArray();
        return new(preference.OnlineEnabled, preference.MaxOnlineDaily, preference.MaxInPersonDaily, preference.RowVersion, hours, exceptions);
    }

    public async Task<DoctorAvailabilityResponse> UpdateAvailabilityAsync(ulong doctor, DoctorAvailabilitySettingsRequest request, CancellationToken ct)
    {
        var preference = await database.ProfessionalPreferences.SingleOrDefaultAsync(x => x.ProfessionalAccountId == doctor, ct) ?? throw Missing();
        RequireVersion(preference.RowVersion, request.RowVersion); preference.OnlineEnabled = request.OnlineEnabled;
        preference.MaxOnlineDaily = request.MaxOnlineDaily; preference.MaxInPersonDaily = request.MaxInPersonDaily;
        preference.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; preference.RowVersion++;
        await SaveAsync(ct); await audit.WriteAsync("professional.availability.settings.updated", doctor, "professional_profile", doctor.ToString(), null, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow); return await AvailabilityAsync(doctor, today, today.AddDays(90), ct);
    }

    public async Task<ProfessionalAvailabilityExceptionResponse> SaveExceptionAsync(ulong doctor, ulong? id,
        ProfessionalAvailabilityExceptionRequest request, CancellationToken ct)
    {
        if (request.Date < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddDays(-1))) throw Invalid("A exceção deve ser atual ou futura.");
        var holidayDate = request.Date.ToDateTime(TimeOnly.MinValue);
        var holiday = await database.Holidays.AsNoTracking().AnyAsync(x => x.HolidayDate == holidayDate
            || x.IsAnnual && x.HolidayDate.Month == holidayDate.Month && x.HolidayDate.Day == holidayDate.Day, ct);
        if (request.IsAvailable && holiday) throw Conflict("Não é possível abrir disponibilidade em um feriado cadastrado.");
        var start = request.StartsAt?.ToTimeSpan(); var end = request.EndsAt?.ToTimeSpan();
        if (request.IsAvailable && request.ModalityCode is "in_person" or "both" && !await InsideClinicHours(request.Date, start!.Value, end!.Value, ct))
            throw Conflict("A faixa presencial precisa estar dentro do horário da clínica.");
        var overlap = await database.ProfessionalAvailabilityExceptions.AsNoTracking().AnyAsync(x => x.ProfessionalAccountId == doctor
            && x.ExceptionDate == request.Date.ToDateTime(TimeOnly.MinValue) && x.Id != id && (x.ModalityCode == request.ModalityCode || x.ModalityCode == "both" || request.ModalityCode == "both")
            && (!x.IsAvailable || !request.IsAvailable || x.StartTime < end && x.EndTime > start), ct);
        if (overlap) throw Conflict("A exceção conflita com outra configuração da data.");
        var now = clock.GetUtcNow().UtcDateTime; ProfessionalAvailabilityException entity;
        if (id.HasValue) { entity = await database.ProfessionalAvailabilityExceptions.SingleOrDefaultAsync(x => x.Id == id && x.ProfessionalAccountId == doctor, ct) ?? throw Missing(); RequireVersion(entity.RowVersion, request.RowVersion); entity.RowVersion++; }
        else { if (request.RowVersion != 0) throw Conflict("Versão inválida para nova exceção."); entity = new() { ProfessionalAccountId = doctor, CreatedAtUtc = now, RowVersion = 1 }; database.ProfessionalAvailabilityExceptions.Add(entity); }
        entity.ExceptionDate = request.Date.ToDateTime(TimeOnly.MinValue); entity.ModalityCode = request.ModalityCode;
        entity.IsAvailable = request.IsAvailable; entity.StartTime = start; entity.EndTime = end; entity.UpdatedAtUtc = now;
        await SaveAsync(ct); await audit.WriteAsync(id.HasValue ? "professional.availability.exception.updated" : "professional.availability.exception.created",
            doctor, "professional_availability_exception", entity.Id.ToString(), null, ct);
        return new(entity.Id, request.Date, entity.ModalityCode, entity.IsAvailable, request.StartsAt, request.EndsAt, entity.RowVersion);
    }

    public async Task DeleteExceptionAsync(ulong doctor, ulong id, ulong version, CancellationToken ct)
    {
        var entity = await database.ProfessionalAvailabilityExceptions.SingleOrDefaultAsync(x => x.Id == id && x.ProfessionalAccountId == doctor, ct) ?? throw Missing();
        RequireVersion(entity.RowVersion, version); database.ProfessionalAvailabilityExceptions.Remove(entity); await SaveAsync(ct);
        await audit.WriteAsync("professional.availability.exception.deleted", doctor, "professional_availability_exception", id.ToString(), null, ct);
    }

    public async Task<IReadOnlyList<DoctorReportVersionResponse>> RectifyAsync(ulong doctor, ulong appointment,
        DoctorReportRectificationRequest request, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var report = await database.MedicalReports.FromSqlInterpolated($"SELECT mr.* FROM medical_reports mr JOIN appointments a ON a.id=mr.appointment_id WHERE mr.appointment_id={appointment} AND a.professional_account_id={doctor} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw Missing();
        if (report.StatusCode != "published") throw Conflict("Somente um laudo publicado pode ser retificado.");
        RequireVersion(report.RowVersion, request.ReportRowVersion);
        var version = (await database.MedicalReportVersions.Where(x => x.MedicalReportId == report.Id).MaxAsync(x => (uint?)x.VersionNumber, ct) ?? 0) + 1;
        var summary = request.ClinicalSummary.Trim(); var recommendations = Text(request.Recommendations); var reason = request.Reason.Trim();
        var now = clock.GetUtcNow().UtcDateTime;
        database.MedicalReportVersions.Add(new()
        {
            MedicalReportId = report.Id,
            VersionNumber = version,
            AuthorProfessionalAccountId = doctor,
            ClinicalSummary = summary,
            Recommendations = recommendations,
            ChangeReason = reason,
            CreatedAtUtc = now
        });
        report.ClinicalSummary = summary; report.Recommendations = recommendations; report.UpdatedAtUtc = now; report.RowVersion++;
        await SaveAsync(ct); await audit.WriteAsync("medical_report.rectified", doctor, "medical_report", report.Id.ToString(),
            new Dictionary<string, string> { ["version"] = version.ToString() }, ct);
        await transaction.CommitAsync(ct);
        return await database.MedicalReportVersions.AsNoTracking().Where(x => x.MedicalReportId == report.Id).OrderByDescending(x => x.VersionNumber)
            .Select(x => new DoctorReportVersionResponse(x.VersionNumber, x.ClinicalSummary, x.Recommendations, x.ChangeReason, x.CreatedAtUtc, x.AuthorProfessionalAccountId)).ToArrayAsync(ct);
    }

    private IQueryable<Appointment> AppointmentQuery(ulong doctor) => database.Appointments.AsNoTracking().Where(x => x.ProfessionalAccountId == doctor && x.InverseRescheduledFromAppointment == null)
        .Include(x => x.PatientAccount).ThenInclude(x => x.PatientProfile).Include(x => x.AppointmentType)
        .Include(x => x.CurrentPayment).Include(x => x.AppointmentReview).Include(x => x.MedicalReport)
        .Include(x => x.InverseRescheduledFromAppointment).Include(x => x.AppointmentRescheduleHistories);
    private static DoctorAppointmentResponse MapAppointment(Appointment x, DateTime now)
    {
        var birth = x.PatientAccount.BirthDate ?? x.PatientAccount.PatientProfile?.BirthDate;
        int? age = birth.HasValue ? now.Year - birth.Value.Year - (birth.Value.Date > now.AddYears(-(now.Year - birth.Value.Year)).Date ? 1 : 0) : null;
        return new(x.Id, x.AppointmentNumber, x.PatientAccountId, x.PatientAccount.FullName, age, x.AppointmentTypeId, x.AppointmentType.Name, x.AppointmentType.CategoryCode,
            x.StatusCode, x.ModalityCode, x.StartsAtUtc, x.EndsAtUtc, x.PriceAmount, x.BasePriceAmount ?? x.PriceAmount, x.DiscountPercent, x.RequiresPayment,
            x.CurrentPayment?.StatusCode ?? "unpaid", x.PaymentLocationCode, x.PatientNotes, x.CancellationReason,
            x.RescheduledFromAppointmentId, x.InverseRescheduledFromAppointment?.Id, x.AppointmentReview?.Rating, x.AppointmentReview?.Comment,
            x.ArrivedAtUtc, x.ArrivalQueueNumber,
            x.AppointmentRescheduleHistories.OrderBy(h => h.SequenceNumber).Select(h => new AppointmentRescheduleHistoryResponse(h.SequenceNumber,
                h.PreviousStartsAtUtc, h.PreviousEndsAtUtc, h.NewStartsAtUtc, h.NewEndsAtUtc, h.Reason, h.OccurredAtUtc)).ToArray(),
            x.ModalityCode == "online" && x.StatusCode == "confirmed" && (!x.RequiresPayment || x.CurrentPayment?.StatusCode == "paid") && x.StartsAtUtc <= now.AddMinutes(15) && x.EndsAtUtc >= now,
            x.StatusCode is "pending" or "confirmed", x.StatusCode is "pending" or "confirmed",
            x.StatusCode is "confirmed" or "arrived", x.StatusCode == "in_progress",
            x.StatusCode is "confirmed" or "arrived" or "in_progress", x.RowVersion);
    }
    private async Task EnsureLinkAsync(ulong doctor, ulong patient, ulong creator, CancellationToken ct)
    {
        var link = await database.ProfessionalPatientLinks.SingleOrDefaultAsync(x => x.ProfessionalAccountId == doctor && x.PatientAccountId == patient, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (link is null) database.ProfessionalPatientLinks.Add(new()
        {
            ProfessionalAccountId = doctor,
            PatientAccountId = patient,
            CreatedByAccountId = creator,
            StatusCode = "active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        });
        else if (link.StatusCode != "active") { link.StatusCode = "active"; link.UpdatedAtUtc = now; link.RowVersion++; }
        await SaveAsync(ct); await audit.WriteAsync("doctor.patient.linked", doctor, "account", patient.ToString(), null, ct);
    }
    private Task<bool> VisiblePatientAsync(ulong doctor, ulong patient, CancellationToken ct) => database.Accounts.AsNoTracking()
        .AnyAsync(x => x.Id == patient && x.RoleCode == ViverAppRoles.Patient, ct);
    private async Task RequireDoctorAsync(ulong doctor, CancellationToken ct)
    {
        if (!await database.ProfessionalProfiles.AsNoTracking().AnyAsync(x => x.AccountId == doctor && x.Account.StatusCode == "active", ct)) throw Missing();
    }
    private async Task<bool> InsideClinicHours(DateOnly date, TimeSpan start, TimeSpan end, CancellationToken ct) =>
        await database.ClinicWeeklyHours.AsNoTracking().AnyAsync(x => x.IsActive && x.DayOfWeek == (byte)date.DayOfWeek && x.StartTime <= start && x.EndTime >= end, ct);
    private async Task<TimeZoneInfo> TimezoneAsync(CancellationToken ct)
    {
        var name = await database.Clinics.AsNoTracking().Select(x => x.TimezoneName).SingleOrDefaultAsync(ct) ?? "America/Sao_Paulo";
        try { return TimeZoneInfo.FindSystemTimeZoneById(name); } catch (TimeZoneNotFoundException) { throw new DoctorRuleException(503, "Fuso horário indisponível."); }
    }
    private async Task<bool> SettingEnabledAsync(string key, bool fallback, CancellationToken ct)
    {
        var value = await database.ApplicationSettings.AsNoTracking().Where(x => x.SettingKey == key)
            .Select(x => x.ValueJson).SingleOrDefaultAsync(ct);
        return value is null ? fallback : string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }
    private static DateTime StartUtc(DateOnly date, TimeZoneInfo timezone) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), timezone);
    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Page(int page, int pageSize) { if (page < 1 || pageSize is < 1 or > 100) throw Invalid("Paginação inválida."); }
    private static void RequireVersion(ulong current, ulong supplied) { if (supplied == 0 || supplied != current) throw Conflict("Os dados foram alterados por outra sessão. Recarregue e tente novamente."); }
    private async Task SaveAsync(CancellationToken ct) { try { await database.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict("Os dados foram alterados por outra sessão."); } catch (DbUpdateException) { throw Conflict("A operação não pôde ser concluída."); } }
    internal static DoctorRuleException Invalid(string message) => new(400, message);
    internal static DoctorRuleException Missing() => new(404, "Recurso não encontrado.");
    internal static DoctorRuleException Forbidden(string message) => new(403, message);
    internal static DoctorRuleException Conflict(string message) => new(409, message);
}

internal sealed class DoctorRuleException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
