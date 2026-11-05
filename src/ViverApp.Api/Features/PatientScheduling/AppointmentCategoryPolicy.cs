using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.PatientScheduling;

public static class AppointmentCategoryPolicy
{
    public static readonly string[] Codes = ["consultation", "examination", "surgery", "procedure"];

    // Existing snapshots are authoritative; legacy rows without items retain their catalog fallback.
    public static IQueryable<Appointment> Filter(IQueryable<Appointment> query, string? category) =>
        string.IsNullOrEmpty(category) ? query : query.Where(a =>
            a.AppointmentServiceItems.Any(s => s.CategoryCode == category)
            || !a.AppointmentServiceItems.Any() && a.AppointmentType.CategoryCode == category);

    public static string[] Categories(IEnumerable<string> snapshots, string fallback)
    {
        var selected = snapshots.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0) selected.Add(fallback);
        return Codes.Where(selected.Contains).ToArray();
    }
}
