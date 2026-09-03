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

namespace ViverApp.Api.Features.ClinicAdministration;

[ApiController]
[Route("api/v1/professionals")]
[Authorize]
[EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
public sealed class ProfessionalsController(
    ViverAppDbContext database,
    UserManager<ViverAppUser> userManager,
    IdentityChallengeService challengeService,
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
            query = query.Where(item => item.FullName.Contains(term)
                || item.Email != null && item.Email.Contains(term)
                || item.PhoneE164 != null && item.PhoneE164.Contains(term));
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

        var specialties = request.RoleCode == ViverAppRoles.Doctor
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

        if (request.RoleCode == ViverAppRoles.Doctor)
        {
            var now = DateTime.UtcNow;
            var profile = new DoctorProfile
            {
                AccountId = user.Id,
                LicenseStateCode = request.LicenseStateCode!.Trim().ToUpperInvariant(),
                LicenseNumber = request.LicenseNumber!.Trim().ToUpperInvariant(),
                Biography = ClinicAdministrationSupport.OptionalText(request.Biography),
                DefaultAppointmentDurationMinutes = request.DefaultAppointmentDurationMinutes!.Value,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = 1,
            };
            database.DoctorProfiles.Add(profile);
            AddSpecialtyLinks(user.Id, specialties, request.PrimarySpecialtyId);
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
            .Include(item => item.DoctorProfile)
            .ThenInclude(profile => profile!.DoctorSpecialties)
            .SingleOrDefaultAsync(
                item => item.Id == accountId
                    && (item.RoleCode == ViverAppRoles.Doctor || item.RoleCode == ViverAppRoles.Manager),
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
        if (account.RoleCode == ViverAppRoles.Doctor)
        {
            if (account.DoctorProfile is null
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

        if (account.DoctorProfile is not null)
        {
            ClinicAdministrationSupport.SetConcurrency(
                database,
                account.DoctorProfile,
                nameof(DoctorProfile.RowVersion),
                request.ProfileRowVersion!.Value);
            account.DoctorProfile.Biography = ClinicAdministrationSupport.OptionalText(request.Biography);
            account.DoctorProfile.DefaultAppointmentDurationMinutes = request.DefaultAppointmentDurationMinutes!.Value;
            account.DoctorProfile.UpdatedAtUtc = DateTime.UtcNow;
            database.DoctorSpecialties.RemoveRange(account.DoctorProfile.DoctorSpecialties);
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
                && (item.RoleCode == ViverAppRoles.Doctor || item.RoleCode == ViverAppRoles.Manager),
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
        new Dictionary<string, string> { ["decision"] = request.DecisionCode },
        cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var updated = await ProfessionalQuery().SingleAsync(item => item.Id == accountId, cancellationToken);
        return Ok(ToResponse(updated));
    }

    [HttpGet("{accountId:long}/weekly-hours")]
    public async Task<ActionResult<IReadOnlyList<DoctorWeeklyHourResponse>>> GetWeeklyHours(
        ulong accountId,
        CancellationToken cancellationToken)
    {
        if (!CanAccessProfessional(accountId))
        {
            return Forbid();
        }

        var items = await database.DoctorWeeklyHours.AsNoTracking()
            .Where(item => item.DoctorAccountId == accountId)
            .OrderBy(item => item.DayOfWeek)
            .ThenBy(item => item.StartTime)
            .ToListAsync(cancellationToken);
        return Ok(items.Select(ToResponse).ToArray());
    }

    [HttpPost("{accountId:long}/weekly-hours")]
    public async Task<ActionResult<DoctorWeeklyHourResponse>> CreateWeeklyHour(
        ulong accountId,
        [FromBody] DoctorWeeklyHourWriteRequest request,
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

        if (!await database.DoctorProfiles.AsNoTracking().AnyAsync(item => item.AccountId == accountId, cancellationToken))
        {
            return NotFound();
        }

        if (request.IsActive && await HasDoctorOverlap(accountId, request, null, cancellationToken))
        {
            return ConflictProblem("O horário conflita com outro período ativo do médico.");
        }

        var now = DateTime.UtcNow;
        var entity = new DoctorWeeklyHour
        {
            DoctorAccountId = accountId,
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            ValidFrom = request.ValidFrom?.ToDateTime(TimeOnly.MinValue),
            ValidUntil = request.ValidUntil?.ToDateTime(TimeOnly.MinValue),
            IsActive = request.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.DoctorWeeklyHours.Add(entity);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return ConflictProblem("Não foi possível cadastrar o horário.");
        }

        await Audit("professional.weekly_hour.created", "doctor_weekly_hour", entity.Id.ToString(), cancellationToken);
        return CreatedAtAction(nameof(GetWeeklyHours), new { accountId }, ToResponse(entity));
    }

    [HttpPut("{accountId:long}/weekly-hours/{id:long}")]
    public async Task<ActionResult<DoctorWeeklyHourResponse>> UpdateWeeklyHour(
        ulong accountId,
        ulong id,
        [FromBody] DoctorWeeklyHourWriteRequest request,
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

        var entity = await database.DoctorWeeklyHours.SingleOrDefaultAsync(
            item => item.Id == id && item.DoctorAccountId == accountId,
            cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (request.IsActive && await HasDoctorOverlap(accountId, request, id, cancellationToken))
        {
            return ConflictProblem("O horário conflita com outro período ativo do médico.");
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(DoctorWeeklyHour.RowVersion), request.RowVersion);
        entity.DayOfWeek = request.DayOfWeek;
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

        await Audit("professional.weekly_hour.updated", "doctor_weekly_hour", id.ToString(), cancellationToken);
        return Ok(ToResponse(entity));
    }

    [HttpDelete("{accountId:long}/weekly-hours/{id:long}")]
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

        var entity = await database.DoctorWeeklyHours.SingleOrDefaultAsync(
            item => item.Id == id && item.DoctorAccountId == accountId,
            cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(DoctorWeeklyHour.RowVersion), rowVersion);
        database.DoctorWeeklyHours.Remove(entity);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }

        await Audit("professional.weekly_hour.deleted", "doctor_weekly_hour", id.ToString(), cancellationToken);
        return NoContent();
    }

    private ulong ActorId => ClinicAdministrationSupport.RequireActorId(User);

    private IQueryable<Account> ProfessionalQuery() => database.Accounts.AsNoTracking()
        .Where(item => item.RoleCode == ViverAppRoles.Doctor || item.RoleCode == ViverAppRoles.Manager)
        .Include(item => item.DoctorProfile)
        .ThenInclude(profile => profile!.DoctorSpecialties)
        .ThenInclude(link => link.Specialty);

    private bool CanAccessProfessional(ulong accountId) =>
        User.IsInRole(ViverAppRoles.Manager)
        || User.IsInRole(ViverAppRoles.Administrator)
        || User.IsInRole(ViverAppRoles.Doctor) && ActorId == accountId;

    private bool CanAccessProfessional(Account account) =>
        User.IsInRole(ViverAppRoles.Administrator)
        || ActorId == account.Id
        || User.IsInRole(ViverAppRoles.Manager) && account.RoleCode == ViverAppRoles.Doctor;

    private bool ValidateFilters(int page, int pageSize, string? search, string? role, string? status)
    {
        var valid = page >= 1
            && pageSize is >= 1 and <= 100
            && (search?.Length ?? 0) <= 200
            && (role is null || role is ViverAppRoles.Doctor or ViverAppRoles.Manager)
            && (status is null || status is "pending_confirmation" or "pending_approval" or "active" or "rejected" or "blocked");
        if (!valid)
        {
            ModelState.AddModelError("filters", "Filtros ou paginação inválidos.");
        }

        return valid;
    }

    private bool ValidateProfessionalFields(ProfessionalCreateRequest request)
    {
        if (request.RoleCode == ViverAppRoles.Doctor)
        {
            var valid = !string.IsNullOrWhiteSpace(request.LicenseStateCode)
                && !string.IsNullOrWhiteSpace(request.LicenseNumber)
                && request.DefaultAppointmentDurationMinutes.HasValue;
            if (!valid)
            {
                ModelState.AddModelError("profile", "CRM, estado e duração padrão são obrigatórios para médicos.");
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
            database.DoctorSpecialties.Add(new DoctorSpecialty
            {
                DoctorAccountId = doctorAccountId,
                SpecialtyId = specialty.Id,
                IsPrimary = specialty.Id == primarySpecialtyId,
            });
        }
    }

    private bool ValidateDoctorHour(DoctorWeeklyHourWriteRequest request)
    {
        var valid = ClinicAdministrationSupport.HasValidRange(request.StartTime, request.EndTime)
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
        DoctorWeeklyHourWriteRequest request,
        ulong? excludedId,
        CancellationToken cancellationToken)
    {
        var validFrom = request.ValidFrom?.ToDateTime(TimeOnly.MinValue);
        var validUntil = request.ValidUntil?.ToDateTime(TimeOnly.MinValue);
        return await database.DoctorWeeklyHours.AsNoTracking().AnyAsync(
            item => item.DoctorAccountId == accountId
                && item.IsActive
                && item.DayOfWeek == request.DayOfWeek
                && (!excludedId.HasValue || item.Id != excludedId.Value)
                && item.StartTime < request.EndTime
                && item.EndTime > request.StartTime
                && (!item.ValidUntil.HasValue || !validFrom.HasValue || item.ValidUntil >= validFrom)
                && (!validUntil.HasValue || !item.ValidFrom.HasValue || validUntil >= item.ValidFrom),
            cancellationToken);
    }

    private async Task Audit(string eventCode, string entityType, string entityId, CancellationToken cancellationToken) =>
        await auditWriter.WriteAsync(eventCode, ActorId, entityType, entityId, null, cancellationToken);

    private ObjectResult ConflictProblem(string title) =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    private static ProfessionalResponse ToResponse(Account account)
    {
        var links = account.DoctorProfile?.DoctorSpecialties.OrderBy(item => item.Specialty.Name).ToArray() ?? [];
        return new ProfessionalResponse(
            account.Id,
            account.FullName,
            account.RoleCode,
            account.StatusCode,
            account.Email,
            account.PhoneE164,
            account.DoctorProfile?.LicenseStateCode,
            account.DoctorProfile?.LicenseNumber,
            account.DoctorProfile?.Biography,
            account.DoctorProfile?.DefaultAppointmentDurationMinutes,
            links.Select(link => new SpecialtyResponse(
                link.Specialty.Id,
                link.Specialty.Name,
                link.Specialty.IsActive,
                link.Specialty.RowVersion)).ToArray(),
            links.SingleOrDefault(link => link.IsPrimary)?.SpecialtyId,
            account.RowVersion,
            account.DoctorProfile?.RowVersion);
    }

    private static DoctorWeeklyHourResponse ToResponse(DoctorWeeklyHour item) => new(
        item.Id,
        item.DayOfWeek,
        item.StartTime,
        item.EndTime,
        item.ValidFrom.HasValue ? DateOnly.FromDateTime(item.ValidFrom.Value) : null,
        item.ValidUntil.HasValue ? DateOnly.FromDateTime(item.ValidUntil.Value) : null,
        item.IsActive,
        item.RowVersion);
}
