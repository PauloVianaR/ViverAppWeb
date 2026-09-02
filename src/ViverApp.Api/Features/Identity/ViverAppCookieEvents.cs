using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;

namespace ViverApp.Api.Features.Identity;

public sealed class ViverAppCookieEvents(ViverAppDbContext database) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var accountIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var sessionIdValue = context.Principal?.FindFirstValue(ViverAppClaimTypes.SessionId);
        var securityStamp = context.Principal?.FindFirstValue(
            new ClaimsIdentityOptions().SecurityStampClaimType);

        if (!ulong.TryParse(accountIdValue, out var accountId)
            || !Guid.TryParse(sessionIdValue, out var sessionId)
            || string.IsNullOrWhiteSpace(securityStamp))
        {
            await RejectAsync(context);
            return;
        }

        var sessionBytes = sessionId.ToByteArray();
        var now = DateTime.UtcNow;
        var record = await database.AuthSessions
            .AsNoTracking()
            .Where(item => item.Id.SequenceEqual(sessionBytes) && item.AccountId == accountId)
            .Select(item => new
            {
                item.ExpiresAtUtc,
                item.RevokedAtUtc,
                item.MfaSatisfied,
                item.LastSeenAtUtc,
                AccountStatus = item.Account.StatusCode,
                item.Account.SecurityStamp,
                item.Account.RoleCode,
            })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        var stampBytes = record is null ? [] : Encoding.UTF8.GetBytes(Convert.ToHexString(record.SecurityStamp));
        var claimBytes = Encoding.UTF8.GetBytes(securityStamp);
        var stampMatches = stampBytes.Length == claimBytes.Length
            && CryptographicOperations.FixedTimeEquals(stampBytes, claimBytes);
        var mfaClaim = context.Principal?.FindFirstValue(ViverAppClaimTypes.MfaSatisfied);
        var mfaMatches = bool.TryParse(mfaClaim, out var claimMfa) && claimMfa == record?.MfaSatisfied;
        var administratorAuthorized = record?.RoleCode != ViverAppRoles.Administrator
            || record.MfaSatisfied
            || !context.Principal!.IsInRole(ViverAppRoles.Administrator);

        if (record is null
            || record.RevokedAtUtc is not null
            || record.ExpiresAtUtc <= now
            || record.AccountStatus != "active"
            || !stampMatches
            || !mfaMatches
            || !administratorAuthorized)
        {
            await RejectAsync(context);
            return;
        }

        if (record.LastSeenAtUtc is null || record.LastSeenAtUtc < now.AddMinutes(-5))
        {
            await database.AuthSessions
                .Where(item => item.Id.SequenceEqual(sessionBytes))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.LastSeenAtUtc, now),
                    context.HttpContext.RequestAborted);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}
