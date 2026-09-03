using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.ClinicAdministration;

[ApiController]
[Route("api/v1/clinic")]
[Authorize(Policy = ViverAppPolicies.Management)]
[EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
public sealed class ClinicConfigurationController(
    ViverAppDbContext database,
    IdentityAuditWriter auditWriter) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ClinicResponse>> Get(CancellationToken cancellationToken)
    {
        var clinic = await database.Clinics.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return clinic is null ? NotFound() : Ok(ToResponse(clinic));
    }

    [HttpPut]
    public async Task<ActionResult<ClinicResponse>> Put(
        [FromBody] ClinicUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidateAddress(request) || !TryValidateTimezone(request.TimezoneName))
        {
            return ValidationProblem(ModelState);
        }

        var clinic = await database.Clinics.SingleOrDefaultAsync(cancellationToken);
        var now = DateTime.UtcNow;
        if (clinic is null)
        {
            if (request.RowVersion != 0)
            {
                return ClinicAdministrationSupport.ConcurrencyProblem(this);
            }

            clinic = new Clinic
            {
                SingletonId = 1,
                CreatedAtUtc = now,
                RowVersion = 1,
            };
            database.Clinics.Add(clinic);
        }
        else
        {
            if (request.RowVersion == 0)
            {
                return ClinicAdministrationSupport.ConcurrencyProblem(this);
            }

            ClinicAdministrationSupport.SetConcurrency(database, clinic, nameof(Clinic.RowVersion), request.RowVersion);
        }

        clinic.LegalName = ClinicAdministrationSupport.RequiredText(request.LegalName);
        clinic.DisplayName = ClinicAdministrationSupport.RequiredText(request.DisplayName);
        clinic.TaxId = ClinicAdministrationSupport.OptionalText(request.TaxId);
        clinic.Email = ClinicAdministrationSupport.OptionalText(request.Email)?.ToLowerInvariant();
        clinic.PhoneE164 = ClinicAdministrationSupport.OptionalText(request.PhoneE164);
        clinic.PostalCode = ClinicAdministrationSupport.OptionalText(request.PostalCode);
        clinic.Street = ClinicAdministrationSupport.OptionalText(request.Street);
        clinic.Number = ClinicAdministrationSupport.OptionalText(request.Number);
        clinic.Complement = ClinicAdministrationSupport.OptionalText(request.Complement);
        clinic.District = ClinicAdministrationSupport.OptionalText(request.District);
        clinic.City = ClinicAdministrationSupport.OptionalText(request.City);
        clinic.StateCode = ClinicAdministrationSupport.OptionalText(request.StateCode)?.ToUpperInvariant();
        clinic.TimezoneName = request.TimezoneName.Trim();
        clinic.UpdatedAtUtc = now;

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
            return ConflictProblem("Não foi possível salvar a clínica.");
        }

        await Audit("clinic.updated", "clinic", "1", cancellationToken);
        return Ok(ToResponse(clinic));
    }

    [HttpGet("weekly-hours")]
    public async Task<ActionResult<IReadOnlyList<WeeklyHourResponse>>> GetWeeklyHours(
        CancellationToken cancellationToken)
    {
        var items = await database.ClinicWeeklyHours
            .AsNoTracking()
            .OrderBy(item => item.DayOfWeek)
            .ThenBy(item => item.StartTime)
            .Select(item => new WeeklyHourResponse(
                item.Id,
                item.DayOfWeek,
                item.StartTime,
                item.EndTime,
                item.IsActive,
                item.RowVersion))
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost("weekly-hours")]
    public async Task<ActionResult<WeeklyHourResponse>> CreateWeeklyHour(
        [FromBody] WeeklyHourWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RowVersion != 0 || !ValidateTimeRange(request.StartTime, request.EndTime))
        {
            return ValidationProblem(ModelState);
        }

        if (request.IsActive && await HasClinicOverlap(request.DayOfWeek, request.StartTime, request.EndTime, null, cancellationToken))
        {
            return ConflictProblem("O horário conflita com outro período ativo da clínica.");
        }

        var now = DateTime.UtcNow;
        var entity = new ClinicWeeklyHour
        {
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsActive = request.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.ClinicWeeklyHours.Add(entity);
        if (!await TrySave(cancellationToken))
        {
            return ConflictProblem("Não foi possível cadastrar o horário.");
        }

        await Audit("clinic.weekly_hour.created", "clinic_weekly_hour", entity.Id.ToString(), cancellationToken);
        return CreatedAtAction(nameof(GetWeeklyHours), ToWeeklyResponse(entity));
    }

    [HttpPut("weekly-hours/{id}")]
    public async Task<ActionResult<WeeklyHourResponse>> UpdateWeeklyHour(
        uint id,
        [FromBody] WeeklyHourWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RowVersion == 0 || !ValidateTimeRange(request.StartTime, request.EndTime))
        {
            return ValidationProblem(ModelState);
        }

        var entity = await database.ClinicWeeklyHours.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (request.IsActive && await HasClinicOverlap(request.DayOfWeek, request.StartTime, request.EndTime, id, cancellationToken))
        {
            return ConflictProblem("O horário conflita com outro período ativo da clínica.");
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(ClinicWeeklyHour.RowVersion), request.RowVersion);
        entity.DayOfWeek = request.DayOfWeek;
        entity.StartTime = request.StartTime;
        entity.EndTime = request.EndTime;
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

        await Audit("clinic.weekly_hour.updated", "clinic_weekly_hour", id.ToString(), cancellationToken);
        return Ok(ToWeeklyResponse(entity));
    }

    [HttpDelete("weekly-hours/{id}")]
    public async Task<IActionResult> DeleteWeeklyHour(
        uint id,
        [FromQuery] ulong rowVersion,
        CancellationToken cancellationToken)
    {
        var entity = await database.ClinicWeeklyHours.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(ClinicWeeklyHour.RowVersion), rowVersion);
        database.ClinicWeeklyHours.Remove(entity);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }

        await Audit("clinic.weekly_hour.deleted", "clinic_weekly_hour", id.ToString(), cancellationToken);
        return NoContent();
    }

    [HttpGet("holidays")]
    public async Task<ActionResult<PagedResponse<HolidayResponse>>> GetHolidays(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? year = null,
        CancellationToken cancellationToken = default)
    {
        _ = ValidatePage(page, pageSize);
        if (year is < 2000 or > 2200)
        {
            ModelState.AddModelError(nameof(year), "O ano deve estar entre 2000 e 2200.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var query = database.Holidays.AsNoTracking();
        if (year.HasValue)
        {
            query = query.Where(item => item.HolidayDate.Year == year.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var entities = await query.OrderBy(item => item.HolidayDate)
            .ThenBy(item => item.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(new PagedResponse<HolidayResponse>(entities.Select(ToHolidayResponse).ToArray(), page, pageSize, total));
    }

    [HttpPost("holidays")]
    public async Task<ActionResult<HolidayResponse>> CreateHoliday(
        [FromBody] HolidayWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RowVersion != 0 || !ValidateOptionalTimeRange(request.StartTime, request.EndTime))
        {
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var entity = new Holiday
        {
            HolidayDate = request.HolidayDate.ToDateTime(TimeOnly.MinValue),
            Name = ClinicAdministrationSupport.RequiredText(request.Name),
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Holidays.Add(entity);
        if (!await TrySave(cancellationToken))
        {
            return ConflictProblem("Já existe um feriado equivalente.");
        }

        await Audit("clinic.holiday.created", "holiday", entity.Id.ToString(), cancellationToken);
        return CreatedAtAction(nameof(GetHolidays), ToHolidayResponse(entity));
    }

    [HttpPut("holidays/{id}")]
    public async Task<ActionResult<HolidayResponse>> UpdateHoliday(
        uint id,
        [FromBody] HolidayWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RowVersion == 0 || !ValidateOptionalTimeRange(request.StartTime, request.EndTime))
        {
            return ValidationProblem(ModelState);
        }

        var entity = await database.Holidays.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(Holiday.RowVersion), request.RowVersion);
        entity.HolidayDate = request.HolidayDate.ToDateTime(TimeOnly.MinValue);
        entity.Name = ClinicAdministrationSupport.RequiredText(request.Name);
        entity.StartTime = request.StartTime;
        entity.EndTime = request.EndTime;
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
            return ConflictProblem("Não foi possível atualizar o feriado.");
        }

        await Audit("clinic.holiday.updated", "holiday", id.ToString(), cancellationToken);
        return Ok(ToHolidayResponse(entity));
    }

    [HttpDelete("holidays/{id}")]
    public async Task<IActionResult> DeleteHoliday(
        uint id,
        [FromQuery] ulong rowVersion,
        CancellationToken cancellationToken)
    {
        var entity = await database.Holidays.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        ClinicAdministrationSupport.SetConcurrency(database, entity, nameof(Holiday.RowVersion), rowVersion);
        database.Holidays.Remove(entity);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }

        await Audit("clinic.holiday.deleted", "holiday", id.ToString(), cancellationToken);
        return NoContent();
    }

    private async Task<bool> HasClinicOverlap(
        byte day,
        TimeSpan start,
        TimeSpan end,
        uint? excludedId,
        CancellationToken cancellationToken) =>
        await database.ClinicWeeklyHours.AsNoTracking().AnyAsync(
            item => item.IsActive
                && item.DayOfWeek == day
                && (!excludedId.HasValue || item.Id != excludedId.Value)
                && item.StartTime < end
                && item.EndTime > start,
            cancellationToken);

    private bool ValidateTimeRange(TimeSpan start, TimeSpan end)
    {
        if (ClinicAdministrationSupport.HasValidRange(start, end))
        {
            return true;
        }

        ModelState.AddModelError("timeRange", "O horário final deve ser posterior ao inicial no mesmo dia.");
        return false;
    }

    private bool ValidateOptionalTimeRange(TimeSpan? start, TimeSpan? end)
    {
        if ((!start.HasValue && !end.HasValue)
            || (start.HasValue && end.HasValue && ClinicAdministrationSupport.HasValidRange(start.Value, end.Value)))
        {
            return true;
        }

        ModelState.AddModelError("timeRange", "Informe início e fim válidos ou deixe ambos vazios.");
        return false;
    }

    private bool TryValidateAddress(ClinicUpdateRequest request)
    {
        var required = new[] { request.PostalCode, request.Street, request.Number, request.District, request.City, request.StateCode };
        var populated = required.Count(value => !string.IsNullOrWhiteSpace(value));
        if (populated == 0 || populated == required.Length)
        {
            return true;
        }

        ModelState.AddModelError("address", "O endereço deve ser informado por completo.");
        return false;
    }

    private bool TryValidateTimezone(string timezoneName)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezoneName.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            ModelState.AddModelError(nameof(timezoneName), "Fuso horário inválido.");
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            ModelState.AddModelError(nameof(timezoneName), "Fuso horário inválido.");
            return false;
        }
    }

    private bool ValidatePage(int page, int pageSize)
    {
        if (page >= 1 && pageSize is >= 1 and <= 100)
        {
            return true;
        }

        ModelState.AddModelError("pagination", "Página e tamanho de página inválidos.");
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

    private static ClinicResponse ToResponse(Clinic clinic) => new(
        clinic.LegalName,
        clinic.DisplayName,
        clinic.TaxId,
        clinic.Email,
        clinic.PhoneE164,
        clinic.PostalCode,
        clinic.Street,
        clinic.Number,
        clinic.Complement,
        clinic.District,
        clinic.City,
        clinic.StateCode,
        clinic.TimezoneName,
        clinic.RowVersion);

    private static WeeklyHourResponse ToWeeklyResponse(ClinicWeeklyHour item) => new(
        item.Id,
        item.DayOfWeek,
        item.StartTime,
        item.EndTime,
        item.IsActive,
        item.RowVersion);

    private static HolidayResponse ToHolidayResponse(Holiday item) => new(
        item.Id,
        DateOnly.FromDateTime(item.HolidayDate),
        item.Name,
        item.StartTime,
        item.EndTime,
        item.RowVersion);
}
