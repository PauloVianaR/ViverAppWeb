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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppointmentViewPreferenceRequest(
    [param: Required, RegularExpression("^(cards|compact)$")] string Mode);

[ApiController]
[Route("api/v1/me/preferences")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UserPreferencesController(ViverAppDbContext database, TimeProvider clock) : ControllerBase
{
    private ulong Actor => ulong.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!,
        CultureInfo.InvariantCulture);

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
            INSERT INTO account_ui_preferences (account_id, appointment_view_mode, updated_at_utc, row_version)
            VALUES ({Actor}, {request.Mode}, {now}, 1)
            ON DUPLICATE KEY UPDATE
                appointment_view_mode = VALUES(appointment_view_mode),
                updated_at_utc = VALUES(updated_at_utc),
                row_version = row_version + 1
            """, ct);
        var preference = await database.AccountUiPreferences.AsNoTracking()
            .SingleAsync(item => item.AccountId == Actor, ct);
        return new(preference.AppointmentViewMode, preference.RowVersion);
    }
}
