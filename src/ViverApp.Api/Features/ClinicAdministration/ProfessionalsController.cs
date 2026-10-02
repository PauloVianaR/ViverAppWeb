using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;
using ViverApp.Api.Features.AdministratorExperience;

namespace ViverApp.Api.Features.ClinicAdministration;

[ApiController]
[Route("api/v1/professionals")]
[Authorize]
[EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
[ServiceFilter(typeof(AdministratorStepUpFilter))]
public sealed class ProfessionalsController(
    ViverAppDbContext database,
    UserManager<ViverAppUser> userManager,
    IdentityChallengeService challengeService,
    IdentityNotificationService notificationService,
    IdentityAuditWriter auditWriter) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = ViverAppPolicies.Management)]
    public async Task<ActionResult<PagedResponse<ProfessionalResponse>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateFilters(page, pageSize, search, role, status))
        {
            return ValidationProblem(ModelState);
        }

        var query = ProfessionalQuery();
        var term = ClinicAdministrationSupport.OptionalText(search);
        if (term is not null)
        {
            query = query.WhereNameEmailOrPhoneContains(term);
        }

        if (role is not null)
        {
            query = query.Where(item => item.RoleCode == role);
        }

        if (status is not null)
        {
            query = query.Where(item => item.StatusCode == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var accounts = await query.OrderBy(item => item.FullName)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(new PagedResponse<ProfessionalResponse>(
            accounts.Select(ToResponse).ToArray(),
            page,
            pageSize,
            total));
    }

    [HttpGet("{accountId:long}")]
    public async Task<ActionResult<ProfessionalResponse>> GetById(
        ulong accountId,
        CancellationToken cancellationToken)
    {
        var account = await ProfessionalQuery().SingleOrDefaultAsync(item => item.Id == accountId, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        return CanAccessProfessional(account) ? Ok(ToResponse(account)) : Forbid();
    }

    [HttpPost]
    [Authorize(Policy = ViverAppPolicies.Administrator)]
    public async Task<ActionResult<ProfessionalResponse>> Create(
        [FromBody] ProfessionalCreateRequest request,
        CancellationToken cancellationToken)
    {
        var email = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : IdentifierNormalizer.NormalizeEmail(request.Email);
        var phone = string.IsNullOrWhiteSpace(request.PhoneE164)
            ? null
            : IdentifierNormalizer.NormalizePhone(request.PhoneE164);
        if ((request.Email is not null && email is null)
            || (request.PhoneE164 is not null && phone is null)
            || email is null && phone is null)
        {
            ModelState.AddModelError("contact", "Informe um e-mail válido e/ou telefone brasileiro no formato E.164.");
        }

        _ = ValidateProfessionalFields(request);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var specialties = request.RoleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist
            ? await LoadSpecialties(request.SpecialtyIds, request.PrimarySpecialtyId, cancellationToken)
            : [];
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = new ViverAppUser
        {
            UserName = email ?? phone,
            NormalizedUserName = email ?? phone,
            RoleCode = request.RoleCode,
            StatusCode = "pending_confirmation",
            FullName = request.FullName.Trim(),
            Email = request.Email?.Trim(),
            NormalizedEmail = email,
            PhoneNumber = phone,
        };
        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ConflictProblem("Não foi possível criar o profissional.");
        }

        if (request.RoleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist)
        {
            var now = DateTime.UtcNow;
            var profile = new ProfessionalProfile
            {
                AccountId = user.Id,
                LicenseTypeCode = request.RoleCode == ViverAppRoles.Psychologist ? "CRP" : "CRM",
                ProfessionalTitle = request.RoleCode == ViverAppRoles.Psychologist ? "Psic." : "Dr.",
                LicenseStateCode = request.LicenseStateCode!.Trim().ToUpperInvariant(),
                LicenseNumber = request.LicenseNumber!.Trim().ToUpperInvariant(),
                Biography = ClinicAdministrationSupport.OptionalText(request.Biography),
                YearsExperience = 0,
                DefaultAppointmentDurationMinutes = request.DefaultAppointmentDurationMinutes!.Value,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = 1,
            };
            database.ProfessionalProfiles.Add(profile);
            AddSpecialtyLinks(user.Id, specialties, request.PrimarySpecialtyId);
            database.ProfessionalPreferences.Add(new ProfessionalPreference
            {
                ProfessionalAccountId = user.Id,
                EmailEnabled = true,
                SmsEnabled = true,
                OnlineEnabled = true,
                MaxOnlineDaily = 8,
                MaxInPersonDaily = 16,
                UpdatedAtUtc = now,
                RowVersion = 1,
            });
            var activeServices = await database.AppointmentTypes.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).ToArrayAsync(cancellationToken);
            database.ProfessionalServices.AddRange(activeServices.Select(id => new ProfessionalService
            {
                ProfessionalAccountId = user.Id,
                AppointmentTypeId = id,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = 1,
            }));
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ConflictProblem("CRM ou especialidades inválidos.");
            }
        }

        var channel = email is not null ? "email" : "sms";
        var requestId = await challengeService.CreateAsync(
            user,
            "contact_verification",
            channel,
            email ?? phone!,
            cancellationToken);
        await auditWriter.WriteAsync(
            "professional.created",
            ActorId,
            "account",
            user.Id.ToString(),
            new Dictionary<string, string>
            {
                ["role"] = request.RoleCode,
                ["verificationChannel"] = channel,
                ["verificationRequestId"] = requestId.ToString(),
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var account = await ProfessionalQuery().SingleAsync(item => item.Id == user.Id, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { accountId = user.Id }, ToResponse(account));
    }

    [HttpPut("{accountId:long}")]
    public async Task<ActionResult<ProfessionalResponse>> Update(
        ulong accountId,
        [FromBody] ProfessionalUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var account = await database.Accounts
            .Include(item => item.ProfessionalProfile)
            .ThenInclude(profile => profile!.ProfessionalSpecialties)
            .SingleOrDefaultAsync(
                item => item.Id == accountId
                    && (item.RoleCode == ViverAppRoles.Doctor || item.RoleCode == ViverAppRoles.Psychologist || item.RoleCode == ViverAppRoles.Manager),
                cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (!CanAccessProfessional(account))
        {
            return Forbid();
        }

        IReadOnlyList<Specialty> specialties = [];
        if (account.RoleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist)
        {
            if (account.ProfessionalProfile is null
                || !request.ProfileRowVersion.HasValue
                || !request.DefaultAppointmentDurationMinutes.HasValue)
            {
                ModelState.AddModelError("profile", "Os dados profissionais e suas versões são obrigatórios.");
                return ValidationProblem(ModelState);
            }

            specialties = await LoadSpecialties(request.SpecialtyIds, request.PrimarySpecialtyId, cancellationToken);
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }
        }
        else if (request.ProfileRowVersion.HasValue
            || request.SpecialtyIds is { Count: > 0 }
            || request.PrimarySpecialtyId.HasValue
            || request.DefaultAppointmentDurationMinutes.HasValue
            || request.Biography is not null)
        {
            ModelState.AddModelError("profile", "Gestores não possuem perfil médico.");
            return ValidationProblem(ModelState);
        }

        ClinicAdministrationSupport.SetConcurrency(database, account, nameof(Account.RowVersion), request.AccountRowVersion);
        account.FullName = request.FullName.Trim();
        account.UpdatedAtUtc = DateTime.UtcNow;

        if (account.ProfessionalProfile is not null)
        {
            ClinicAdministrationSupport.SetConcurrency(
                database,
                account.ProfessionalProfile,
                nameof(ProfessionalProfile.RowVersion),
                request.ProfileRowVersion!.Value);
            account.ProfessionalProfile.Biography = ClinicAdministrationSupport.OptionalText(request.Biography);
            account.ProfessionalProfile.DefaultAppointmentDurationMinutes = request.DefaultAppointmentDurationMinutes!.Value;
            account.ProfessionalProfile.UpdatedAtUtc = DateTime.UtcNow;
            database.ProfessionalSpecialties.RemoveRange(account.ProfessionalProfile.ProfessionalSpecialties);
            AddSpecialtyLinks(accountId, specialties, request.PrimarySpecialtyId);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }
        catch (DbUpdateException)
        {
            return ConflictProblem("Não foi possível atualizar o profissional.");
        }

        await Audit("professional.updated", "account", accountId.ToString(), cancellationToken);
        var updated = await ProfessionalQuery().SingleAsync(item => item.Id == accountId, cancellationToken);
        return Ok(ToResponse(updated));
    }

    [HttpPost("{accountId:long}/review")]
    [Authorize(Policy = ViverAppPolicies.Administrator)]
    public async Task<ActionResult<ProfessionalResponse>> Review(
        ulong accountId,
        [FromBody] ProfessionalReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DecisionCode is "rejected" or "blocked"
            && (request.Reason?.Trim().Length ?? 0) < 5)
        {
            ModelState.AddModelError(nameof(request.Reason), "A justificativa deve ter pelo menos cinco caracteres.");
            return ValidationProblem(ModelState);
        }

        var account = await database.Accounts.SingleOrDefaultAsync(
            item => item.Id == accountId
                    && (item.RoleCode == ViverAppRoles.Doctor || item.RoleCode == ViverAppRoles.Psychologist || item.RoleCode == ViverAppRoles.Manager),
            cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (request.DecisionCode is "approved" or "reactivated"
            && !account.EmailVerified
            && !account.PhoneVerified)
        {
            return ConflictProblem("O profissional precisa confirmar ao menos um contato antes da aprovação.");
        }

        var transitionAllowed = request.DecisionCode switch
        {
            "approved" or "rejected" => account.StatusCode == "pending_approval",
            "blocked" => account.StatusCode == "active",
            "reactivated" => account.StatusCode is "blocked" or "rejected",
            _ => false,
        };
        if (!transitionAllowed)
        {
            return ConflictProblem("A mudança de status não é válida para o estado atual do profissional.");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var previousStatus = account.StatusCode;
        var newStatus = request.DecisionCode switch
        {
            "approved" or "reactivated" => "active",
            "rejected" => "rejected",
            "blocked" => "blocked",
            _ => throw new InvalidOperationException("professional_decision_invalid"),
        };
        ClinicAdministrationSupport.SetConcurrency(database, account, nameof(Account.RowVersion), request.AccountRowVersion);
        account.StatusCode = newStatus;
        account.UpdatedAtUtc = DateTime.UtcNow;
        if (newStatus is "rejected" or "blocked")
        {
            account.SecurityStamp = RandomNumberGenerator.GetBytes(32);
            await database.AuthSessions
                .Where(item => item.AccountId == accountId && item.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.RevokedAtUtc, DateTime.UtcNow)
                        .SetProperty(item => item.RevokeReasonCode, "professional_status"),
                    cancellationToken);
        }

        database.ProfessionalReviews.Add(new ProfessionalReview
        {
            ProfessionalAccountId = accountId,
            ReviewerAccountId = ActorId,
            DecisionCode = request.DecisionCode,
            Reason = ClinicAdministrationSupport.OptionalText(request.Reason),
            OccurredAtUtc = DateTime.UtcNow,
        });
        notificationService.QueueProfessionalReview(account, request.DecisionCode);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }

        await auditWriter.WriteAsync(
        "professional.reviewed",
        ActorId,
        "account",
        accountId.ToString(),
        new Dictionary<string, string>
        {
            ["decision"] = request.DecisionCode,
            ["previousStatus"] = previousStatus,
            ["newStatus"] = newStatus,
        },
        cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var updated = await ProfessionalQuery().SingleAsync(item => item.Id == accountId, cancellationToken);
        return Ok(ToResponse(updated));
    }

    [HttpGet("{accountId:long}/weekly-hours")]
    [ManagerFeatureGate("manager.professional_schedules_enabled")]
    public async Task<ActionResult<IReadOnlyList<ProfessionalWeeklyHourResponse>>> GetWeeklyHours(
        ulong accountId,
        CancellationToken cancellationToken)
    {
        if (!CanAccessProfessional(accountId))
        {
            return Forbid();
        }

        var items = await database.ProfessionalWeeklyHours.AsNoTracking()
            .Where(item => item.ProfessionalAccountId == accountId)
            .OrderBy(item => item.DayOfWeek)
            .ThenBy(item => item.StartTime)
            .ToListAsync(cancellationToken);
        return Ok(items.Select(ToResponse).ToArray());
    }

    [HttpPost("{accountId:long}/weekly-hours")]
    [ManagerFeatureGate("manager.professional_schedules_enabled")]
    public async Task<ActionResult<ProfessionalWeeklyHourResponse>> CreateWeeklyHour(
        ulong accountId,
        [FromBody] ProfessionalWeeklyHourWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanAccessProfessional(accountId))
        {
            return Forbid();
        }

        if (request.RowVersion != 0 || !ValidateDoctorHour(request))
        {
            return ValidationProblem(ModelState);
        }

        if (!await database.ProfessionalProfiles.AsNoTracking().AnyAsync(item => item.AccountId == accountId, cancellationToken))
        {
            return NotFound();
        }

        if (request.IsActive && await HasDoctorOverlap(accountId, request, null, cancellationToken))
        {
            return ConflictProblem("O horário conflita com outro período ativo do médico.");
        }

        var now = DateTime.UtcNow;
        var entity = new ProfessionalWeeklyHour
        {
            ProfessionalAccountId = accountId,
            DayOfWeek = request.DayOfWeek,
            ModalityCode = request.ModalityCode,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            ValidFrom = request.ValidFrom?.ToDateTime(TimeOnly.MinValue),
            ValidUntil = request.ValidUntil?.ToDateTime(TimeOnly.MinValue),
            IsActive = request.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.ProfessionalWeeklyHours.Add(entity);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return ConflictProblem("Não foi possível cadastrar o horário.");
        }

        await Audit("professional.weekly_hour.created", "professional_weekly_hour", entity.Id.ToString(), cancellationToken);
        return CreatedAtAction(nameof(GetWeeklyHours), new { accountId }, ToResponse(entity));
    }

    [HttpPut("{accountId:long}/weekly-hours/{id:long}")]
    [ManagerFeatureGate("manager.professional_schedules_enabled")]
    public async Task<ActionResult<ProfessionalWeeklyHourResponse>> UpdateWeeklyHour(
        ulong accountId,
        ulong id,
        [FromBody] ProfessionalWeeklyHourWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanAccessProfessional(accountId))
        {
            return Forbid();
        }

        if (request.RowVersion == 0 || !ValidateDoctorHour(request))
        {
            return ValidationProblem(ModelState);
        }

        var entity = await database.ProfessionalWeeklyHours.SingleOrDefaultAsync(
            item => item.Id == id && item.ProfessionalAccountId == accountId,
            cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (request.IsActive && await HasDoctorOverlap(accountId, request, id, cancellationToken))
        {
            return ConflictProblem("O horário conflita com outro período ativo do médico.");
        }
        if (await WouldUncoverAppointments(accountId, id, request, cancellationToken))
        {
            return ConflictProblem("A alteração deixaria atendimentos futuros sem disponibilidade. Reagende-os antes de editar esta faixa.");
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(ProfessionalWeeklyHour.RowVersion), request.RowVersion);
        entity.DayOfWeek = request.DayOfWeek;
        entity.ModalityCode = request.ModalityCode;
        entity.StartTime = request.StartTime;
        entity.EndTime = request.EndTime;
        entity.ValidFrom = request.ValidFrom?.ToDateTime(TimeOnly.MinValue);
        entity.ValidUntil = request.ValidUntil?.ToDateTime(TimeOnly.MinValue);
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }
        catch (DbUpdateException)
        {
            return ConflictProblem("Não foi possível atualizar o horário.");
        }

        await Audit("professional.weekly_hour.updated", "professional_weekly_hour", id.ToString(), cancellationToken);
        return Ok(ToResponse(entity));
    }

    [HttpDelete("{accountId:long}/weekly-hours/{id:long}")]
    [ManagerFeatureGate("manager.professional_schedules_enabled")]
    public async Task<IActionResult> DeleteWeeklyHour(
        ulong accountId,
        ulong id,
        [FromQuery] ulong rowVersion,
        CancellationToken cancellationToken)
    {
        if (!CanAccessProfessional(accountId))
        {
            return Forbid();
        }

        var entity = await database.ProfessionalWeeklyHours.SingleOrDefaultAsync(
            item => item.Id == id && item.ProfessionalAccountId == accountId,
            cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (await WouldUncoverAppointments(accountId, id, null, cancellationToken))
        {
            return ConflictProblem("Esta faixa contém atendimentos futuros. Reagende-os antes de removê-la.");
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(ProfessionalWeeklyHour.RowVersion), rowVersion);
        database.ProfessionalWeeklyHours.Remove(entity);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }

        await Audit("professional.weekly_hour.deleted", "professional_weekly_hour", id.ToString(), cancellationToken);
        return NoContent();
    }

    private ulong ActorId => ClinicAdministrationSupport.RequireActorId(User);

    private IQueryable<Account> ProfessionalQuery() => database.Accounts.AsNoTracking()
        .Where(item => item.RoleCode == ViverAppRoles.Doctor || item.RoleCode == ViverAppRoles.Psychologist || item.RoleCode == ViverAppRoles.Manager)
        .Include(item => item.ProfessionalProfile)
        .ThenInclude(profile => profile!.ProfessionalSpecialties)
        .ThenInclude(link => link.Specialty);

    private bool CanAccessProfessional(ulong accountId) =>
        User.IsInRole(ViverAppRoles.Manager)
        || User.IsInRole(ViverAppRoles.Administrator)
        || (User.IsInRole(ViverAppRoles.Doctor) || User.IsInRole(ViverAppRoles.Psychologist)) && ActorId == accountId;

    private bool CanAccessProfessional(Account account) =>
        User.IsInRole(ViverAppRoles.Administrator)
        || ActorId == account.Id
        || User.IsInRole(ViverAppRoles.Manager) && account.RoleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist;

    private bool ValidateFilters(int page, int pageSize, string? search, string? role, string? status)
    {
        var valid = page >= 1
            && pageSize is >= 1 and <= 100
            && (search?.Length ?? 0) <= 200
            && (role is null || role is ViverAppRoles.Doctor or ViverAppRoles.Psychologist or ViverAppRoles.Manager)
            && (status is null || status is "pending_confirmation" or "pending_approval" or "active" or "rejected" or "blocked");
        if (!valid)
        {
            ModelState.AddModelError("filters", "Filtros ou paginação inválidos.");
        }

        return valid;
    }

    private bool ValidateProfessionalFields(ProfessionalCreateRequest request)
    {
        if (request.RoleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist)
        {
            var valid = !string.IsNullOrWhiteSpace(request.LicenseStateCode)
                && !string.IsNullOrWhiteSpace(request.LicenseNumber)
                && request.DefaultAppointmentDurationMinutes.HasValue;
            if (!valid)
            {
                ModelState.AddModelError("profile", "Registro profissional, região e duração padrão são obrigatórios.");
            }

            return valid;
        }

        var hasDoctorData = request.LicenseStateCode is not null
            || request.LicenseNumber is not null
            || request.Biography is not null
            || request.DefaultAppointmentDurationMinutes.HasValue
            || request.SpecialtyIds is { Count: > 0 }
            || request.PrimarySpecialtyId.HasValue;
        if (hasDoctorData)
        {
            ModelState.AddModelError("profile", "Gestores não possuem perfil médico.");
            return false;
        }

        return true;
    }

    private async Task<IReadOnlyList<Specialty>> LoadSpecialties(
        IReadOnlyList<uint>? specialtyIds,
        uint? primarySpecialtyId,
        CancellationToken cancellationToken)
    {
        var ids = specialtyIds?.Distinct().ToArray() ?? [];
        if (ids.Length == 0
            || !primarySpecialtyId.HasValue
            || !ids.Contains(primarySpecialtyId.Value))
        {
            ModelState.AddModelError("specialties", "Informe especialidades e uma especialidade principal pertencente à lista.");
            return [];
        }

        var items = await database.Specialties
            .Where(item => item.IsActive && ids.Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (items.Count != ids.Length)
        {
            ModelState.AddModelError("specialties", "Uma ou mais especialidades não existem ou estão inativas.");
        }

        return items;
    }

    private void AddSpecialtyLinks(
        ulong doctorAccountId,
        IEnumerable<Specialty> specialties,
        uint? primarySpecialtyId)
    {
        foreach (var specialty in specialties)
        {
            database.ProfessionalSpecialties.Add(new ProfessionalSpecialty
            {
                ProfessionalAccountId = doctorAccountId,
                SpecialtyId = specialty.Id,
                IsPrimary = specialty.Id == primarySpecialtyId,
            });
        }
    }

    private bool ValidateDoctorHour(ProfessionalWeeklyHourWriteRequest request)
    {
        var valid = ClinicAdministrationSupport.HasValidRange(request.StartTime, request.EndTime)
            && request.ModalityCode is "in_person" or "online" or "both"
            && (!request.ValidUntil.HasValue
                || !request.ValidFrom.HasValue
                || request.ValidUntil.Value >= request.ValidFrom.Value);
        if (!valid)
        {
            ModelState.AddModelError("timeRange", "Horário ou período de vigência inválido.");
        }

        return valid;
    }

    private async Task<bool> HasDoctorOverlap(
        ulong accountId,
        ProfessionalWeeklyHourWriteRequest request,
        ulong? excludedId,
        CancellationToken cancellationToken)
    {
        var validFrom = request.ValidFrom?.ToDateTime(TimeOnly.MinValue);
        var validUntil = request.ValidUntil?.ToDateTime(TimeOnly.MinValue);
        return await database.ProfessionalWeeklyHours.AsNoTracking().AnyAsync(
            item => item.ProfessionalAccountId == accountId
                && item.IsActive
                && item.DayOfWeek == request.DayOfWeek
                && (item.ModalityCode == request.ModalityCode || item.ModalityCode == "both" || request.ModalityCode == "both")
                && (!excludedId.HasValue || item.Id != excludedId.Value)
                && item.StartTime < request.EndTime
                && item.EndTime > request.StartTime
                && (!item.ValidUntil.HasValue || !validFrom.HasValue || item.ValidUntil >= validFrom)
                && (!validUntil.HasValue || !item.ValidFrom.HasValue || validUntil >= item.ValidFrom),
            cancellationToken);
    }

    private async Task<bool> WouldUncoverAppointments(ulong accountId, ulong excludedId,
        ProfessionalWeeklyHourWriteRequest? replacement, CancellationToken ct)
    {
        var mode = await database.ProfessionalPreferences.AsNoTracking()
            .Where(x => x.ProfessionalAccountId == accountId).Select(x => x.AvailabilityMode)
            .SingleOrDefaultAsync(ct);
        if (mode != "recurring") return false;
        var now = DateTime.UtcNow;
        var bookings = await database.Appointments.AsNoTracking()
            .Where(x => x.ProfessionalAccountId == accountId && x.StartsAtUtc >= now
                && (x.StatusCode == "pending" || x.StatusCode == "confirmed"
                    || x.StatusCode == "arrived" || x.StatusCode == "in_progress"))
            .Select(x => new { x.StartsAtUtc, x.EndsAtUtc, x.ModalityCode }).ToListAsync(ct);
        if (bookings.Count == 0) return false;
        var hours = await database.ProfessionalWeeklyHours.AsNoTracking()
            .Where(x => x.ProfessionalAccountId == accountId && x.IsActive && x.Id != excludedId).ToListAsync(ct);
        var exceptions = await database.ProfessionalAvailabilityExceptions.AsNoTracking()
            .Where(x => x.ProfessionalAccountId == accountId && x.IsAvailable).ToListAsync(ct);
        var zoneName = await database.Clinics.AsNoTracking().Select(x => x.TimezoneName).SingleOrDefaultAsync(ct)
            ?? "America/Sao_Paulo";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneName);
        foreach (var booking in bookings)
        {
            var start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(booking.StartsAtUtc, DateTimeKind.Utc), zone);
            var end = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(booking.EndsAtUtc, DateTimeKind.Utc), zone);
            if (exceptions.Any(x => x.ExceptionDate.Date == start.Date
                && (x.ModalityCode == "both" || x.ModalityCode == booking.ModalityCode))) continue;
            var covered = hours.Any(x => x.DayOfWeek == (byte)start.DayOfWeek
                && (x.ModalityCode == "both" || x.ModalityCode == booking.ModalityCode)
                && (!x.ValidFrom.HasValue || x.ValidFrom.Value.Date <= start.Date)
                && (!x.ValidUntil.HasValue || x.ValidUntil.Value.Date >= start.Date)
                && x.StartTime <= start.TimeOfDay && x.EndTime >= end.TimeOfDay);
            if (!covered && replacement is { IsActive: true })
                covered = replacement.DayOfWeek == (byte)start.DayOfWeek
                    && (replacement.ModalityCode == "both" || replacement.ModalityCode == booking.ModalityCode)
                    && (!replacement.ValidFrom.HasValue || replacement.ValidFrom.Value.ToDateTime(TimeOnly.MinValue) <= start.Date)
                    && (!replacement.ValidUntil.HasValue || replacement.ValidUntil.Value.ToDateTime(TimeOnly.MinValue) >= start.Date)
                    && replacement.StartTime <= start.TimeOfDay && replacement.EndTime >= end.TimeOfDay;
            if (!covered) return true;
        }
        return false;
    }

    private async Task Audit(string eventCode, string entityType, string entityId, CancellationToken cancellationToken) =>
        await auditWriter.WriteAsync(eventCode, ActorId, entityType, entityId, null, cancellationToken);

    private ObjectResult ConflictProblem(string title) =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    private static ProfessionalResponse ToResponse(Account account)
    {
        var links = account.ProfessionalProfile?.ProfessionalSpecialties.OrderBy(item => item.Specialty.Name).ToArray() ?? [];
        return new ProfessionalResponse(
            account.Id,
            account.FullName,
            account.RoleCode,
            account.StatusCode,
            account.Email,
            account.PhoneE164,
            account.TaxId,
            account.BirthDate.HasValue ? DateOnly.FromDateTime(account.BirthDate.Value) : null,
            account.ProfessionalProfile?.ProfessionalTitle,
            account.ProfessionalProfile?.LicenseStateCode,
            account.ProfessionalProfile?.LicenseNumber,
            account.ProfessionalProfile?.Biography,
            account.ProfessionalProfile?.YearsExperience,
            account.ProfessionalProfile?.DefaultAppointmentDurationMinutes,
            links.Select(link => new SpecialtyResponse(
                link.Specialty.Id,
                link.Specialty.Name,
                link.Specialty.IsActive,
                link.Specialty.RowVersion)).ToArray(),
            links.SingleOrDefault(link => link.IsPrimary)?.SpecialtyId,
            account.RowVersion,
            account.ProfessionalProfile?.RowVersion);
    }

    private static ProfessionalWeeklyHourResponse ToResponse(ProfessionalWeeklyHour item) => new(
        item.Id,
        item.DayOfWeek,
        item.StartTime,
        item.EndTime,
        item.ValidFrom.HasValue ? DateOnly.FromDateTime(item.ValidFrom.Value) : null,
        item.ValidUntil.HasValue ? DateOnly.FromDateTime(item.ValidUntil.Value) : null,
        item.IsActive,
        item.RowVersion,
        item.ModalityCode);
}
