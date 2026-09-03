using ViverApp.Api.Features.PatientScheduling;
using Xunit;

namespace ViverApp.PatientScheduling.Tests;

public sealed class PatientSchedulingPolicyTests
{
    private static readonly TimeZoneInfo Timezone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    [Theory]
    [InlineData(10, 20, 20, 30, false)]
    [InlineData(10, 21, 20, 30, true)]
    [InlineData(20, 30, 10, 21, true)]
    [InlineData(10, 30, 15, 20, true)]
    public void Overlap_UsesHalfOpenIntervals(int firstStart, int firstEnd, int secondStart, int secondEnd, bool expected)
    {
        var day = new DateTime(2026, 9, 10);

        var overlaps = PatientSchedulingPolicy.Overlaps(
            day.AddMinutes(firstStart),
            day.AddMinutes(firstEnd),
            day.AddMinutes(secondStart),
            day.AddMinutes(secondEnd));

        Assert.Equal(expected, overlaps);
    }

    [Fact]
    public void Slots_IntersectClinicHoursAndRemoveHolidayAndBusyPeriods()
    {
        var date = new DateOnly(2026, 9, 10);
        var busyStart = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Unspecified),
            Timezone);

        var slots = PatientSchedulingPolicy.BuildDaySlots(
            date,
            durationMinutes: 30,
            intervalMinutes: 30,
            "America/Sao_Paulo",
            Timezone,
            [new(TimeSpan.FromHours(8), TimeSpan.FromHours(12))],
            [new(TimeSpan.FromHours(9), TimeSpan.FromHours(11))],
            [new(TimeSpan.FromHours(9.5), TimeSpan.FromHours(10))],
            [new(busyStart, busyStart.AddMinutes(30))],
            DateTime.MinValue,
            requiresClinic: true);

        Assert.Equal([new TimeOnly(9, 0), new TimeOnly(10, 30)], slots.Select(item => item.StartsAt));
    }

    [Fact]
    public void Slots_RejectNonexistentDaylightSavingLocalTime()
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        var slots = PatientSchedulingPolicy.BuildDaySlots(
            new DateOnly(2026, 3, 8),
            durationMinutes: 30,
            intervalMinutes: 30,
            "America/New_York",
            timezone,
            [new(TimeSpan.FromHours(2), TimeSpan.FromHours(3))],
            [],
            [],
            [],
            DateTime.MinValue,
            requiresClinic: false);

        Assert.Empty(slots);
    }
}
