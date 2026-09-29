using Microsoft.AspNetCore.Authorization;
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
[Route("api/v1/catalog")]
[Authorize]
[EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
[ServiceFilter(typeof(AdministratorStepUpFilter))]
public sealed class CatalogController(
    ViverAppDbContext database,
    IdentityAuditWriter auditWriter) : ControllerBase
{
    [HttpGet("specialties")]
    public async Task<ActionResult<PagedResponse<SpecialtyResponse>>> GetSpecialties(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        if (!ValidatePagination(page, pageSize, search))
        {
            return ValidationProblem(ModelState);
        }

        var query = database.Specialties.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(item => item.IsActive);
        }

        var normalizedSearch = ClinicAdministrationSupport.OptionalText(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.Contains(normalizedSearch));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(item => item.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new SpecialtyResponse(item.Id, item.Name, item.IsActive, item.RowVersion))
            .ToListAsync(cancellationToken);
        return Ok(new PagedResponse<SpecialtyResponse>(items, page, pageSize, total));
    }

    [HttpPost("specialties")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    public async Task<ActionResult<SpecialtyResponse>> CreateSpecialty(
        [FromBody] SpecialtyWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RowVersion != 0)
        {
            ModelState.AddModelError(nameof(request.RowVersion), "A versão deve ser zero em novos registros.");
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var entity = new Specialty
        {
            Name = ClinicAdministrationSupport.RequiredText(request.Name),
            NormalizedName = ClinicAdministrationSupport.NormalizeName(request.Name),
            IsActive = request.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Specialties.Add(entity);
        if (!await TrySave(cancellationToken))
        {
            return ConflictProblem("Já existe uma especialidade com esse nome.");
        }

        await Audit("catalog.specialty.created", "specialty", entity.Id.ToString(), cancellationToken);
        return CreatedAtAction(nameof(GetSpecialties), ToResponse(entity));
    }

    [HttpPut("specialties/{id}")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    public async Task<ActionResult<SpecialtyResponse>> UpdateSpecialty(
        uint id,
        [FromBody] SpecialtyWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RowVersion == 0)
        {
            ModelState.AddModelError(nameof(request.RowVersion), "A versão atual é obrigatória.");
            return ValidationProblem(ModelState);
        }

        var entity = await database.Specialties.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(Specialty.RowVersion), request.RowVersion);
        entity.Name = ClinicAdministrationSupport.RequiredText(request.Name);
        entity.NormalizedName = ClinicAdministrationSupport.NormalizeName(request.Name);
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        var save = await TrySaveWithConcurrency(cancellationToken);
        if (save == SaveResult.Concurrency)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }

        if (save == SaveResult.Conflict)
        {
            return ConflictProblem("Já existe uma especialidade com esse nome.");
        }

        await Audit("catalog.specialty.updated", "specialty", id.ToString(), cancellationToken);
        return Ok(ToResponse(entity));
    }

    [HttpDelete("specialties/{id}")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    public async Task<IActionResult> DeactivateSpecialty(
        uint id,
        [FromQuery] ulong rowVersion,
        CancellationToken cancellationToken)
    {
        var entity = await database.Specialties.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(Specialty.RowVersion), rowVersion);
        entity.IsActive = false;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        var save = await TrySaveWithConcurrency(cancellationToken);
        if (save == SaveResult.Concurrency)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }

        await Audit("catalog.specialty.deactivated", "specialty", id.ToString(), cancellationToken);
        return NoContent();
    }

    [HttpGet("appointment-types")]
    public async Task<ActionResult<PagedResponse<AppointmentTypeResponse>>> GetAppointmentTypes(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? modality = null,
        [FromQuery] string? category = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        _ = ValidatePagination(page, pageSize, search);
        if (modality is not null && modality is not ("in_person" or "online" or "both"))
        {
            ModelState.AddModelError(nameof(modality), "Modalidade inválida.");
        }
        if (category is not null && category is not ("consultation" or "examination" or "surgery" or "procedure"))
        {
            ModelState.AddModelError(nameof(category), "Tipo de atendimento inválido.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var query = database.AppointmentTypes.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(item => item.IsActive);
        }

        var normalizedSearch = ClinicAdministrationSupport.OptionalText(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.Contains(normalizedSearch));
        }

        if (modality is not null)
        {
            query = query.Where(item => item.ModalityCode == modality);
        }
        if (category is not null)
        {
            query = query.Where(item => item.CategoryCode == category);
        }

        var total = await query.CountAsync(cancellationToken);
        var entities = await query.OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var ids = entities.Select(item => item.Id).ToArray();
        var usedIds = await database.Appointments.AsNoTracking()
            .Where(item => ids.Contains(item.AppointmentTypeId))
            .Select(item => item.AppointmentTypeId).Distinct().ToArrayAsync(cancellationToken);
        var used = usedIds.ToHashSet();
        return Ok(new PagedResponse<AppointmentTypeResponse>(
            entities.Select(item => ToResponse(item, !used.Contains(item.Id))).ToArray(),
            page,
            pageSize,
            total));
    }

    [HttpPost("appointment-types")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    [ManagerFeatureGate("manager.appointment_types_enabled")]
    public async Task<ActionResult<AppointmentTypeResponse>> CreateAppointmentType(
        [FromBody] AppointmentTypeWriteRequest request,
        CancellationToken cancellationToken)
    {
        var professionalIds = request.ProfessionalAccountIds?.Distinct().ToArray() ?? [];
        if (professionalIds.Length > 0 && User.IsInRole(ViverAppRoles.Manager))
        {
            var permission = await database.ApplicationSettings.AsNoTracking()
                .Where(item => item.SettingKey == "manager.professional_services_enabled")
                .Select(item => item.ValueJson).SingleOrDefaultAsync(cancellationToken);
            if (permission is null || !ManagerFeatureGateFilter.TryReadBoolean(permission, out var allowed) || !allowed)
                return Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "O vínculo de profissionais está desabilitado para o Gestor.");
        }
        if (professionalIds.Length > 0)
        {
            var validCount = await database.ProfessionalProfiles.AsNoTracking()
                .CountAsync(profile => professionalIds.Contains(profile.AccountId)
                    && profile.Account.StatusCode == "active"
                    && (profile.Account.RoleCode == ViverAppRoles.Doctor
                        || profile.Account.RoleCode == ViverAppRoles.Psychologist), cancellationToken);
            if (validCount != professionalIds.Length)
            {
                ModelState.AddModelError(nameof(request.ProfessionalAccountIds),
                    "Selecione apenas médicos ou psicólogos ativos.");
                return ValidationProblem(ModelState);
            }
        }
        if (request.RequiresPayment && request.PriceAmount <= 0)
        {
            ModelState.AddModelError(nameof(request.PriceAmount), "Informe um preço maior que zero para um atendimento cobrado.");
            return ValidationProblem(ModelState);
        }

        if (request.RowVersion != 0)
        {
            ModelState.AddModelError(nameof(request.RowVersion), "A versão deve ser zero em novos registros.");
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var entity = new AppointmentType
        {
            Name = ClinicAdministrationSupport.RequiredText(request.Name),
            Description = ClinicAdministrationSupport.OptionalText(request.Description),
            CategoryCode = request.CategoryCode,
            ModalityCode = request.ModalityCode,
            DurationMinutes = request.DurationMinutes,
            PriceAmount = request.RequiresPayment ? request.PriceAmount : 0,
            RequiresPayment = request.RequiresPayment,
            IsActive = request.IsActive,
            DisplayOrder = request.DisplayOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        await using var transaction = database.Database.CurrentTransaction is null
            ? await database.Database.BeginTransactionAsync(cancellationToken) : null;
        database.AppointmentTypes.Add(entity);
        if (!await TrySave(cancellationToken))
        {
            return ConflictProblem("Não foi possível cadastrar o tipo de atendimento.");
        }

        foreach (var professionalId in professionalIds)
            database.ProfessionalServices.Add(new ProfessionalService
            {
                ProfessionalAccountId = professionalId,
                AppointmentTypeId = entity.Id,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = 1,
            });
        if (professionalIds.Length > 0 && !await TrySave(cancellationToken))
            return ConflictProblem("Não foi possível vincular os profissionais. O tipo não foi cadastrado.");
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        await Audit("catalog.appointment_type.created", "appointment_type", entity.Id.ToString(), cancellationToken);
        return CreatedAtAction(nameof(GetAppointmentTypes), ToResponse(entity));
    }

    [HttpGet("professionals")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    [ManagerFeatureGate("manager.professional_services_enabled")]
    public async Task<ActionResult<IReadOnlyList<AppointmentTypeProfessionalResponse>>> GetProfessionals(
        CancellationToken cancellationToken)
    {
        var professionals = await database.ProfessionalProfiles.AsNoTracking()
            .Where(profile => profile.Account.StatusCode == "active"
                && (profile.Account.RoleCode == ViverAppRoles.Doctor
                    || profile.Account.RoleCode == ViverAppRoles.Psychologist))
            .OrderBy(profile => profile.Account.FullName)
            .Select(profile => new AppointmentTypeProfessionalResponse(
                profile.AccountId, profile.Account.FullName, profile.Account.RoleCode,
                profile.LicenseTypeCode + " " + profile.LicenseStateCode + " " + profile.LicenseNumber, false))
            .ToArrayAsync(cancellationToken);
        return Ok(professionals);
    }

    [HttpPut("appointment-types/{id}")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    [ManagerFeatureGate("manager.appointment_types_enabled")]
    public async Task<ActionResult<AppointmentTypeResponse>> UpdateAppointmentType(
        uint id,
        [FromBody] AppointmentTypeWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RequiresPayment && request.PriceAmount <= 0)
        {
            ModelState.AddModelError(nameof(request.PriceAmount), "Informe um preço maior que zero para um atendimento cobrado.");
            return ValidationProblem(ModelState);
        }

        if (request.RowVersion == 0)
        {
            ModelState.AddModelError(nameof(request.RowVersion), "A versão atual é obrigatória.");
            return ValidationProblem(ModelState);
        }

        var entity = await database.AppointmentTypes.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(AppointmentType.RowVersion), request.RowVersion);
        entity.Name = ClinicAdministrationSupport.RequiredText(request.Name);
        entity.Description = ClinicAdministrationSupport.OptionalText(request.Description);
        entity.CategoryCode = request.CategoryCode;
        entity.ModalityCode = request.ModalityCode;
        entity.DurationMinutes = request.DurationMinutes;
        entity.PriceAmount = request.RequiresPayment ? request.PriceAmount : 0;
        entity.RequiresPayment = request.RequiresPayment;
        entity.IsActive = request.IsActive;
        entity.DisplayOrder = request.DisplayOrder;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        var save = await TrySaveWithConcurrency(cancellationToken);
        if (save == SaveResult.Concurrency)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }

        if (save == SaveResult.Conflict)
        {
            return ConflictProblem("Não foi possível atualizar o tipo de atendimento.");
        }

        await Audit("catalog.appointment_type.updated", "appointment_type", id.ToString(), cancellationToken);
        return Ok(ToResponse(entity));
    }

    [HttpDelete("appointment-types/{id}")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    [ManagerFeatureGate("manager.appointment_types_enabled")]
    public async Task<IActionResult> DeactivateAppointmentType(
        uint id,
        [FromQuery] ulong rowVersion,
        CancellationToken cancellationToken)
    {
        var entity = await database.AppointmentTypes.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(AppointmentType.RowVersion), rowVersion);
        entity.IsActive = false;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        var save = await TrySaveWithConcurrency(cancellationToken);
        if (save == SaveResult.Concurrency)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }

        await Audit("catalog.appointment_type.deactivated", "appointment_type", id.ToString(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("appointment-types/{id}/permanent")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    [ManagerFeatureGate("manager.appointment_types_enabled")]
    public async Task<IActionResult> DeleteUnusedAppointmentType(
        uint id, [FromQuery] ulong rowVersion, CancellationToken cancellationToken)
    {
        if (rowVersion == 0)
            return Problem(statusCode: 400, title: "Recarregue o tipo de atendimento antes de excluí-lo.");

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var entity = await database.AppointmentTypes.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null) return NotFound();
        if (entity.RowVersion != rowVersion) return ClinicAdministrationSupport.ConcurrencyProblem(this);
        if (await database.Appointments.AsNoTracking().AnyAsync(item => item.AppointmentTypeId == id, cancellationToken))
            return ConflictProblem("Este tipo já foi usado em um agendamento e não pode ser excluído. Você pode desativá-lo.");

        var links = await database.ProfessionalServices.Where(item => item.AppointmentTypeId == id)
            .ToListAsync(cancellationToken);
        database.ProfessionalServices.RemoveRange(links);
        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(AppointmentType.RowVersion), rowVersion);
        database.AppointmentTypes.Remove(entity);
        var save = await TrySaveWithConcurrency(cancellationToken);
        if (save == SaveResult.Concurrency) return ClinicAdministrationSupport.ConcurrencyProblem(this);
        if (save == SaveResult.Conflict)
            return ConflictProblem("O tipo foi associado a um atendimento durante a exclusão. Recarregue a lista.");
        await transaction.CommitAsync(cancellationToken);
        await Audit("catalog.appointment_type.deleted", "appointment_type", id.ToString(), cancellationToken);
        return NoContent();
    }

    [HttpGet("appointment-types/{id}/professionals")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    [ManagerFeatureGate("manager.professional_services_enabled")]
    public async Task<ActionResult<IReadOnlyList<AppointmentTypeProfessionalResponse>>> GetAppointmentTypeProfessionals(
        uint id,
        CancellationToken cancellationToken)
    {
        if (!await database.AppointmentTypes.AsNoTracking().AnyAsync(item => item.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var professionals = await database.ProfessionalProfiles.AsNoTracking()
            .Where(profile => profile.Account.StatusCode == "active"
                && (profile.Account.RoleCode == ViverAppRoles.Doctor
                    || profile.Account.RoleCode == ViverAppRoles.Psychologist))
            .OrderBy(profile => profile.Account.FullName)
            .Select(profile => new AppointmentTypeProfessionalResponse(
                profile.AccountId,
                profile.Account.FullName,
                profile.Account.RoleCode,
                profile.LicenseTypeCode + " " + profile.LicenseStateCode + " " + profile.LicenseNumber,
                profile.ProfessionalServices.Any(link => link.AppointmentTypeId == id && link.IsActive)))
            .ToArrayAsync(cancellationToken);

        return Ok(professionals);
    }

    [HttpPut("appointment-types/{id}/professionals")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    [ManagerFeatureGate("manager.professional_services_enabled")]
    public async Task<ActionResult<IReadOnlyList<AppointmentTypeProfessionalResponse>>> UpdateAppointmentTypeProfessionals(
        uint id,
        [FromBody] AppointmentTypeProfessionalsUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!await database.AppointmentTypes.AnyAsync(item => item.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var selectedIds = request.ProfessionalAccountIds?.Distinct().ToArray() ?? [];
        var validIds = await database.ProfessionalProfiles.AsNoTracking()
            .Where(profile => selectedIds.Contains(profile.AccountId)
                && profile.Account.StatusCode == "active"
                && (profile.Account.RoleCode == ViverAppRoles.Doctor
                    || profile.Account.RoleCode == ViverAppRoles.Psychologist))
            .Select(profile => profile.AccountId)
            .ToArrayAsync(cancellationToken);
        if (validIds.Length != selectedIds.Length)
        {
            ModelState.AddModelError(nameof(request.ProfessionalAccountIds),
                "Um ou mais profissionais não existem, estão inativos ou não são clínicos.");
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var existing = await database.ProfessionalServices
            .Where(link => link.AppointmentTypeId == id)
            .ToArrayAsync(cancellationToken);
        foreach (var link in existing)
        {
            link.IsActive = selectedIds.Contains(link.ProfessionalAccountId);
            link.UpdatedAtUtc = now;
            link.RowVersion++;
        }

        var existingIds = existing.Select(link => link.ProfessionalAccountId).ToHashSet();
        foreach (var professionalId in selectedIds.Where(professionalId => !existingIds.Contains(professionalId)))
        {
            database.ProfessionalServices.Add(new ProfessionalService
            {
                ProfessionalAccountId = professionalId,
                AppointmentTypeId = id,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = 1,
            });
        }

        await database.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(
            "catalog.appointment_type.professionals_updated",
            ClinicAdministrationSupport.RequireActorId(User),
            "appointment_type",
            id.ToString(),
            new Dictionary<string, string> { ["linkedCount"] = selectedIds.Length.ToString() },
            cancellationToken);
        return await GetAppointmentTypeProfessionals(id, cancellationToken);
    }

    private bool ValidatePagination(int page, int pageSize, string? search)
    {
        if (page >= 1 && pageSize is >= 1 and <= 100 && (search?.Length ?? 0) <= 120)
        {
            return true;
        }

        ModelState.AddModelError("pagination", "Filtros ou paginação inválidos.");
        return false;
    }

    private async Task<bool> TrySave(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task<SaveResult> TrySaveWithConcurrency(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return SaveResult.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return SaveResult.Concurrency;
        }
        catch (DbUpdateException)
        {
            return SaveResult.Conflict;
        }
    }

    private async Task Audit(string eventCode, string entityType, string entityId, CancellationToken cancellationToken) =>
        await auditWriter.WriteAsync(
            eventCode,
            ClinicAdministrationSupport.RequireActorId(User),
            entityType,
            entityId,
            null,
            cancellationToken);

    private ObjectResult ConflictProblem(string title) =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    private static SpecialtyResponse ToResponse(Specialty item) =>
        new(item.Id, item.Name, item.IsActive, item.RowVersion);

    private static AppointmentTypeResponse ToResponse(AppointmentType item, bool canDelete = false) => new(
        item.Id,
        item.Name,
        item.Description,
        item.CategoryCode,
        item.ModalityCode,
        item.DurationMinutes,
        item.PriceAmount,
        item.RequiresPayment,
        item.IsActive,
        item.DisplayOrder,
        item.RowVersion,
        canDelete);

    private enum SaveResult
    {
        Saved,
        Concurrency,
        Conflict,
    }
}
