using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.Notifications;

public sealed record NotificationPreferencesResponse(
    bool ReminderEmailEnabled, bool ReminderSmsEnabled,
    bool PremiumUpdatesEnabled, ulong RowVersion);

public sealed record NotificationPreferencesUpdateRequest(
    bool ReminderEmailEnabled, bool ReminderSmsEnabled,
    bool PremiumUpdatesEnabled, ulong RowVersion);

internal sealed class NotificationRuleException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

internal sealed class NotificationRuleFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not NotificationRuleException error) return;
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = error.StatusCode,
            Title = error.Message,
        }) { StatusCode = error.StatusCode };
        context.ExceptionHandled = true;
    }
}

public sealed class NotificationPreferencesService(ViverAppDbContext database, TimeProvider clock)
{
    public async Task<NotificationPreferencesResponse> GetAsync(ulong accountId, CancellationToken cancellationToken)
    {
        var preference = await database.NotificationPreferences.AsNoTracking()
            .SingleOrDefaultAsync(item => item.AccountId == accountId, cancellationToken);
        return Map(preference);
    }

    public async Task<NotificationPreferencesResponse> UpdateAsync(
        ulong accountId, NotificationPreferencesUpdateRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var preference = await database.NotificationPreferences
            .FromSqlInterpolated($"SELECT * FROM notification_preferences WHERE account_id={accountId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (preference is null)
        {
            if (request.RowVersion != 0)
                throw new NotificationRuleException(409, "As preferências foram alteradas. Atualize a página.");
            preference = new NotificationPreference { AccountId = accountId, RowVersion = 1 };
            database.NotificationPreferences.Add(preference);
        }
        else
        {
            if (preference.RowVersion != request.RowVersion)
                throw new NotificationRuleException(409, "As preferências foram alteradas. Atualize a página.");
            preference.RowVersion++;
        }
        preference.ReminderEmailEnabled = request.ReminderEmailEnabled;
        preference.ReminderSmsEnabled = request.ReminderSmsEnabled;
        preference.PremiumUpdatesEnabled = request.PremiumUpdatesEnabled;
        preference.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(preference);
    }

    private static NotificationPreferencesResponse Map(NotificationPreference? preference) =>
        preference is null
            ? new NotificationPreferencesResponse(true, false, true, 0)
            : new NotificationPreferencesResponse(preference.ReminderEmailEnabled ?? true,
                preference.ReminderSmsEnabled, preference.PremiumUpdatesEnabled ?? true,
                preference.RowVersion);
}

[ApiController]
[Route("api/v1/notifications/preferences")]
[Authorize]
[ServiceFilter(typeof(NotificationRuleFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationPreferencesController(NotificationPreferencesService preferences) : ControllerBase
{
    [HttpGet]
    public Task<NotificationPreferencesResponse> Get(CancellationToken cancellationToken) =>
        preferences.GetAsync(ActorId, cancellationToken);

    [HttpPut, EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<NotificationPreferencesResponse> Update(
        NotificationPreferencesUpdateRequest request, CancellationToken cancellationToken) =>
        preferences.UpdateAsync(ActorId, request, cancellationToken);

    private ulong ActorId => ulong.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
}
