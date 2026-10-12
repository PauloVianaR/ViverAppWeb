namespace ViverApp.Web;

public static class AppointmentNavigation
{
    public static DateTime WeekStart(DateTime day) => day.Date.AddDays(-(int)day.DayOfWeek);
    public static DateTime WeekEnd(DateTime day) => WeekStart(day).AddDays(7);
    public static string AgendaReturn(string root, string? returnTo)
    {
        // Only an agenda in the same access area; never accept external redirects.
        if (returnTo is not null && returnTo.StartsWith(root + "/agenda?", StringComparison.Ordinal)
            && !returnTo.Contains('\\') && !returnTo.Any(char.IsControl)
            && returnTo.Length <= 500) return returnTo;
        return root + "/agenda";
    }
}
