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
        var entities = await query.OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(new PagedResponse<AppointmentTypeResponse>(
            entities.Select(ToResponse).ToArray(),
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
            PriceAmount = request.PriceAmount,
            IsActive = request.IsActive,
            DisplayOrder = request.DisplayOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.AppointmentTypes.Add(entity);
        if (!await TrySave(cancellationToken))
        {
            return ConflictProblem("Não foi possível cadastrar o tipo de atendimento.");
        }

        await Audit("catalog.appointment_type.created", "appointment_type", entity.Id.ToString(), cancellationToken);
        return CreatedAtAction(nameof(GetAppointmentTypes), ToResponse(entity));
    }

    [HttpPut("appointment-types/{id}")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    [ManagerFeatureGate("manager.appointment_types_enabled")]
    public async Task<ActionResult<AppointmentTypeResponse>> UpdateAppointmentType(
        uint id,
        [FromBody] AppointmentTypeWriteRequest request,
        CancellationToken cancellationToken)
    {
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
        entity.PriceAmount = request.PriceAmount;
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

    private static AppointmentTypeResponse ToResponse(AppointmentType item) => new(
        item.Id,
        item.Name,
        item.Description,
        item.CategoryCode,
        item.ModalityCode,
        item.DurationMinutes,
        item.PriceAmount,
        item.IsActive,
        item.DisplayOrder,
        item.RowVersion);

    private enum SaveResult
    {
        Saved,
        Concurrency,
        Conflict,
    }
}
