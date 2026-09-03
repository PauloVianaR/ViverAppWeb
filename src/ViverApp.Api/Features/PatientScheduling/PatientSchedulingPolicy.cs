namespace ViverApp.Api.Features.PatientScheduling;

internal sealed record SchedulingPolicy(
    int BookingHorizonDays,
    int MinimumLeadMinutes,
    int CancellationCutoffHours,
    int RescheduleCutoffHours,
    int SlotIntervalMinutes);

internal sealed record LocalAvailabilityWindow(TimeSpan StartsAt, TimeSpan EndsAt);

internal sealed record BusyPeriod(DateTime StartsAtUtc, DateTime EndsAtUtc);

internal static class PatientSchedulingPolicy
{
    public static bool Overlaps(DateTime firstStart, DateTime firstEnd, DateTime secondStart, DateTime secondEnd) =>
        firstStart < secondEnd && secondStart < firstEnd;

    public static IReadOnlyList<AvailableSlotResponse> BuildDaySlots(
        DateOnly date,
        int durationMinutes,
        int intervalMinutes,
        string timezoneName,
        TimeZoneInfo timezone,
        IEnumerable<LocalAvailabilityWindow> doctorWindows,
        IEnumerable<LocalAvailabilityWindow> clinicWindows,
        IEnumerable<LocalAvailabilityWindow> holidayBlocks,
        IEnumerable<BusyPeriod> busyPeriods,
        DateTime earliestStartUtc,
        bool requiresClinic)
    {
        var effectiveWindows = requiresClinic
            ? IntersectWindows(doctorWindows, clinicWindows)
            : doctorWindows.OrderBy(item => item.StartsAt).ToArray();
        var blocked = holidayBlocks.ToArray();
        var busy = busyPeriods.ToArray();
        var duration = TimeSpan.FromMinutes(durationMinutes);
        var step = TimeSpan.FromMinutes(intervalMinutes);
        var result = new List<AvailableSlotResponse>();

        foreach (var window in effectiveWindows)
        {
            for (var cursor = window.StartsAt; cursor + duration <= window.EndsAt; cursor += step)
            {
                var localStart = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.FromTimeSpan(cursor)), DateTimeKind.Unspecified);
                var localEnd = localStart.Add(duration);
                if (timezone.IsInvalidTime(localStart)
                    || timezone.IsInvalidTime(localEnd)
                    || timezone.IsAmbiguousTime(localStart)
                    || timezone.IsAmbiguousTime(localEnd))
                {
                    continue;
                }

                var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, timezone);
                var utcEnd = TimeZoneInfo.ConvertTimeToUtc(localEnd, timezone);
                if (utcStart < earliestStartUtc
                    || blocked.Any(item => Overlaps(cursor, cursor + duration, item.StartsAt, item.EndsAt))
                    || busy.Any(item => Overlaps(utcStart, utcEnd, item.StartsAtUtc, item.EndsAtUtc)))
                {
                    continue;
                }

                result.Add(new AvailableSlotResponse(
                    date,
                    TimeOnly.FromTimeSpan(cursor),
                    ToTimeOnly(cursor + duration),
                    utcStart,
                    utcEnd,
                    timezoneName));
            }
        }

        return result
            .DistinctBy(item => item.StartsAtUtc)
            .OrderBy(item => item.StartsAtUtc)
            .ToArray();
    }

    private static IReadOnlyList<LocalAvailabilityWindow> IntersectWindows(
        IEnumerable<LocalAvailabilityWindow> first,
        IEnumerable<LocalAvailabilityWindow> second)
    {
        var result = new List<LocalAvailabilityWindow>();
        foreach (var left in first)
        {
            foreach (var right in second)
            {
                var start = left.StartsAt > right.StartsAt ? left.StartsAt : right.StartsAt;
                var end = left.EndsAt < right.EndsAt ? left.EndsAt : right.EndsAt;
                if (end > start)
                {
                    result.Add(new LocalAvailabilityWindow(start, end));
                }
            }
        }

        return result.OrderBy(item => item.StartsAt).ToArray();
    }

    private static bool Overlaps(TimeSpan firstStart, TimeSpan firstEnd, TimeSpan secondStart, TimeSpan secondEnd) =>
        firstStart < secondEnd && secondStart < firstEnd;

    private static TimeOnly ToTimeOnly(TimeSpan value) =>
        TimeOnly.FromTimeSpan(TimeSpan.FromTicks(value.Ticks % TimeSpan.TicksPerDay));
}
