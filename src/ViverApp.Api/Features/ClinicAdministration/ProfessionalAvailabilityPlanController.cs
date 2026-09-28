using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.AdministratorExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.ClinicAdministration;

public sealed record VariableAvailabilityInterval(TimeOnly StartsAt, TimeOnly EndsAt, string ModalityCode);
public sealed record VariableAvailabilityDay(DateOnly Date, IReadOnlyList<VariableAvailabilityInterval> Intervals);
public sealed record VariableAvailabilityPlan(string Mode, ulong RowVersion, bool OnlineEnabled,
    IReadOnlyList<VariableAvailabilityDay> Days, IReadOnlyList<VariableAvailabilityDay> ClinicDefaults,
    IReadOnlyList<DateOnly> Holidays, IReadOnlyList<DateOnly> BookedDates, IReadOnlyList<DateOnly> ConflictDates);
public sealed record AvailabilityImpact(int AffectedAppointments, IReadOnlyList<ulong> AppointmentNumbers,
    IReadOnlyList<DateOnly> AffectedDates);
public sealed record ProfessionalDailyLimits(bool OnlineEnabled, ushort MaxOnlineDaily,
    ushort MaxInPersonDaily, ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfessionalDailyLimitsRequest(bool OnlineEnabled,
    [param: Range(0, 100)] ushort MaxOnlineDaily,
    [param: Range(0, 100)] ushort MaxInPersonDaily,
    [param: Range(1, long.MaxValue)] ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AvailabilityModeChangeRequest(
    [param: Required, RegularExpression("^(recurring|variable)$")] string Mode,
    [param: Range(1, long.MaxValue)] ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VariableDaysWriteRequest(
    [param: MinLength(1), MaxLength(31)] IReadOnlyList<DateOnly> Dates,
    [param: MinLength(1), MaxLength(8)] IReadOnlyList<VariableAvailabilityInterval> Intervals,
    [param: Range(1, long.MaxValue)] ulong RowVersion);

[ApiController]
[Route("api/v1/professionals/{professionalId:long}/availability-plan")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ServiceFilter(typeof(AdministratorStepUpFilter))]
public sealed class ProfessionalAvailabilityPlanController(
    ViverAppDbContext database, IdentityAuditWriter audit, TimeProvider clock) : ControllerBase
{
    private static readonly string[] OpenAppointmentStatuses = ["pending", "confirmed", "arrived", "in_progress"];
    private ulong Actor => ClinicAdministrationSupport.RequireActorId(User);
    private bool CanAccess(ulong professionalId) =>
        User.IsInRole(ViverAppRoles.Administrator)
        || User.IsInRole(ViverAppRoles.Manager)
        || (User.IsInRole(ViverAppRoles.Doctor) || User.IsInRole(ViverAppRoles.Psychologist)) && Actor == professionalId;

    [HttpGet("daily-limits")]
    [ManagerFeatureGate("manager.professional_schedules_enabled")]
    public async Task<ActionResult<ProfessionalDailyLimits>> DailyLimits(ulong professionalId, CancellationToken ct)
    {
        if (!CanAccess(professionalId)) return Forbid();
        var preference = await database.ProfessionalPreferences.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProfessionalAccountId == professionalId, ct);
        return preference is null ? NotFound() : Ok(new ProfessionalDailyLimits(preference.OnlineEnabled,
            preference.MaxOnlineDaily, preference.MaxInPersonDaily, preference.RowVersion));
    }

    [HttpPut("daily-limits")]
    [ManagerFeatureGate("manager.professional_schedules_enabled")]
    public async Task<ActionResult<ProfessionalDailyLimits>> UpdateDailyLimits(ulong professionalId,
        ProfessionalDailyLimitsRequest request, CancellationToken ct)
    {
        if (!CanAccess(professionalId)) return Forbid();
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var preference = await LockedPreference(professionalId, ct);
        if (preference is null) return NotFound();
        if (preference.RowVersion != request.RowVersion)
            return Conflict(new ProblemDetails { Status = 409, Title = "A disponibilidade foi alterada. Recarregue e tente novamente." });
        preference.OnlineEnabled = request.OnlineEnabled;
        preference.MaxOnlineDaily = request.MaxOnlineDaily;
        preference.MaxInPersonDaily = request.MaxInPersonDaily;
        preference.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        preference.RowVersion++;
        await database.SaveChangesAsync(ct);
        await audit.WriteAsync("professional.availability.settings.updated", Actor, "professional_preference",
            professionalId.ToString(), null, ct);
        await transaction.CommitAsync(ct);
        return Ok(new ProfessionalDailyLimits(preference.OnlineEnabled, preference.MaxOnlineDaily,
            preference.MaxInPersonDaily, preference.RowVersion));
    }

    [HttpGet]
    [ManagerFeatureGate("manager.professional_schedules_enabled")]
    public async Task<ActionResult<VariableAvailabilityPlan>> Get(
        ulong professionalId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        if (!CanAccess(professionalId)) return Forbid();
        if (to < from || to.DayNumber - from.DayNumber > 62)
            return Problem(statusCode: 400, title: "Selecione um período de até 63 dias.");
        var preference = await database.ProfessionalPreferences.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProfessionalAccountId == professionalId, ct);
        if (preference is null) return NotFound();
        var first = from.ToDateTime(TimeOnly.MinValue);
        var last = to.ToDateTime(TimeOnly.MinValue);
        var rows = await database.ProfessionalVariableHours.AsNoTracking()
            .Where(x => x.ProfessionalAccountId == professionalId && x.AvailableDate >= first && x.AvailableDate <= last)
            .OrderBy(x => x.AvailableDate).ThenBy(x => x.StartTime).ToListAsync(ct);
        var clinicHours = await database.ClinicWeeklyHours.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.StartTime).ToListAsync(ct);
        var holidays = await database.Holidays.AsNoTracking()
            .Where(x => x.IsAnnual || x.HolidayDate >= first && x.HolidayDate <= last).ToListAsync(ct);
        var defaults = new List<VariableAvailabilityDay>();
        var holidayDates = new List<DateOnly>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (holidays.Any(x => x.HolidayDate.Date == date.ToDateTime(TimeOnly.MinValue)
                || x.IsAnnual && x.HolidayDate.Month == date.Month && x.HolidayDate.Day == date.Day))
                holidayDates.Add(date);
            defaults.Add(new VariableAvailabilityDay(date,
                clinicHours.Where(x => x.DayOfWeek == (byte)date.DayOfWeek)
                    .Select(x => new VariableAvailabilityInterval(TimeOnly.FromTimeSpan(x.StartTime),
                        TimeOnly.FromTimeSpan(x.EndTime), "both")).ToArray()));
        }
        var days = rows.GroupBy(x => DateOnly.FromDateTime(x.AvailableDate))
            .Select(group => new VariableAvailabilityDay(group.Key, group.Select(x => new VariableAvailabilityInterval(
                TimeOnly.FromTimeSpan(x.StartTime), TimeOnly.FromTimeSpan(x.EndTime), x.ModalityCode)).ToArray()))
            .ToArray();
        var zoneName = await database.Clinics.AsNoTracking()
            .Select(x => x.TimezoneName).SingleOrDefaultAsync(ct) ?? "America/Sao_Paulo";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneName);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(first, DateTimeKind.Unspecified), zone);
        var untilUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), zone);
        var appointments = await database.Appointments.AsNoTracking()
            .Where(x => x.ProfessionalAccountId == professionalId && OpenAppointmentStatuses.Contains(x.StatusCode)
                && x.StartsAtUtc >= fromUtc && x.StartsAtUtc < untilUtc)
            .Select(x => x.StartsAtUtc).ToListAsync(ct);
        var bookedDates = appointments.Select(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(x, DateTimeKind.Utc), zone))).Distinct().Order().ToArray();
        var impact = await FindImpact(professionalId, preference.AvailabilityMode, null, null, ct);
        return Ok(new VariableAvailabilityPlan(preference.AvailabilityMode, preference.RowVersion, preference.OnlineEnabled,
            days, defaults, holidayDates, bookedDates,
            impact.AffectedDates.Where(x => x >= from && x <= to).Distinct().Order().ToArray()));
    }

    [HttpGet("impact")]
    [ManagerFeatureGate("manager.professional_schedules_enabled")]
    public async Task<ActionResult<AvailabilityImpact>> Impact(
        ulong professionalId, [FromQuery] string mode, CancellationToken ct)
    {
        if (!CanAccess(professionalId)) return Forbid();
        if (mode is not ("recurring" or "variable"))
            return Problem(statusCode: 400, title: "Modo de disponibilidade inválido.");
        if (!await database.ProfessionalPreferences.AsNoTracking().AnyAsync(x => x.ProfessionalAccountId == professionalId, ct))
            return NotFound();
        return Ok(await FindImpact(professionalId, mode, null, null, ct));
    }

    [HttpPut("mode")]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    [ManagerFeatureGate("manager.professional_schedules_enabled")]
    public async Task<ActionResult<AvailabilityImpact>> ChangeMode(
        ulong professionalId, AvailabilityModeChangeRequest request, CancellationToken ct)
    {
        if (!CanAccess(professionalId)) return Forbid();
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var preference = await LockedPreference(professionalId, ct);
        if (preference is null) return NotFound();
        if (preference.RowVersion != request.RowVersion) return ConflictResult("A disponibilidade mudou em outra sessão. Recarregue os horários.");
        var impact = await FindImpact(professionalId, request.Mode, null, null, ct);
        if (impact.AffectedAppointments > 0)
            return ConflictResult($"A mudança deixaria {impact.AffectedAppointments} atendimento(s) já marcado(s) sem disponibilidade. Ajuste os horários antes de trocar de modo.");
        preference.AvailabilityMode = request.Mode;
        preference.RowVersion++;
        preference.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await audit.WriteAsync("professional.availability.mode_changed", Actor, "professional_preference",
            professionalId.ToString(), new Dictionary<string, string> { ["mode"] = request.Mode }, ct);
        return Ok(impact);
    }

    [HttpPut("variable-days")]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    [ManagerFeatureGate("manager.professional_schedules_enabled")]
    public async Task<ActionResult<VariableAvailabilityPlan>> ReplaceDays(
        ulong professionalId, VariableDaysWriteRequest request, CancellationToken ct)
    {
        if (!CanAccess(professionalId)) return Forbid();
        if (request.Dates is null || request.Intervals is null || request.Dates.Count is < 1 or > 31
            || request.Intervals.Count is < 1 or > 8 || request.Dates.Distinct().Count() != request.Dates.Count)
            return Problem(statusCode: 400, title: "Selecione até 31 datas distintas e até 8 faixas de horário.");
        var timezoneName = await database.Clinics.AsNoTracking()
            .Select(x => x.TimezoneName).SingleOrDefaultAsync(ct) ?? "America/Sao_Paulo";
        var clinicNow = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(timezoneName));
        var today = DateOnly.FromDateTime(clinicNow.DateTime);
        if (request.Dates.Any(x => x < today || x > today.AddDays(730)))
            return Problem(statusCode: 400, title: "Selecione datas de hoje em diante, dentro dos próximos dois anos.");
        var ordered = request.Intervals.OrderBy(x => x.StartsAt).ToArray();
        if (ordered.Any(x => x.StartsAt >= x.EndsAt || x.ModalityCode is not ("both" or "online" or "in_person"))
            || ordered.Zip(ordered.Skip(1)).Any(x => x.First.EndsAt > x.Second.StartsAt))
            return Problem(statusCode: 400, title: "Revise as faixas: horários ou modalidades inválidos, ou faixas sobrepostas.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var preference = await LockedPreference(professionalId, ct);
        if (preference is null) return NotFound();
        if (preference.RowVersion != request.RowVersion) return ConflictResult("A disponibilidade mudou em outra sessão. Recarregue os horários.");
        if (!preference.OnlineEnabled && ordered.Any(x => x.ModalityCode is "online" or "both"))
            return Problem(statusCode: 400, title: "Habilite o atendimento online no perfil antes de criar faixas online.");
        var clinic = await database.ClinicWeeklyHours.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var holidayRows = await database.Holidays.AsNoTracking().ToListAsync(ct);
        foreach (var date in request.Dates)
        {
            var day = date.ToDateTime(TimeOnly.MinValue);
            var dayHolidays = holidayRows.Where(x => x.HolidayDate.Date == day
                || x.IsAnnual && x.HolidayDate.Month == day.Month && x.HolidayDate.Day == day.Day).ToArray();
            foreach (var interval in ordered)
            {
                if (dayHolidays.Any(x => !x.StartTime.HasValue
                    || x.StartTime < interval.EndsAt.ToTimeSpan() && x.EndTime > interval.StartsAt.ToTimeSpan()))
                    return ConflictResult($"Há feriado ou bloqueio no dia {date:dd/MM/yyyy} para a faixa informada.");
                if (interval.ModalityCode is "in_person" or "both"
                    && !clinic.Any(x => x.DayOfWeek == (byte)date.DayOfWeek
                        && x.StartTime <= interval.StartsAt.ToTimeSpan() && x.EndTime >= interval.EndsAt.ToTimeSpan()))
                    return ConflictResult($"A faixa presencial de {date:dd/MM/yyyy} precisa estar dentro do funcionamento da clínica.");
            }
        }
        if (preference.AvailabilityMode == "variable")
        {
            var impact = await FindImpact(professionalId, "variable", request.Dates, ordered, ct);
            if (impact.AffectedAppointments > 0)
                return ConflictResult($"A alteração afetaria {impact.AffectedAppointments} atendimento(s) já marcado(s). Preserve suas faixas ou reagende antes.");
        }
        var dates = request.Dates.Select(x => x.ToDateTime(TimeOnly.MinValue)).ToArray();
        var old = await database.ProfessionalVariableHours
            .Where(x => x.ProfessionalAccountId == professionalId && dates.Contains(x.AvailableDate)).ToListAsync(ct);
        database.ProfessionalVariableHours.RemoveRange(old);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var date in dates)
            foreach (var interval in ordered)
                database.ProfessionalVariableHours.Add(new ProfessionalVariableHour
                {
                    ProfessionalAccountId = professionalId,
                    AvailableDate = date,
                    StartTime = interval.StartsAt.ToTimeSpan(),
                    EndTime = interval.EndsAt.ToTimeSpan(),
                    ModalityCode = interval.ModalityCode,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    RowVersion = 1,
                });
        preference.RowVersion++;
        preference.UpdatedAtUtc = now;
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await audit.WriteAsync("professional.availability.variable_days_updated", Actor, "professional_preference",
            professionalId.ToString(), new Dictionary<string, string> { ["dates"] = string.Join(',', request.Dates), ["intervals"] = ordered.Length.ToString() }, ct);
        return await Get(professionalId, request.Dates.Min(), request.Dates.Max(), ct);
    }

    [HttpDelete("variable-days/{date}")]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    [ManagerFeatureGate("manager.professional_schedules_enabled")]
    public async Task<IActionResult> RemoveDay(ulong professionalId, DateOnly date, [FromQuery] ulong rowVersion, CancellationToken ct)
    {
        if (!CanAccess(professionalId)) return Forbid();
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var preference = await LockedPreference(professionalId, ct);
        if (preference is null) return NotFound();
        if (preference.RowVersion != rowVersion) return ConflictResult("A disponibilidade mudou em outra sessão. Recarregue os horários.");
        if (preference.AvailabilityMode == "variable")
        {
            var impact = await FindImpact(professionalId, "variable", [date], [], ct);
            if (impact.AffectedAppointments > 0)
                return ConflictResult("O dia contém atendimentos futuros. Reagende-os antes de remover a disponibilidade.");
        }
        var day = date.ToDateTime(TimeOnly.MinValue);
        var rows = await database.ProfessionalVariableHours.Where(x => x.ProfessionalAccountId == professionalId
            && x.AvailableDate == day).ToListAsync(ct);
        database.ProfessionalVariableHours.RemoveRange(rows);
        preference.RowVersion++;
        preference.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await audit.WriteAsync("professional.availability.variable_day_removed", Actor, "professional_preference",
            professionalId.ToString(), new Dictionary<string, string> { ["date"] = date.ToString("yyyy-MM-dd") }, ct);
        return NoContent();
    }

    private Task<ProfessionalPreference?> LockedPreference(ulong professionalId, CancellationToken ct) =>
        database.ProfessionalPreferences.FromSqlInterpolated($"SELECT * FROM professional_preferences WHERE professional_account_id = {professionalId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    private async Task<AvailabilityImpact> FindImpact(ulong professionalId, string mode,
        IReadOnlyList<DateOnly>? replacedDates, IReadOnlyList<VariableAvailabilityInterval>? replacement, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var bookings = await database.Appointments.AsNoTracking()
            .Where(x => x.ProfessionalAccountId == professionalId && OpenAppointmentStatuses.Contains(x.StatusCode)
                && x.StartsAtUtc >= now).Select(x => new { x.AppointmentNumber, x.StartsAtUtc, x.EndsAtUtc, x.ModalityCode })
            .ToListAsync(ct);
        if (bookings.Count == 0) return new(0, [], []);
        var zoneName = await database.Clinics.AsNoTracking().Select(x => x.TimezoneName).SingleOrDefaultAsync(ct)
            ?? "America/Sao_Paulo";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneName);
        var weekly = mode == "recurring" ? await database.ProfessionalWeeklyHours.AsNoTracking()
            .Where(x => x.ProfessionalAccountId == professionalId && x.IsActive).ToListAsync(ct) : [];
        var variable = mode == "variable" ? await database.ProfessionalVariableHours.AsNoTracking()
            .Where(x => x.ProfessionalAccountId == professionalId).ToListAsync(ct) : [];
        var exceptions = await database.ProfessionalAvailabilityExceptions.AsNoTracking()
            .Where(x => x.ProfessionalAccountId == professionalId && x.IsAvailable).ToListAsync(ct);
        var affected = new List<ulong>();
        var affectedDates = new List<DateOnly>();
        foreach (var booking in bookings)
        {
            var start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(booking.StartsAtUtc, DateTimeKind.Utc), zone);
            var end = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(booking.EndsAtUtc, DateTimeKind.Utc), zone);
            var date = DateOnly.FromDateTime(start);
            var overrideWindows = exceptions.Where(x => x.ExceptionDate.Date == start.Date
                && Compatible(x.ModalityCode, booking.ModalityCode)).ToArray();
            var covered = overrideWindows.Length > 0
                ? overrideWindows.Any(x => x.StartTime <= start.TimeOfDay && x.EndTime >= end.TimeOfDay)
                : mode == "recurring"
                ? weekly.Any(x => x.DayOfWeek == (byte)date.DayOfWeek
                    && (!x.ValidFrom.HasValue || x.ValidFrom.Value.Date <= start.Date)
                    && (!x.ValidUntil.HasValue || x.ValidUntil.Value.Date >= start.Date)
                    && Compatible(x.ModalityCode, booking.ModalityCode)
                    && x.StartTime <= start.TimeOfDay && x.EndTime >= end.TimeOfDay)
                : replacedDates is not null && replacedDates.Contains(date)
                    ? replacement!.Any(x => Compatible(x.ModalityCode, booking.ModalityCode)
                        && x.StartsAt <= TimeOnly.FromDateTime(start) && x.EndsAt >= TimeOnly.FromDateTime(end))
                    : variable.Any(x => x.AvailableDate.Date == start.Date
                        && Compatible(x.ModalityCode, booking.ModalityCode)
                        && x.StartTime <= start.TimeOfDay && x.EndTime >= end.TimeOfDay);
            if (!covered)
            {
                affected.Add(booking.AppointmentNumber);
                affectedDates.Add(date);
            }
        }
        return new(affected.Count, affected.Take(25).ToArray(), affectedDates.Distinct().Order().ToArray());
    }

    private static bool Compatible(string configured, string booked) => configured == "both" || configured == booked;
    private ObjectResult ConflictResult(string title) => Problem(statusCode: StatusCodes.Status409Conflict, title: title);
}
