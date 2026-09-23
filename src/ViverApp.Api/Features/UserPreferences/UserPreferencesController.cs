using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Security;

namespace ViverApp.Api.Features.UserPreferences;

public sealed record AppointmentViewPreferenceResponse(string Mode, ulong RowVersion);
public sealed record CalendarViewPreferenceResponse(string Mode, ulong RowVersion);
public sealed record DesktopSidebarPreferenceResponse(bool Enabled, bool Collapsed);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppointmentViewPreferenceRequest(
    [param: Required, RegularExpression("^(cards|compact)$")] string Mode);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CalendarViewPreferenceRequest(
    [param: Required, RegularExpression("^(day|week|month|year)$")] string Mode);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopSidebarPreferenceRequest(bool Collapsed);

[ApiController]
[Route("api/v1/me/preferences")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UserPreferencesController(ViverAppDbContext database, TimeProvider clock) : ControllerBase
{
    private ulong Actor => ulong.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!,
        CultureInfo.InvariantCulture);

    [HttpGet("desktop-sidebar")]
    public async Task<DesktopSidebarPreferenceResponse> GetDesktopSidebar(CancellationToken ct)
    {
        var setting = await database.ApplicationSettings.AsNoTracking()
            .Where(item => item.SettingKey == "web.desktop_sidebar_enabled")
            .Select(item => item.ValueJson).SingleOrDefaultAsync(ct);
        var enabled = setting is null || string.Equals(setting.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        var collapsed = await database.AccountUiPreferences.AsNoTracking()
            .Where(item => item.AccountId == Actor).Select(item => (bool?)item.DesktopSidebarCollapsed)
            .SingleOrDefaultAsync(ct) ?? false;
        return new(enabled, collapsed);
    }

    [HttpPut("desktop-sidebar")]
    [EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<DesktopSidebarPreferenceResponse> UpdateDesktopSidebar(DesktopSidebarPreferenceRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO account_ui_preferences
                (account_id, appointment_view_mode, calendar_view_mode, desktop_sidebar_collapsed, updated_at_utc, row_version)
            VALUES ({Actor}, {"cards"}, {"month"}, {request.Collapsed}, {now}, 1)
            ON DUPLICATE KEY UPDATE
                desktop_sidebar_collapsed = VALUES(desktop_sidebar_collapsed),
                updated_at_utc = VALUES(updated_at_utc),
                row_version = row_version + 1
            """, ct);
        return await GetDesktopSidebar(ct);
    }

    [HttpGet("appointment-view")]
    public async Task<AppointmentViewPreferenceResponse> GetAppointmentView(CancellationToken ct)
    {
        var preference = await database.AccountUiPreferences.AsNoTracking()
            .SingleOrDefaultAsync(item => item.AccountId == Actor, ct);
        return preference is null
            ? new AppointmentViewPreferenceResponse("cards", 0)
            : new AppointmentViewPreferenceResponse(preference.AppointmentViewMode, preference.RowVersion);
    }

    [HttpPut("appointment-view")]
    [EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<AppointmentViewPreferenceResponse> UpdateAppointmentView(
        AppointmentViewPreferenceRequest request,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO account_ui_preferences (account_id, appointment_view_mode, calendar_view_mode, updated_at_utc, row_version)
            VALUES ({Actor}, {request.Mode}, {"month"}, {now}, 1)
            ON DUPLICATE KEY UPDATE
                appointment_view_mode = VALUES(appointment_view_mode),
                updated_at_utc = VALUES(updated_at_utc),
                row_version = row_version + 1
            """, ct);
        var preference = await database.AccountUiPreferences.AsNoTracking()
            .SingleAsync(item => item.AccountId == Actor, ct);
        return new(preference.AppointmentViewMode, preference.RowVersion);
    }

    [HttpGet("calendar-view")]
    public async Task<CalendarViewPreferenceResponse> GetCalendarView(CancellationToken ct)
    {
        var preference = await database.AccountUiPreferences.AsNoTracking()
            .SingleOrDefaultAsync(item => item.AccountId == Actor, ct);
        return preference is null
            ? new CalendarViewPreferenceResponse("month", 0)
            : new CalendarViewPreferenceResponse(preference.CalendarViewMode, preference.RowVersion);
    }

    [HttpPut("calendar-view")]
    [EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<CalendarViewPreferenceResponse> UpdateCalendarView(CalendarViewPreferenceRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO account_ui_preferences (account_id, appointment_view_mode, calendar_view_mode, updated_at_utc, row_version)
            VALUES ({Actor}, {"cards"}, {request.Mode}, {now}, 1)
            ON DUPLICATE KEY UPDATE
                calendar_view_mode = VALUES(calendar_view_mode),
                updated_at_utc = VALUES(updated_at_utc),
                row_version = row_version + 1
            """, ct);
        var preference = await database.AccountUiPreferences.AsNoTracking().SingleAsync(item => item.AccountId == Actor, ct);
        return new(preference.CalendarViewMode, preference.RowVersion);
    }
}
