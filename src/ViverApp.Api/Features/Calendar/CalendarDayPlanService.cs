using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;

namespace ViverApp.Api.Features.Calendar;

public sealed record CalendarWindowResponse(TimeSpan Start, TimeSpan End);
public sealed record CalendarDayPlanResponse(int IntervalMinutes, IReadOnlyList<CalendarWindowResponse> Windows,
    IReadOnlyList<DateOnly> UnavailableDays);

public sealed class CalendarDayPlanService(ViverAppDbContext database)
{
    public async Task<CalendarDayPlanResponse> GetAsync(ulong actor, string role, DateOnly date, ulong? requestedProfessional, CancellationToken ct)
    {
        if (date.Year is < 1900 or > 9998) throw new CalendarRuleException(400, "A data é inválida.");
        var professional = role switch
        {
            ViverAppRoles.Doctor or ViverAppRoles.Psychologist when requestedProfessional is null || requestedProfessional == actor => actor,
            ViverAppRoles.Manager or ViverAppRoles.Administrator when requestedProfessional.HasValue => requestedProfessional.Value,
            _ => throw new CalendarRuleException(403, "Selecione um profissional permitido."),
        };
        var active = await database.Accounts.AsNoTracking().AnyAsync(a => a.Id == professional && a.StatusCode == "active"
            && (a.RoleCode == ViverAppRoles.Doctor || a.RoleCode == ViverAppRoles.Psychologist), ct);
        if (!active) throw new CalendarRuleException(404, "Profissional não encontrado.");

        var first = new DateOnly(date.Year, date.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var from = first.ToDateTime(TimeOnly.MinValue);
        var through = last.ToDateTime(TimeOnly.MinValue);
        var clinic = await database.ClinicWeeklyHours.AsNoTracking().Where(h => h.IsActive).ToArrayAsync(ct);
        var preference = await database.ProfessionalPreferences.AsNoTracking()
            .Where(p => p.ProfessionalAccountId == professional).Select(p => p.AvailabilityMode).SingleOrDefaultAsync(ct);
        var weekly = preference == "variable" ? [] : await database.ProfessionalWeeklyHours.AsNoTracking()
            .Where(h => h.ProfessionalAccountId == professional && h.IsActive
                && (!h.ValidFrom.HasValue || h.ValidFrom <= through)
                && (!h.ValidUntil.HasValue || h.ValidUntil >= from)).ToArrayAsync(ct);
        var variable = preference != "variable" ? [] : await database.ProfessionalVariableHours.AsNoTracking()
            .Where(h => h.ProfessionalAccountId == professional && h.AvailableDate >= from && h.AvailableDate <= through).ToArrayAsync(ct);
        var exceptions = await database.ProfessionalAvailabilityExceptions.AsNoTracking()
            .Where(h => h.ProfessionalAccountId == professional && h.ExceptionDate >= from && h.ExceptionDate <= through).ToArrayAsync(ct);
        var holidays = await database.Holidays.AsNoTracking()
            .Where(h => h.IsAnnual || h.HolidayDate >= from && h.HolidayDate <= through).ToArrayAsync(ct);
        var rawInterval = await database.ApplicationSettings.AsNoTracking().Where(s => s.SettingKey == "appointments.slot_interval_minutes")
            .Select(s => s.ValueJson).SingleOrDefaultAsync(ct);
        var interval = int.TryParse(rawInterval?.Trim('"'), out var number) && number is >= 5 and <= 120 ? number : 10;
        var unavailable = new List<DateOnly>();
        IReadOnlyList<CalendarWindowResponse> chosen = [];
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var clinicWindows = clinic.Where(h => h.DayOfWeek == (byte)day.DayOfWeek)
                .Select(h => new CalendarWindowResponse(h.StartTime, h.EndTime)).ToArray();
            var dayDate = day.ToDateTime(TimeOnly.MinValue).Date;
            var professionalWindows = new List<CalendarWindowResponse>();
            foreach (var modality in new[] { "in_person", "online" })
            {
                var dayExceptions = exceptions.Where(h => h.ExceptionDate.Date == dayDate
                    && (h.ModalityCode == "both" || h.ModalityCode == modality)).ToArray();
                if (dayExceptions.Any(h => !h.IsAvailable)) continue;
                var current = dayExceptions.Any(h => h.IsAvailable)
                    ? dayExceptions.Where(h => h.IsAvailable && h.StartTime.HasValue && h.EndTime.HasValue)
                        .Select(h => new CalendarWindowResponse(h.StartTime!.Value, h.EndTime!.Value)).ToArray()
                    : preference == "variable"
                        ? variable.Where(h => h.AvailableDate.Date == dayDate
                            && (h.ModalityCode == "both" || h.ModalityCode == modality))
                            .Select(h => new CalendarWindowResponse(h.StartTime, h.EndTime)).ToArray()
                        : weekly.Where(h => h.DayOfWeek == (byte)day.DayOfWeek
                            && (h.ModalityCode == "both" || h.ModalityCode == modality)
                            && (!h.ValidFrom.HasValue || h.ValidFrom.Value.Date <= dayDate)
                            && (!h.ValidUntil.HasValue || h.ValidUntil.Value.Date >= dayDate))
                            .Select(h => new CalendarWindowResponse(h.StartTime, h.EndTime)).ToArray();
                professionalWindows.AddRange(current);
            }
            var windows = (from c in clinicWindows
                           from p in professionalWindows
                           let start = c.Start > p.Start ? c.Start : p.Start
                           let end = c.End < p.End ? c.End : p.End
                           where end > start
                           select new CalendarWindowResponse(start, end)).ToList();
            foreach (var holiday in holidays.Where(h => h.HolidayDate.Date == day.ToDateTime(TimeOnly.MinValue).Date
                || h.IsAnnual && h.HolidayDate.Month == day.Month && h.HolidayDate.Day == day.Day))
            {
                var blockStart = holiday.StartTime ?? TimeSpan.Zero;
                var blockEnd = holiday.EndTime ?? TimeSpan.FromDays(1);
                windows = windows.SelectMany(w => new[]
                    {
                        new CalendarWindowResponse(w.Start, blockStart < w.End ? blockStart : w.End),
                        new CalendarWindowResponse(blockEnd > w.Start ? blockEnd : w.Start, w.End),
                    }).Where(w => w.End > w.Start).ToList();
            }
            var merged = new List<CalendarWindowResponse>();
            foreach (var window in windows.OrderBy(w => w.Start).ThenBy(w => w.End))
            {
                if (merged.Count == 0 || window.Start > merged[^1].End) merged.Add(window);
                else if (window.End > merged[^1].End) merged[^1] = merged[^1] with { End = window.End };
            }
            if (merged.Count == 0) unavailable.Add(day);
            if (day == date) chosen = merged;
        }
        return new CalendarDayPlanResponse(interval, chosen, unavailable);
    }
}
