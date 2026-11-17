using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;

namespace ViverApp.Api.Features.ClinicalOperations;

public static class AppointmentNoShowPolicy
{
    public const string AllowPendingKey = "appointments.allow_pending_no_show";
    public static async Task<bool> AllowsPendingAsync(ViverAppDbContext database, CancellationToken ct) =>
        (await database.ApplicationSettings.AsNoTracking().Where(s => s.SettingKey == AllowPendingKey)
            .Select(s => s.ValueJson).SingleOrDefaultAsync(ct))?.Trim() == "true";
    public static bool CanMark(string status, DateTime startsAt, DateTime now, bool allowPending) =>
        startsAt <= now && (status is "confirmed" or "arrived" || status == "pending" && allowPending);
}
