using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Identity;

public sealed class ViverAppSessionService(
    ViverAppDbContext database,
    IUserClaimsPrincipalFactory<ViverAppUser> principalFactory,
    IHttpContextAccessor httpContextAccessor,
    IdentitySecurityOptions securityOptions)
{
    public async Task SignInAsync(
        ViverAppUser user,
        string authenticationMethod,
        bool mfaSatisfied,
        bool persistent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationMethod);
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Não há contexto HTTP para criar a sessão.");
        var now = DateTime.UtcNow;
        var administrator = user.RoleCode == ViverAppRoles.Administrator;
        var expires = now.Add(administrator
            ? TimeSpan.FromHours(2)
            : persistent ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8));
        var sessionId = Guid.NewGuid();
        var session = new AuthSession
        {
            Id = sessionId.ToByteArray(),
            AccountId = user.Id,
            RefreshTokenHash = RandomNumberGenerator.GetBytes(32),
            AuthenticationMethod = authenticationMethod,
            MfaSatisfied = mfaSatisfied,
            CreatedAtUtc = now,
            ExpiresAtUtc = expires,
            IpAddressHash = HashOptional(context.Connection.RemoteIpAddress?.GetAddressBytes()),
            UserAgentHash = HashOptional(
                Encoding.UTF8.GetBytes(context.Request.Headers.UserAgent.ToString())),
        };

        database.AuthSessions.Add(session);
        await database.SaveChangesAsync(cancellationToken);
        await database.Accounts
            .Where(item => item.Id == user.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.LastLoginAtUtc, now),
                cancellationToken);

        var principal = await principalFactory.CreateAsync(user);
        var identity = principal.Identity as ClaimsIdentity
            ?? throw new InvalidOperationException("O principal de identidade não possui ClaimsIdentity.");
        identity.AddClaim(new Claim(ViverAppClaimTypes.SessionId, sessionId.ToString("D")));
        identity.AddClaim(new Claim(ViverAppClaimTypes.AuthenticationMethod, authenticationMethod));
        identity.AddClaim(new Claim(
            ViverAppClaimTypes.MfaSatisfied,
            mfaSatisfied ? bool.TrueString : bool.FalseString));

        if (!mfaSatisfied)
        {
            foreach (var roleClaim in identity.FindAll(identity.RoleClaimType).ToArray())
            {
                identity.RemoveClaim(roleClaim);
            }
        }

        try
        {
            await context.SignInAsync(
                IdentityConstants.ApplicationScheme,
                principal,
                new AuthenticationProperties
                {
                    AllowRefresh = false,
                    ExpiresUtc = new DateTimeOffset(expires, TimeSpan.Zero),
                    IsPersistent = persistent && !administrator,
                    IssuedUtc = new DateTimeOffset(now, TimeSpan.Zero),
                });
        }
        catch
        {
            session.RevokedAtUtc = DateTime.UtcNow;
            session.RevokeReasonCode = "sign_in_failed";
            await database.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task RevokeCurrentAsync(string reason, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        var sessionIdValue = context?.User.FindFirstValue(ViverAppClaimTypes.SessionId);
        if (Guid.TryParse(sessionIdValue, out var sessionId))
        {
            var bytes = sessionId.ToByteArray();
            var session = await database.AuthSessions.SingleOrDefaultAsync(
                item => item.Id.SequenceEqual(bytes),
                cancellationToken);
            if (session is not null && session.RevokedAtUtc is null)
            {
                session.RevokedAtUtc = DateTime.UtcNow;
                session.RevokeReasonCode = reason;
                await database.SaveChangesAsync(cancellationToken);
            }
        }

        if (context is not null)
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }

    public async Task RevokeAllAsync(
        ulong accountId,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await database.AuthSessions
            .Where(item => item.AccountId == accountId && item.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.RevokedAtUtc, now)
                    .SetProperty(item => item.RevokeReasonCode, reason),
                cancellationToken);
    }

    public async Task<IReadOnlyList<SessionResponse>> ListAsync(
        ulong accountId,
        CancellationToken cancellationToken)
    {
        var currentSessionId = GetCurrentSessionId();
        var now = DateTime.UtcNow;
        var sessions = await database.AuthSessions
            .AsNoTracking()
            .Where(item => item.AccountId == accountId
                && item.RevokedAtUtc == null
                && item.ExpiresAtUtc > now)
            .OrderByDescending(item => item.LastSeenAtUtc ?? item.CreatedAtUtc)
            .Select(item => new
            {
                item.Id,
                item.AuthenticationMethod,
                item.MfaSatisfied,
                item.CreatedAtUtc,
                item.ExpiresAtUtc,
                item.LastSeenAtUtc,
            })
            .ToListAsync(cancellationToken);

        return sessions.Select(item =>
        {
            var id = new Guid(item.Id);
            return new SessionResponse(
                id,
                id == currentSessionId,
                item.AuthenticationMethod,
                item.MfaSatisfied,
                item.CreatedAtUtc,
                item.ExpiresAtUtc,
                item.LastSeenAtUtc);
        }).ToArray();
    }

    public async Task<bool> RevokeAsync(
        ulong accountId,
        Guid sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        var bytes = sessionId.ToByteArray();
        var updated = await database.AuthSessions
            .Where(item => item.AccountId == accountId
                && item.Id.SequenceEqual(bytes)
                && item.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.RevokedAtUtc, DateTime.UtcNow)
                    .SetProperty(item => item.RevokeReasonCode, reason),
                cancellationToken);

        if (updated > 0 && GetCurrentSessionId() == sessionId)
        {
            var context = httpContextAccessor.HttpContext;
            if (context is not null)
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            }
        }

        return updated > 0;
    }

    private Guid? GetCurrentSessionId()
    {
        var value = httpContextAccessor.HttpContext?.User.FindFirstValue(
            ViverAppClaimTypes.SessionId);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private byte[]? HashOptional(byte[]? value)
    {
        return value is null || value.Length == 0
            ? null
            : HMACSHA256.HashData(securityOptions.ChallengePepper, value);
    }
}
