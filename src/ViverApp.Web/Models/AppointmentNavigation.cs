using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace ViverApp.Web;

// UI context only, never an authorization source or a container for patient data.
public sealed record AppointmentNavigationContext(string Root, string View, DateOnly Date,
    ulong ProfessionalId, string? Time = null);

public static class AppointmentNavigation
{
    public static DateTime WeekStart(DateTime day) => day.Date.AddDays(-(int)day.DayOfWeek);
    public static DateTime WeekEnd(DateTime day) => WeekStart(day).AddDays(7);
    public static string State(AppointmentNavigationContext context) => JsonSerializer.Serialize(context);

    public static string QueryFallback(string path, AppointmentNavigationContext context)
    {
        var agenda = $"{context.Root}/agenda?view={context.View}&date={context.Date:yyyy-MM-dd}&professional={context.ProfessionalId}";
        if (path == context.Root + "/agenda") return agenda;
        if (path == context.Root + "/agendar")
            return $"{path}?date={context.Date:yyyy-MM-dd}&time={context.Time}&professional={context.ProfessionalId}&returnTo={Uri.EscapeDataString(agenda)}";
        return $"{path}?returnTo={Uri.EscapeDataString(agenda)}";
    }

    public static AppointmentNavigationContext? Restore(string root, string? state)
    {
        if (string.IsNullOrWhiteSpace(state) || state.Length > 1000) return null;
        try
        {
            var value = JsonSerializer.Deserialize<AppointmentNavigationContext>(state);
            return value is not null && value.Root == root
                && value.View is "day" or "week" or "month" or "year"
                && value.Date != default
                && (value.Time is null || TimeOnly.TryParseExact(value.Time, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)) ? value : null;
        }
        catch (JsonException) { return null; }
    }

    public static AppointmentNavigationContext? LegacyContext(string root, string? returnTo)
    {
        var safe = AgendaReturn(root, returnTo);
        if (!safe.Contains('?')) return null;
        var values = QueryHelpers.ParseQuery(safe[safe.IndexOf('?')..]);
        if (!DateOnly.TryParseExact(values["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)) return null;
        var view = values["view"].ToString();
        if (view is not ("day" or "week" or "month" or "year")) return null;
        ulong.TryParse(values["professional"], out var professional);
        return new(root, view, date, professional);
    }
    public static string AgendaReturn(string root, string? returnTo)
    {
        // Only an agenda in the same access area; never accept external redirects.
        if (returnTo is not null && returnTo.StartsWith(root + "/agenda?", StringComparison.Ordinal)
            && !returnTo.Contains('\\') && !returnTo.Any(char.IsControl)
            && returnTo.Length <= 500) return returnTo;
        return root + "/agenda";
    }
}
