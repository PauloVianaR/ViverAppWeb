using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.PatientScheduling;

public sealed class PatientSchedulingService(
    ViverAppDbContext database,
    IPatientSchedulingAuditWriter auditWriter,
    TimeProvider timeProvider)
{
    private static readonly string[] ActiveStatuses = ["pending", "confirmed", "arrived", "in_progress"];
    private static readonly string[] MutableStatuses = ["pending", "confirmed"];
    private const int MaximumAvailabilityDays = 31;

    public async Task<SchedulingPage<BookingProfessionalResponse>> SearchProfessionalsAsync(
        int page,
        int pageSize,
        string? search,
        uint? specialtyId,
        uint? appointmentTypeId,
        string? modality,
        CancellationToken cancellationToken)
    {
        ValidatePagination(page, pageSize);
        ValidateModality(modality, allowNull: true);

        if (appointmentTypeId.HasValue)
        {
            var type = await database.AppointmentTypes.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == appointmentTypeId && item.IsActive, cancellationToken)
                ?? throw NotFound("Tipo de atendimento não encontrado.");
            if (modality is not null && !SupportsModality(type.ModalityCode, modality))
            {
                throw Conflict("O tipo de atendimento não oferece a modalidade selecionada.");
            }
        }

        var query = database.Accounts.AsNoTracking()
            .Where(account => (account.RoleCode == ViverAppRoles.Doctor || account.RoleCode == ViverAppRoles.Psychologist)
                && account.StatusCode == "active"
                && account.ProfessionalProfile != null
                && account.ProfessionalProfile.ProfessionalWeeklyHours.Any(hour => hour.IsActive));
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (normalizedSearch is not null)
        {
            query = query.Where(account => account.FullName.Contains(normalizedSearch)
                || account.ProfessionalProfile!.ProfessionalSpecialties.Any(link => link.Specialty.Name.Contains(normalizedSearch)));
        }

        if (specialtyId.HasValue)
        {
            query = query.Where(account => account.ProfessionalProfile!.ProfessionalSpecialties
                .Any(link => link.SpecialtyId == specialtyId && link.Specialty.IsActive));
        }

        if (appointmentTypeId.HasValue)
        {
            var serviceId = appointmentTypeId.Value;
            query = query.Where(account => account.ProfessionalProfile!.ProfessionalServices
                .Any(link => link.AppointmentTypeId == serviceId && link.IsActive && link.AppointmentType.IsActive));
        }

        var total = await query.CountAsync(cancellationToken);
        var accounts = await query
            .Include(account => account.ProfessionalProfile!)
                .ThenInclude(profile => profile.ProfessionalSpecialties)
                .ThenInclude(link => link.Specialty)
            .OrderBy(account => account.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var doctorIds = accounts.Select(x => x.Id).ToArray();
        var reviews = await database.AppointmentReviews.AsNoTracking().Where(x => doctorIds.Contains(x.Appointment.ProfessionalAccountId))
            .GroupBy(x => x.Appointment.ProfessionalAccountId).Select(x => new { Id = x.Key, Average = x.Average(r => (double)r.Rating), Count = x.Count() }).ToDictionaryAsync(x => x.Id, cancellationToken);
        var items = accounts.Select(account => new BookingProfessionalResponse(
            account.Id,
            account.FullName,
            account.ProfessionalProfile!.Biography,
            account.ProfessionalProfile.LicenseStateCode,
            account.ProfessionalProfile.LicenseNumber,
            account.ProfessionalProfile.DefaultAppointmentDurationMinutes,
            account.ProfessionalProfile.ProfessionalSpecialties
                .Where(link => link.Specialty.IsActive)
                .OrderByDescending(link => link.IsPrimary)
                .ThenBy(link => link.Specialty.Name)
                .Select(link => new BookingSpecialtyResponse(link.SpecialtyId, link.Specialty.Name, link.IsPrimary))
                .ToArray(), account.ProfessionalProfile.YearsExperience,
            reviews.TryGetValue(account.Id, out var rating) ? rating.Average : null,
            reviews.TryGetValue(account.Id, out var count) ? count.Count : 0,
            modality != "in_person", account.ProfessionalProfile.LicenseTypeCode)).ToArray();
        return new SchedulingPage<BookingProfessionalResponse>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<AvailableSlotResponse>> GetAvailableSlotsAsync(
        ulong patientId,
        ulong doctorId,
        uint appointmentTypeId,
        string modality,
        DateOnly from,
        int days,
        CancellationToken cancellationToken)
    {
        ValidateModality(modality, allowNull: false);
        if (days is < 1 or > MaximumAvailabilityDays)
        {
            throw BadRequest($"O período deve ter entre 1 e {MaximumAvailabilityDays} dias.");
        }

        var type = await RequireAppointmentTypeAsync(appointmentTypeId, modality, cancellationToken);
        await RequireActiveDoctorAsync(doctorId, cancellationToken);
        var (policy, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        var today = LocalDate(timeProvider.GetUtcNow(), timezone);
        if (from < today || from > today.AddDays(policy.BookingHorizonDays))
        {
            throw BadRequest("A data inicial está fora do período permitido para agendamento.");
        }

        var until = from.AddDays(days - 1);
        var horizon = today.AddDays(policy.BookingHorizonDays);
        if (until > horizon)
        {
            until = horizon;
        }

        return await BuildSlotsAsync(
            patientId,
            doctorId,
            type,
            modality,
            from,
            until,
            timezoneName,
            timezone,
            policy,
            excludedAppointmentId: null,
            cancellationToken);
    }

    public async Task<SchedulingPage<AppointmentResponse>> GetAppointmentsAsync(
        ulong patientId,
        int page,
        int pageSize,
        string view,
        string? modality,
        CancellationToken cancellationToken)
    {
        ValidatePagination(page, pageSize);
        ValidateModality(modality, allowNull: true);
        if (view is not ("future" or "history" or "all"))
        {
            throw BadRequest("A visualização deve ser future, history ou all.");
        }

        var (_, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var query = database.Appointments.AsNoTracking().Where(item => item.PatientAccountId == patientId && item.InverseRescheduledFromAppointment == null);
        query = view switch
        {
            "future" => query.Where(item => ActiveStatuses.Contains(item.StatusCode) && item.StartsAtUtc >= now),
            "history" => query.Where(item => !ActiveStatuses.Contains(item.StatusCode) || item.StartsAtUtc < now),
            _ => query,
        };
        if (modality is not null)
        {
            query = query.Where(item => item.ModalityCode == modality);
        }

        var total = await query.CountAsync(cancellationToken);
        var entities = await IncludeAppointmentGraph(query)
            .OrderBy(item => item.StartsAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var responses = new List<AppointmentResponse>(entities.Count);
        foreach (var entity in entities)
        {
            responses.Add(await ToResponseAsync(entity, timezoneName, timezone, cancellationToken));
        }

        return new SchedulingPage<AppointmentResponse>(responses, page, pageSize, total);
    }

    public async Task<AppointmentResponse> GetAppointmentAsync(
        ulong patientId,
        ulong appointmentId,
        CancellationToken cancellationToken)
    {
        var entity = await IncludeAppointmentGraph(database.Appointments.AsNoTracking())
            .SingleOrDefaultAsync(item => item.Id == appointmentId && item.PatientAccountId == patientId, cancellationToken)
            ?? throw NotFound("Agendamento não encontrado.");
        var (_, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        return await ToResponseAsync(entity, timezoneName, timezone, cancellationToken);
    }

    public async Task<(AppointmentResponse Response, bool Replayed)> CreateAsync(
        ulong patientId,
        string idempotencyKey,
        AppointmentCreateRequest request,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(idempotencyKey);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockPatientAsync(patientId, cancellationToken);
        var requestHash = HashRequest(patientId, "create", request);
        var replay = await TryReplayAsync($"appointment.create:{patientId}", idempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return (replay, true);
        }

        await LockDoctorAsync(request.ProfessionalAccountId, cancellationToken);
        var type = await RequireAppointmentTypeAsync(request.AppointmentTypeId, request.ModalityCode, cancellationToken);
        var doctor = await RequireActiveDoctorAsync(request.ProfessionalAccountId, cancellationToken);
        var (policy, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        var slot = await RequireSlotAsync(
            patientId,
            request.ProfessionalAccountId,
            type,
            request.ModalityCode,
            request.LocalDate,
            request.LocalStartsAt,
            timezoneName,
            timezone,
            policy,
            excludedAppointmentId: null,
            cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var discount = await database.PremiumMemberships.AsNoTracking()
            .Where(x => x.AccountId == patientId && x.StatusCode == "active" && x.StartsAtUtc <= now && (x.EndsAtUtc == null || x.EndsAtUtc > now) && x.PremiumPlan.IsActive)
            .Select(x => (decimal?)x.PremiumPlan.AppointmentDiscountPercent).MaxAsync(cancellationToken) ?? 0;
        var entity = new Appointment
        {
            AppointmentNumber = await AllocateAppointmentNumberAsync(cancellationToken),
            PatientAccountId = patientId,
            ProfessionalAccountId = request.ProfessionalAccountId,
            AppointmentTypeId = request.AppointmentTypeId,
            CreatedByAccountId = patientId,
            StatusCode = type.RequiresPayment ? "pending" : "confirmed",
            ModalityCode = request.ModalityCode,
            StartsAtUtc = slot.StartsAtUtc,
            EndsAtUtc = slot.EndsAtUtc,
            PriceAmount = type.RequiresPayment ? PatientExperience.PatientExperienceService.DiscountedPrice(type.PriceAmount, discount) : 0,
            BasePriceAmount = type.RequiresPayment ? type.PriceAmount : 0,
            DiscountPercent = type.RequiresPayment ? discount : 0,
            RequiresPayment = type.RequiresPayment,
            PaymentLocationCode = "web",
            CurrencyCode = "BRL",
            PatientNotes = OptionalText(request.PatientNotes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Appointments.Add(entity);
        await database.SaveChangesAsync(cancellationToken);
        AddHistory(entity, patientId, null, entity.StatusCode, type.RequiresPayment ? null : "Atendimento sem cobrança confirmado automaticamente", now);
        var response = BuildResponse(entity, doctor.FullName, type.Name, timezoneName, timezone, null);
        StoreIdempotency(
            $"appointment.create:{patientId}",
            idempotencyKey,
            requestHash,
            response,
            StatusCodes.Status201Created,
            now);
        await database.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(
            "appointment.created",
            patientId,
            entity.Id,
            new Dictionary<string, string> { ["modality"] = request.ModalityCode },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (response, false);
    }

    public async Task<(AppointmentResponse Response, bool Replayed)> CreateForDoctorAsync(
        ulong doctorId,
        string idempotencyKey,
        DoctorAppointmentCreateRequest request,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(idempotencyKey);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockPatientAsync(request.PatientAccountId, cancellationToken);
        var isLinked = await database.ProfessionalPatientLinks.AnyAsync(x => x.ProfessionalAccountId == doctorId
            && x.PatientAccountId == request.PatientAccountId && x.StatusCode == "active", cancellationToken)
            || await database.Appointments.AnyAsync(x => x.ProfessionalAccountId == doctorId && x.PatientAccountId == request.PatientAccountId, cancellationToken);
        if (!isLinked) throw NotFound("Paciente não encontrado.");
        var requestHash = HashRequest(doctorId, $"doctor-create:{request.PatientAccountId}", request);
        var scope = $"appointment.doctor-create:{doctorId}";
        var replay = await TryReplayAsync(scope, idempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return (replay, true);
        }

        await LockDoctorAsync(doctorId, cancellationToken);
        var type = await RequireAppointmentTypeAsync(request.AppointmentTypeId, request.ModalityCode, cancellationToken);
        var doctor = await RequireActiveDoctorAsync(doctorId, cancellationToken);
        if (!await database.ProfessionalServices.AnyAsync(x => x.ProfessionalAccountId == doctorId
            && x.AppointmentTypeId == type.Id && x.IsActive && x.AppointmentType.IsActive, cancellationToken))
            throw Conflict("Este serviço não está ativo no seu perfil.");
        var (policy, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        var slot = await RequireSlotAsync(request.PatientAccountId, doctorId, type, request.ModalityCode,
            request.LocalDate, request.LocalStartsAt, timezoneName, timezone, policy, null, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var discount = await database.PremiumMemberships.AsNoTracking()
            .Where(x => x.AccountId == request.PatientAccountId && x.StatusCode == "active" && x.StartsAtUtc <= now
                && (x.EndsAtUtc == null || x.EndsAtUtc > now) && x.PremiumPlan.IsActive)
            .Select(x => (decimal?)x.PremiumPlan.AppointmentDiscountPercent).MaxAsync(cancellationToken) ?? 0;
        var entity = new Appointment
        {
            AppointmentNumber = await AllocateAppointmentNumberAsync(cancellationToken),
            PatientAccountId = request.PatientAccountId,
            ProfessionalAccountId = doctorId,
            AppointmentTypeId = type.Id,
            CreatedByAccountId = doctorId,
            StatusCode = type.RequiresPayment ? "pending" : "confirmed",
            ModalityCode = request.ModalityCode,
            StartsAtUtc = slot.StartsAtUtc,
            EndsAtUtc = slot.EndsAtUtc,
            PriceAmount = type.RequiresPayment ? PatientExperience.PatientExperienceService.DiscountedPrice(type.PriceAmount, discount) : 0,
            BasePriceAmount = type.RequiresPayment ? type.PriceAmount : 0,
            DiscountPercent = type.RequiresPayment ? discount : 0,
            RequiresPayment = type.RequiresPayment,
            PaymentLocationCode = request.ModalityCode == "in_person" ? "clinic" : "web",
            CurrencyCode = "BRL",
            PatientNotes = OptionalText(request.PatientNotes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Appointments.Add(entity);
        await database.SaveChangesAsync(cancellationToken);
        AddHistory(entity, doctorId, null, entity.StatusCode, type.RequiresPayment ? null : "Atendimento sem cobrança confirmado automaticamente", now);
        var response = BuildResponse(entity, doctor.FullName, type.Name, timezoneName, timezone, null);
        StoreIdempotency(scope, idempotencyKey, requestHash, response, StatusCodes.Status201Created, now);
        await database.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync("appointment.created_by_doctor", doctorId, entity.Id,
            new Dictionary<string, string> { ["modality"] = request.ModalityCode }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (response, false);
    }

    public async Task<(AppointmentResponse Response, bool Replayed)> CreateForManagerAsync(
        ulong managerId,
        string idempotencyKey,
        ViverApp.Api.Features.ManagerExperience.ManagerAppointmentCreateRequest request,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(idempotencyKey);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockPatientAsync(request.PatientAccountId, cancellationToken);
        var requestHash = HashRequest(managerId, $"manager-create:{request.PatientAccountId}", request);
        var scope = $"appointment.manager-create:{managerId}";
        var replay = await TryReplayAsync(scope, idempotencyKey, requestHash, cancellationToken);
        if (replay is not null) { await transaction.CommitAsync(cancellationToken); return (replay, true); }
        await LockDoctorAsync(request.ProfessionalAccountId, cancellationToken);
        var type = await RequireAppointmentTypeAsync(request.AppointmentTypeId, request.ModalityCode, cancellationToken);
        var doctor = await RequireActiveDoctorAsync(request.ProfessionalAccountId, cancellationToken);
        if (!await database.ProfessionalServices.AnyAsync(x => x.ProfessionalAccountId == request.ProfessionalAccountId
            && x.AppointmentTypeId == type.Id && x.IsActive && x.AppointmentType.IsActive, cancellationToken))
            throw Conflict("Este serviço não é oferecido pelo profissional selecionado.");
        var (policy, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        var slot = await RequireSlotAsync(request.PatientAccountId, request.ProfessionalAccountId, type, request.ModalityCode,
            request.LocalDate, request.LocalStartsAt, timezoneName, timezone, policy, null, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var discount = await database.PremiumMemberships.AsNoTracking()
            .Where(x => x.AccountId == request.PatientAccountId && x.StatusCode == "active" && x.StartsAtUtc <= now
                && (x.EndsAtUtc == null || x.EndsAtUtc > now) && x.PremiumPlan.IsActive)
            .Select(x => (decimal?)x.PremiumPlan.AppointmentDiscountPercent).MaxAsync(cancellationToken) ?? 0;
        var entity = new Appointment
        {
            AppointmentNumber = await AllocateAppointmentNumberAsync(cancellationToken),
            PatientAccountId = request.PatientAccountId,
            ProfessionalAccountId = request.ProfessionalAccountId,
            AppointmentTypeId = type.Id,
            CreatedByAccountId = managerId,
            StatusCode = type.RequiresPayment ? "pending" : "confirmed",
            ModalityCode = request.ModalityCode,
            StartsAtUtc = slot.StartsAtUtc,
            EndsAtUtc = slot.EndsAtUtc,
            PriceAmount = type.RequiresPayment ? PatientExperience.PatientExperienceService.DiscountedPrice(type.PriceAmount, discount) : 0,
            BasePriceAmount = type.RequiresPayment ? type.PriceAmount : 0,
            DiscountPercent = type.RequiresPayment ? discount : 0,
            RequiresPayment = type.RequiresPayment,
            PaymentLocationCode = request.ModalityCode == "in_person" ? "clinic" : "web",
            CurrencyCode = "BRL",
            PatientNotes = OptionalText(request.PatientNotes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Appointments.Add(entity); await database.SaveChangesAsync(cancellationToken);
        AddHistory(entity, managerId, null, entity.StatusCode, type.RequiresPayment ? null : "Atendimento sem cobrança confirmado automaticamente", now);
        var response = BuildResponse(entity, doctor.FullName, type.Name, timezoneName, timezone, null);
        StoreIdempotency(scope, idempotencyKey, requestHash, response, StatusCodes.Status201Created, now);
        await database.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync("appointment.created_by_manager", managerId, entity.Id,
            new Dictionary<string, string> { ["doctorAccountId"] = request.ProfessionalAccountId.ToString(CultureInfo.InvariantCulture), ["modality"] = request.ModalityCode }, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return (response, false);
    }

    public async Task<AppointmentResponse> CancelForManagerAsync(ulong managerId, ulong appointmentId,
        AppointmentCancelRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var entity = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken) ?? throw NotFound("Agendamento não encontrado.");
        if (!MutableStatuses.Contains(entity.StatusCode)) throw Conflict("Somente agendamentos pendentes ou confirmados podem ser cancelados.");
        if (entity.RowVersion != request.RowVersion) throw Conflict("O agendamento foi alterado por outra sessão.");
        var previous = entity.StatusCode; var now = timeProvider.GetUtcNow().UtcDateTime;
        entity.StatusCode = "canceled"; entity.CancellationReason = request.Reason.Trim(); entity.CanceledByAccountId = managerId;
        entity.CanceledAtUtc = now; entity.UpdatedAtUtc = now; entity.RowVersion++;
        AddHistory(entity, managerId, previous, "canceled", entity.CancellationReason, now); await database.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync("appointment.canceled_by_manager", managerId, entity.Id, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken); var (_, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        var hydrated = await IncludeAppointmentGraph(database.Appointments.AsNoTracking()).SingleAsync(x => x.Id == entity.Id, cancellationToken);
        return await ToResponseAsync(hydrated, timezoneName, timezone, cancellationToken);
    }

    public async Task<(AppointmentResponse Response, bool Replayed)> RescheduleForManagerAsync(ulong managerId,
        ulong appointmentId, string idempotencyKey, AppointmentRescheduleRequest request, CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(idempotencyKey);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var original = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken) ?? throw NotFound("Agendamento não encontrado.");
        if (!MutableStatuses.Contains(original.StatusCode)) throw Conflict("Somente agendamentos pendentes ou confirmados podem ser reagendados.");
        if (original.RowVersion != request.RowVersion) throw Conflict("O agendamento foi alterado por outra sessão.");
        var requestHash = HashRequest(managerId, $"manager-reschedule:{appointmentId}", request); var scope = $"appointment.manager-move:{managerId}";
        var replay = await TryReplayAsync(scope, idempotencyKey, requestHash, cancellationToken);
        if (replay is not null) { await transaction.CommitAsync(cancellationToken); return (replay, true); }
        await LockDoctorAsync(original.ProfessionalAccountId, cancellationToken);
        var type = await RequireAppointmentTypeAsync(original.AppointmentTypeId, original.ModalityCode, cancellationToken);
        var doctor = await RequireActiveDoctorAsync(original.ProfessionalAccountId, cancellationToken);
        var (policy, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        var slot = await RequireSlotAsync(original.PatientAccountId, original.ProfessionalAccountId, type, original.ModalityCode,
            request.LocalDate, request.LocalStartsAt, timezoneName, timezone, policy, original.Id, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await ApplyRescheduleAsync(original, managerId, slot, request.Reason, now, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        var hydrated = await IncludeAppointmentGraph(database.Appointments.AsNoTracking())
            .SingleAsync(x => x.Id == original.Id, cancellationToken);
        var response = BuildResponse(hydrated, doctor.FullName, type.Name, timezoneName, timezone, null);
        StoreIdempotency(scope, idempotencyKey, requestHash, response, 200, now); await database.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync("appointment.rescheduled_by_manager", managerId, original.Id,
            new Dictionary<string, string> { ["rescheduleCount"] = response.RescheduleHistory.Count.ToString(CultureInfo.InvariantCulture) }, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return (response, false);
    }

    public async Task<AppointmentResponse> CancelForDoctorAsync(ulong doctorId, ulong appointmentId,
        AppointmentCancelRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var entity = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (entity is null || entity.ProfessionalAccountId != doctorId) throw NotFound("Agendamento não encontrado.");
        if (!MutableStatuses.Contains(entity.StatusCode)) throw Conflict("Somente agendamentos pendentes ou confirmados podem ser cancelados.");
        if (entity.RowVersion != request.RowVersion) throw Conflict("O agendamento foi alterado por outra sessão. Recarregue e tente novamente.");
        var previous = entity.StatusCode; var now = timeProvider.GetUtcNow().UtcDateTime;
        entity.StatusCode = "canceled"; entity.CancellationReason = request.Reason.Trim(); entity.CanceledByAccountId = doctorId;
        entity.CanceledAtUtc = now; entity.UpdatedAtUtc = now; entity.RowVersion++;
        AddHistory(entity, doctorId, previous, "canceled", entity.CancellationReason, now);
        await database.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync("appointment.canceled_by_doctor", doctorId, entity.Id, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var (_, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        var hydrated = await IncludeAppointmentGraph(database.Appointments.AsNoTracking()).SingleAsync(x => x.Id == entity.Id, cancellationToken);
        return await ToResponseAsync(hydrated, timezoneName, timezone, cancellationToken);
    }

    public async Task<(AppointmentResponse Response, bool Replayed)> RescheduleForDoctorAsync(ulong doctorId,
        ulong appointmentId, string idempotencyKey, AppointmentRescheduleRequest request, CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(idempotencyKey);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var original = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (original is null || original.ProfessionalAccountId != doctorId) throw NotFound("Agendamento não encontrado.");
        if (!MutableStatuses.Contains(original.StatusCode)) throw Conflict("Somente agendamentos pendentes ou confirmados podem ser reagendados.");
        if (original.RowVersion != request.RowVersion) throw Conflict("O agendamento foi alterado por outra sessão. Recarregue e tente novamente.");
        var requestHash = HashRequest(doctorId, $"doctor-reschedule:{appointmentId}", request);
        var scope = $"appointment.doctor-move:{doctorId}";
        var replay = await TryReplayAsync(scope, idempotencyKey, requestHash, cancellationToken);
        if (replay is not null) { await transaction.CommitAsync(cancellationToken); return (replay, true); }
        await LockDoctorAsync(doctorId, cancellationToken);
        var type = await RequireAppointmentTypeAsync(original.AppointmentTypeId, original.ModalityCode, cancellationToken);
        var doctor = await RequireActiveDoctorAsync(doctorId, cancellationToken);
        var (policy, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        var slot = await RequireSlotAsync(original.PatientAccountId, doctorId, type, original.ModalityCode,
            request.LocalDate, request.LocalStartsAt, timezoneName, timezone, policy, original.Id, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await ApplyRescheduleAsync(original, doctorId, slot, request.Reason, now, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        var hydrated = await IncludeAppointmentGraph(database.Appointments.AsNoTracking())
            .SingleAsync(x => x.Id == original.Id, cancellationToken);
        var response = BuildResponse(hydrated, doctor.FullName, type.Name, timezoneName, timezone, null);
        StoreIdempotency(scope, idempotencyKey, requestHash, response, StatusCodes.Status200OK, now);
        await database.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync("appointment.rescheduled_by_doctor", doctorId, original.Id,
            new Dictionary<string, string> { ["rescheduleCount"] = response.RescheduleHistory.Count.ToString(CultureInfo.InvariantCulture) }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (response, false);
    }

    public async Task<AppointmentResponse> CancelAsync(
        ulong patientId,
        ulong appointmentId,
        AppointmentCancelRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockPatientAsync(patientId, cancellationToken);
        var entity = await database.Appointments
            .FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (entity is null || entity.PatientAccountId != patientId)
        {
            throw NotFound("Agendamento não encontrado.");
        }

        if (!MutableStatuses.Contains(entity.StatusCode))
        {
            throw Conflict("Somente agendamentos pendentes ou confirmados podem ser cancelados.");
        }

        if (entity.RowVersion != request.RowVersion)
        {
            throw Conflict("O agendamento foi alterado por outra sessão. Recarregue e tente novamente.");
        }

        var (policy, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (entity.StartsAtUtc <= now.AddHours(policy.CancellationCutoffHours))
        {
            throw Conflict($"O cancelamento pelo paciente exige pelo menos {policy.CancellationCutoffHours} horas de antecedência.");
        }

        var previous = entity.StatusCode;
        entity.StatusCode = "canceled";
        entity.CancellationReason = request.Reason.Trim();
        entity.CanceledByAccountId = patientId;
        entity.CanceledAtUtc = now;
        entity.UpdatedAtUtc = now;
        entity.RowVersion++;
        AddHistory(entity, patientId, previous, "canceled", entity.CancellationReason, now);
        await database.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(
            "appointment.canceled_by_patient",
            patientId,
            entity.Id,
            null,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAppointmentAsync(patientId, entity.Id, cancellationToken);
    }

    public async Task<(AppointmentResponse Response, bool Replayed)> RescheduleAsync(
        ulong patientId,
        ulong appointmentId,
        string idempotencyKey,
        AppointmentRescheduleRequest request,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(idempotencyKey);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockPatientAsync(patientId, cancellationToken);
        var requestHash = HashRequest(patientId, $"reschedule:{appointmentId}", request);
        var scope = $"appointment.move:{patientId}";
        var replay = await TryReplayAsync(scope, idempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return (replay, true);
        }

        var original = await database.Appointments
            .FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (original is null || original.PatientAccountId != patientId)
        {
            throw NotFound("Agendamento não encontrado.");
        }

        if (!MutableStatuses.Contains(original.StatusCode))
        {
            throw Conflict("Somente agendamentos pendentes ou confirmados podem ser reagendados.");
        }

        if (original.RowVersion != request.RowVersion)
        {
            throw Conflict("O agendamento foi alterado por outra sessão. Recarregue e tente novamente.");
        }

        await LockDoctorAsync(original.ProfessionalAccountId, cancellationToken);
        var type = await RequireAppointmentTypeAsync(original.AppointmentTypeId, original.ModalityCode, cancellationToken);
        var doctor = await RequireActiveDoctorAsync(original.ProfessionalAccountId, cancellationToken);
        var (policy, timezoneName, timezone) = await LoadConfigurationAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (original.StartsAtUtc <= now.AddHours(policy.RescheduleCutoffHours))
        {
            throw Conflict($"O reagendamento pelo paciente exige pelo menos {policy.RescheduleCutoffHours} horas de antecedência.");
        }

        var slot = await RequireSlotAsync(
            patientId,
            original.ProfessionalAccountId,
            type,
            original.ModalityCode,
            request.LocalDate,
            request.LocalStartsAt,
            timezoneName,
            timezone,
            policy,
            original.Id,
            cancellationToken);
        await ApplyRescheduleAsync(original, patientId, slot, request.Reason, now, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        var hydrated = await IncludeAppointmentGraph(database.Appointments.AsNoTracking())
            .SingleAsync(x => x.Id == original.Id, cancellationToken);
        var response = BuildResponse(hydrated, doctor.FullName, type.Name, timezoneName, timezone, null);
        StoreIdempotency(
            scope,
            idempotencyKey,
            requestHash,
            response,
            StatusCodes.Status200OK,
            now);
        await database.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(
            "appointment.rescheduled_by_patient",
            patientId,
            original.Id,
            new Dictionary<string, string> { ["rescheduleCount"] = response.RescheduleHistory.Count.ToString(CultureInfo.InvariantCulture) },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (response, false);
    }

    private async Task ApplyRescheduleAsync(
        Appointment appointment,
        ulong actorAccountId,
        AvailableSlotResponse slot,
        string? reason,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var sequence = (await database.AppointmentRescheduleHistories
            .Where(item => item.AppointmentId == appointment.Id)
            .MaxAsync(item => (uint?)item.SequenceNumber, cancellationToken) ?? 0) + 1;
        database.AppointmentRescheduleHistories.Add(new AppointmentRescheduleHistory
        {
            AppointmentId = appointment.Id,
            SequenceNumber = sequence,
            ActorAccountId = actorAccountId,
            PreviousStartsAtUtc = appointment.StartsAtUtc,
            PreviousEndsAtUtc = appointment.EndsAtUtc,
            NewStartsAtUtc = slot.StartsAtUtc,
            NewEndsAtUtc = slot.EndsAtUtc,
            Reason = OptionalText(reason),
            OccurredAtUtc = occurredAtUtc
        });
        appointment.StartsAtUtc = slot.StartsAtUtc;
        appointment.EndsAtUtc = slot.EndsAtUtc;
        appointment.UpdatedAtUtc = occurredAtUtc;
        appointment.RowVersion++;
    }

    private async Task<AvailableSlotResponse> RequireSlotAsync(
        ulong patientId,
        ulong doctorId,
        AppointmentType type,
        string modality,
        DateOnly date,
        TimeOnly startsAt,
        string timezoneName,
        TimeZoneInfo timezone,
        SchedulingPolicy policy,
        ulong? excludedAppointmentId,
        CancellationToken cancellationToken)
    {
        var today = LocalDate(timeProvider.GetUtcNow(), timezone);
        if (date < today || date > today.AddDays(policy.BookingHorizonDays))
        {
            throw BadRequest("A data está fora do período permitido para agendamento.");
        }

        var slots = await BuildSlotsAsync(
            patientId,
            doctorId,
            type,
            modality,
            date,
            date,
            timezoneName,
            timezone,
            policy,
            excludedAppointmentId,
            cancellationToken);
        var limitJson = await database.ApplicationSettings.Where(x => x.SettingKey == "appointments.patient_daily_limit").Select(x => x.ValueJson).SingleOrDefaultAsync(cancellationToken);
        var dailyLimit = int.TryParse(limitJson, out var configuredLimit) && configuredLimit is > 0 and <= 50 ? configuredLimit : 3;
        var dayStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), timezone);
        var dayEnd = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), timezone);
        if (await database.Appointments.CountAsync(x => x.PatientAccountId == patientId && x.Id != excludedAppointmentId && ActiveStatuses.Contains(x.StatusCode) && x.StartsAtUtc >= dayStart && x.StartsAtUtc < dayEnd, cancellationToken) >= dailyLimit)
            throw Conflict("Você atingiu o limite diário de agendamentos. Escolha outra data.");
        return slots.SingleOrDefault(item => item.StartsAt == startsAt)
            ?? throw Conflict("O horário não está mais disponível. Atualize a agenda e escolha outro.");
    }

    private async Task<IReadOnlyList<AvailableSlotResponse>> BuildSlotsAsync(
        ulong patientId,
        ulong doctorId,
        AppointmentType type,
        string modality,
        DateOnly from,
        DateOnly until,
        string timezoneName,
        TimeZoneInfo timezone,
        SchedulingPolicy policy,
        ulong? excludedAppointmentId,
        CancellationToken cancellationToken)
    {
        if (!await database.ProfessionalServices.AsNoTracking().AnyAsync(x => x.ProfessionalAccountId == doctorId
            && x.AppointmentTypeId == type.Id && x.IsActive && x.AppointmentType.IsActive, cancellationToken))
            return [];
        var preferences = await database.ProfessionalPreferences.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProfessionalAccountId == doctorId, cancellationToken);
        if (modality == "online" && preferences is { OnlineEnabled: false }) return [];
        var fromLocal = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var untilLocal = DateTime.SpecifyKind(until.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(fromLocal, timezone);
        var untilUtc = TimeZoneInfo.ConvertTimeToUtc(untilLocal, timezone);
        var busy = await database.Appointments.AsNoTracking()
            .Where(item => ActiveStatuses.Contains(item.StatusCode)
                && item.Id != excludedAppointmentId
                && (item.ProfessionalAccountId == doctorId || item.PatientAccountId == patientId)
                && item.StartsAtUtc < untilUtc
                && item.EndsAtUtc > fromUtc)
            .Select(item => new BusyPeriod(item.StartsAtUtc, item.EndsAtUtc))
            .ToListAsync(cancellationToken);
        var doctorHours = await database.ProfessionalWeeklyHours.AsNoTracking()
            .Where(item => item.ProfessionalAccountId == doctorId && item.IsActive)
            .ToListAsync(cancellationToken);
        var clinicHours = modality == "in_person"
            ? await database.ClinicWeeklyHours.AsNoTracking().Where(item => item.IsActive).ToListAsync(cancellationToken)
            : [];
        var holidayEntities = await database.Holidays.AsNoTracking()
            .Where(item => item.IsAnnual || item.HolidayDate >= from.ToDateTime(TimeOnly.MinValue)
                && item.HolidayDate <= until.ToDateTime(TimeOnly.MinValue))
            .ToListAsync(cancellationToken);
        var exceptions = await database.ProfessionalAvailabilityExceptions.AsNoTracking()
            .Where(x => x.ProfessionalAccountId == doctorId && x.ExceptionDate >= from.ToDateTime(TimeOnly.MinValue)
                && x.ExceptionDate <= until.ToDateTime(TimeOnly.MinValue)
                && (x.ModalityCode == modality || x.ModalityCode == "both"))
            .ToListAsync(cancellationToken);
        var earliest = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(policy.MinimumLeadMinutes);
        var limitJson = await database.ApplicationSettings.Where(x => x.SettingKey == "appointments.patient_daily_limit").Select(x => x.ValueJson).SingleOrDefaultAsync(cancellationToken);
        var dailyLimit = int.TryParse(limitJson, out var configuredLimit) && configuredLimit is > 0 and <= 50 ? configuredLimit : 3;
        var patientDates = (await database.Appointments.AsNoTracking().Where(x => x.PatientAccountId == patientId && x.Id != excludedAppointmentId
            && ActiveStatuses.Contains(x.StatusCode) && x.StartsAtUtc >= fromUtc && x.StartsAtUtc < untilUtc)
            .Select(x => x.StartsAtUtc).ToArrayAsync(cancellationToken))
            .GroupBy(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x, DateTimeKind.Utc), timezone)))
            .Where(x => x.Count() >= dailyLimit).Select(x => x.Key).ToHashSet();
        var modalityLimit = modality == "online" ? preferences?.MaxOnlineDaily ?? 8 : preferences?.MaxInPersonDaily ?? 16;
        if (modalityLimit == 0) return [];
        var doctorFullDates = (await database.Appointments.AsNoTracking().Where(x => x.ProfessionalAccountId == doctorId
            && x.Id != excludedAppointmentId && x.ModalityCode == modality && ActiveStatuses.Contains(x.StatusCode)
            && x.StartsAtUtc >= fromUtc && x.StartsAtUtc < untilUtc).Select(x => x.StartsAtUtc).ToArrayAsync(cancellationToken))
            .GroupBy(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x, DateTimeKind.Utc), timezone)))
            .Where(x => modalityLimit == 0 || x.Count() >= modalityLimit).Select(x => x.Key).ToHashSet();
        var result = new List<AvailableSlotResponse>();
        for (var date = from; date <= until; date = date.AddDays(1))
        {
            if (patientDates.Contains(date) || doctorFullDates.Contains(date)) continue;
            var dateValue = date.ToDateTime(TimeOnly.MinValue);
            var day = (byte)date.DayOfWeek;
            var dateExceptions = exceptions.Where(x => x.ExceptionDate.Date == dateValue.Date).ToArray();
            if (dateExceptions.Any(x => !x.IsAvailable)) continue;
            var weeklyWindows = doctorHours
                .Where(item => item.DayOfWeek == day
                    && (item.ModalityCode == modality || item.ModalityCode == "both")
                    && (!item.ValidFrom.HasValue || item.ValidFrom.Value.Date <= dateValue)
                    && (!item.ValidUntil.HasValue || item.ValidUntil.Value.Date >= dateValue))
                .Select(item => new LocalAvailabilityWindow(item.StartTime, item.EndTime)).ToArray();
            var overrideWindows = dateExceptions.Where(x => x.IsAvailable)
                .Select(x => new LocalAvailabilityWindow(x.StartTime!.Value, x.EndTime!.Value)).ToArray();
            var doctorWindows = overrideWindows.Length > 0 ? overrideWindows : weeklyWindows;
            var clinicWindows = clinicHours
                .Where(item => item.DayOfWeek == day)
                .Select(item => new LocalAvailabilityWindow(item.StartTime, item.EndTime));
            var holidays = holidayEntities.Where(item => item.HolidayDate.Date == dateValue.Date
                || item.IsAnnual && item.HolidayDate.Month == dateValue.Month && item.HolidayDate.Day == dateValue.Day).ToArray();
            var holidayBlocks = holidays.Select(item => item.StartTime.HasValue
                    ? new LocalAvailabilityWindow(item.StartTime.Value, item.EndTime!.Value)
                    : new LocalAvailabilityWindow(TimeSpan.Zero, TimeSpan.FromDays(1)))
                .ToArray();
            result.AddRange(PatientSchedulingPolicy.BuildDaySlots(
                date,
                type.DurationMinutes,
                policy.SlotIntervalMinutes,
                timezoneName,
                timezone,
                doctorWindows,
                clinicWindows,
                holidayBlocks,
                busy,
                earliest,
                modality == "in_person"));
        }

        return result;
    }

    private async Task<(SchedulingPolicy Policy, string TimezoneName, TimeZoneInfo Timezone)> LoadConfigurationAsync(
        CancellationToken cancellationToken)
    {
        var values = await database.ApplicationSettings.AsNoTracking()
            .Where(item => item.SettingKey.StartsWith("appointments.") || item.SettingKey == "system.timezone")
            .ToDictionaryAsync(item => item.SettingKey, item => item.ValueJson, cancellationToken);
        var clinicTimezone = await database.Clinics.AsNoTracking()
            .Select(item => item.TimezoneName)
            .SingleOrDefaultAsync(cancellationToken);
        var timezoneName = !string.IsNullOrWhiteSpace(clinicTimezone)
            ? clinicTimezone
            : ReadString(values, "system.timezone", "America/Sao_Paulo");
        TimeZoneInfo timezone;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneName);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new SchedulingRuleException(StatusCodes.Status503ServiceUnavailable, "O fuso horário da clínica não está disponível no servidor.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new SchedulingRuleException(StatusCodes.Status503ServiceUnavailable, "O fuso horário da clínica é inválido.");
        }

        return (new SchedulingPolicy(
            ReadInt(values, "appointments.booking_horizon_days", 365, 1, 730),
            ReadInt(values, "appointments.minimum_lead_minutes", 120, 0, 10080),
            ReadInt(values, "appointments.cancellation_cutoff_hours", 24, 0, 720),
            ReadInt(values, "appointments.reschedule_cutoff_hours", 24, 0, 720),
            ReadInt(values, "appointments.slot_interval_minutes", 10, 5, 120)), timezoneName, timezone);
    }

    private async Task<AppointmentType> RequireAppointmentTypeAsync(uint id, string modality, CancellationToken cancellationToken)
    {
        var type = await database.AppointmentTypes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken)
            ?? throw NotFound("Tipo de atendimento não encontrado.");
        if (!SupportsModality(type.ModalityCode, modality))
        {
            throw Conflict("O tipo de atendimento não oferece a modalidade selecionada.");
        }

        return type;
    }

    private async Task<Account> RequireActiveDoctorAsync(ulong doctorId, CancellationToken cancellationToken) =>
        await database.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == doctorId
                && (item.RoleCode == ViverAppRoles.Doctor || item.RoleCode == ViverAppRoles.Psychologist)
                && item.StatusCode == "active"
                && item.ProfessionalProfile != null, cancellationToken)
        ?? throw NotFound("Profissional não encontrado.");

    private async Task LockPatientAsync(ulong patientId, CancellationToken cancellationToken)
    {
        var patient = await database.Accounts
            .FromSqlInterpolated($"SELECT * FROM accounts WHERE id = {patientId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (patient is null || patient.RoleCode != ViverAppRoles.Patient || patient.StatusCode != "active")
        {
            throw new SchedulingRuleException(StatusCodes.Status403Forbidden, "A conta não pode realizar agendamentos.");
        }
    }

    private async Task LockDoctorAsync(ulong doctorId, CancellationToken cancellationToken)
    {
        var doctor = await database.ProfessionalProfiles
            .FromSqlInterpolated($"SELECT * FROM professional_profiles WHERE account_id = {doctorId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (doctor is null)
        {
            throw NotFound("Profissional não encontrado.");
        }
    }

    private async Task<AppointmentResponse?> TryReplayAsync(
        string scope,
        string key,
        byte[] hash,
        CancellationToken cancellationToken)
    {
        var existing = await database.IdempotencyRecords.SingleOrDefaultAsync(
            item => item.ScopeCode == scope && item.IdempotencyKey == key,
            cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(existing.RequestHash, hash))
        {
            throw Conflict("A chave de idempotência já foi usada com outro conteúdo.");
        }

        if (existing.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime || existing.ResponseBodyJson is null)
        {
            throw Conflict("A chave de idempotência não pode mais ser reutilizada.");
        }

        return JsonSerializer.Deserialize<AppointmentResponse>(existing.ResponseBodyJson)
            ?? throw Conflict("A resposta idempotente armazenada é inválida.");
    }

    private void StoreIdempotency(
        string scope,
        string key,
        byte[] hash,
        AppointmentResponse response,
        int responseStatusCode,
        DateTime now)
    {
        database.IdempotencyRecords.Add(new IdempotencyRecord
        {
            ScopeCode = scope,
            IdempotencyKey = key,
            RequestHash = hash,
            ResponseStatusCode = checked((ushort)responseStatusCode),
            ResponseBodyJson = JsonSerializer.Serialize(response),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(24),
        });
    }

    private void AddHistory(
        Appointment appointment,
        ulong actorId,
        string? fromStatus,
        string toStatus,
        string? reason,
        DateTime occurredAtUtc)
    {
        database.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            Appointment = appointment,
            ActorAccountId = actorId,
            FromStatusCode = fromStatus,
            ToStatusCode = toStatus,
            Reason = reason,
            StartsAtUtc = appointment.StartsAtUtc,
            EndsAtUtc = appointment.EndsAtUtc,
            OccurredAtUtc = occurredAtUtc,
        });
    }

    private async Task<AppointmentResponse> ToResponseAsync(
        Appointment entity,
        string timezoneName,
        TimeZoneInfo timezone,
        CancellationToken cancellationToken)
    {
        var successorId = entity.InverseRescheduledFromAppointment?.Id
            ?? await database.Appointments.AsNoTracking()
                .Where(item => item.RescheduledFromAppointmentId == entity.Id)
                .Select(item => (ulong?)item.Id)
                .SingleOrDefaultAsync(cancellationToken);
        return BuildResponse(
            entity,
            entity.ProfessionalAccount.Account.FullName,
            entity.AppointmentType.Name,
            timezoneName,
            timezone,
            successorId);
    }

    private async Task<ulong> AllocateAppointmentNumberAsync(CancellationToken cancellationToken)
    {
        var sequence = await database.AppointmentNumberSequences
            .FromSqlRaw("SELECT * FROM appointment_number_sequence WHERE sequence_key = 1 FOR UPDATE")
            .SingleAsync(cancellationToken);
        var number = sequence.NextValue;
        sequence.NextValue++;
        return number;
    }

    private static AppointmentResponse BuildResponse(
        Appointment entity,
        string doctorName,
        string appointmentTypeName,
        string timezoneName,
        TimeZoneInfo timezone,
        ulong? successorId)
    {
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(entity.StartsAtUtc, DateTimeKind.Utc), timezone);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(entity.EndsAtUtc, DateTimeKind.Utc), timezone);
        return new AppointmentResponse(
            entity.Id,
            entity.AppointmentNumber,
            entity.ProfessionalAccountId,
            doctorName,
            entity.AppointmentTypeId,
            appointmentTypeName,
            entity.StatusCode,
            entity.ModalityCode,
            DateOnly.FromDateTime(localStart),
            TimeOnly.FromDateTime(localStart),
            TimeOnly.FromDateTime(localEnd),
            DateTime.SpecifyKind(entity.StartsAtUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(entity.EndsAtUtc, DateTimeKind.Utc),
            timezoneName,
            entity.PriceAmount,
            entity.CurrencyCode,
            entity.PatientNotes,
            entity.CancellationReason,
            entity.RescheduledFromAppointmentId,
            successorId,
            entity.ArrivedAtUtc,
            entity.ArrivalBusinessDate is { } arrivalDate ? DateOnly.FromDateTime(arrivalDate) : null,
            entity.ArrivalQueueNumber,
            entity.AppointmentRescheduleHistories
                .OrderBy(item => item.SequenceNumber)
                .Select(item => new AppointmentRescheduleHistoryResponse(
                    item.SequenceNumber,
                    DateTime.SpecifyKind(item.PreviousStartsAtUtc, DateTimeKind.Utc),
                    DateTime.SpecifyKind(item.PreviousEndsAtUtc, DateTimeKind.Utc),
                    DateTime.SpecifyKind(item.NewStartsAtUtc, DateTimeKind.Utc),
                    DateTime.SpecifyKind(item.NewEndsAtUtc, DateTimeKind.Utc),
                    item.Reason,
                    DateTime.SpecifyKind(item.OccurredAtUtc, DateTimeKind.Utc)))
                .ToArray(),
            entity.RequiresPayment,
            entity.RowVersion);
    }

    private static IQueryable<Appointment> IncludeAppointmentGraph(IQueryable<Appointment> query) => query
        .Include(item => item.AppointmentType)
        .Include(item => item.ProfessionalAccount)
            .ThenInclude(profile => profile.Account)
        .Include(item => item.InverseRescheduledFromAppointment)
        .Include(item => item.AppointmentRescheduleHistories);

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback, int minimum, int maximum)
    {
        if (!values.TryGetValue(key, out var json))
        {
            return fallback;
        }

        try
        {
            var value = JsonSerializer.Deserialize<int>(json);
            return value >= minimum && value <= maximum ? value : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static string ReadString(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        if (!values.TryGetValue(key, out var json))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(json) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static byte[] HashRequest<T>(ulong actorId, string operation, T request)
    {
        var payload = JsonSerializer.Serialize(new { actorId, operation, request });
        return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    }

    private static DateOnly LocalDate(DateTimeOffset utcNow, TimeZoneInfo timezone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timezone).DateTime);

    private static string? OptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool SupportsModality(string configured, string requested) =>
        configured == "both" || configured == requested;

    private static void ValidatePagination(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 50)
        {
            throw BadRequest("A paginação é inválida.");
        }
    }

    private static void ValidateModality(string? modality, bool allowNull)
    {
        if ((!allowNull || modality is not null) && modality is not ("in_person" or "online"))
        {
            throw BadRequest("A modalidade deve ser in_person ou online.");
        }
    }

    private static void ValidateIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || key.Length is < 16 or > 100
            || key.Any(character => character > 127 || char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw BadRequest("O cabeçalho Idempotency-Key deve conter de 16 a 100 caracteres ASCII sem espaços.");
        }
    }

    private static SchedulingRuleException BadRequest(string title) =>
        new(StatusCodes.Status400BadRequest, title);

    private static SchedulingRuleException NotFound(string title) =>
        new(StatusCodes.Status404NotFound, title);

    private static SchedulingRuleException Conflict(string title) =>
        new(StatusCodes.Status409Conflict, title);
}
