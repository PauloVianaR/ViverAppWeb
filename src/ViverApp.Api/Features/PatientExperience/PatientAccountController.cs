using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.PatientExperience;

public sealed class RecentAuthentication(ViverAppDbContext database)
{
    public async Task<bool> IsRecentAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ViverAppClaimTypes.SessionId), out var session)
            || !ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var account)) return false;
        var id = session.ToByteArray();
        var now = DateTime.UtcNow;
        return await database.AuthSessions.AnyAsync(x => x.Id.SequenceEqual(id) && x.AccountId == account
            && x.CreatedAtUtc >= now.AddMinutes(-30) && x.ExpiresAtUtc > now && x.RevokedAtUtc == null, ct);
    }
}

[ApiController, Route("api/v1/patient/account"), Route("api/v1/doctor/account"), Route("api/v1/psychologist/account"), Route("api/v1/manager/account"), Authorize(Roles = "patient,doctor,psychologist,manager")]
[ServiceFilter(typeof(PatientExperienceExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PatientAccountController(ViverAppDbContext database, UserManager<ViverAppUser> users,
    IdentityChallengeService challenges, IdentityAuditWriter audit, RecentAuthentication recent,
    ViverAppSessionService sessions) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    private async Task RequireRecentAsync(CancellationToken ct)
    {
        if (!await recent.IsRecentAsync(User, ct)) throw new PatientExperienceException(403, "Entre novamente para confirmar sua identidade antes desta alteração.");
    }

    [HttpGet("security")]
    public async Task<IActionResult> Security(CancellationToken ct)
    {
        var user = (await users.GetUserAsync(User))!;
        return Ok(new { hasPassword = await users.HasPasswordAsync(user), googleLinked = (await users.GetLoginsAsync(user)).Any(x => x.LoginProvider == "Google"), recentAuthentication = await recent.IsRecentAsync(User, ct) });
    }

    [HttpPost("contact/request"), EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    public async Task<IActionResult> RequestContact(PatientContactRequest request, CancellationToken ct)
    {
        await RequireRecentAsync(ct);
        var destination = request.Channel == "email" ? IdentifierNormalizer.NormalizeEmail(request.Destination) : IdentifierNormalizer.NormalizePhone(request.Destination);
        if (destination is null) throw PatientExperienceService.Invalid("Informe um e-mail válido ou telefone brasileiro com +55 e DDD.");
        var user = (await users.GetUserAsync(User))!;
        var id = Guid.NewGuid();
        // Não revela se o destino já pertence a outra pessoa.
        var exists = request.Channel == "email" ? await database.Accounts.AnyAsync(x => x.Id != Actor && x.NormalizedEmail == destination, ct)
            : await database.Accounts.AnyAsync(x => x.Id != Actor && x.PhoneE164 == destination, ct);
        if (!exists)
        {
            await using var tx = await database.Database.BeginTransactionAsync(ct);
            var challenge = await challenges.CreateAsync(user, "contact_change", request.Channel, destination, ct);
            database.ContactChangeRequests.Add(new() { Id = id, AccountId = Actor, ChallengeId = challenge.ToByteArray(), ChannelCode = request.Channel, Destination = destination, CreatedAtUtc = DateTime.UtcNow });
            await audit.WriteAsync("patient.contact_change_requested", Actor, null, ct);
            await tx.CommitAsync(ct);
        }
        return Accepted(new { requestId = id, message = "Se o contato estiver disponível, enviaremos um código. O contato atual permanece válido até a confirmação." });
    }

    [HttpPost("contact/confirm"), EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    public async Task<IActionResult> ConfirmContact(PatientContactConfirmRequest request, CancellationToken ct)
    {
        var pending = await database.ContactChangeRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RequestId && x.AccountId == Actor && x.CompletedAtUtc == null, ct);
        if (pending is null) throw PatientExperienceService.Invalid("Código inválido ou expirado.");
        var verified = await challenges.VerifyAsync(new Guid(pending.ChallengeId), "contact_change", request.Code, ct);
        if (!verified.Succeeded || verified.AccountId != Actor || verified.Channel != pending.ChannelCode) throw PatientExperienceService.Invalid("Código inválido ou expirado.");
        await using var tx = await database.Database.BeginTransactionAsync(ct);
        var account = await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE id={Actor} FOR UPDATE").SingleAsync(ct);
        if (pending.ChannelCode == "email")
        {
            if (await database.Accounts.AnyAsync(x => x.Id != Actor && x.NormalizedEmail == pending.Destination, ct)) throw PatientExperienceService.Conflict("Não foi possível alterar o contato.");
            account.Email = pending.Destination; account.NormalizedEmail = pending.Destination; account.EmailVerified = true;
        }
        else
        {
            if (await database.Accounts.AnyAsync(x => x.Id != Actor && x.PhoneE164 == pending.Destination, ct)) throw PatientExperienceService.Conflict("Não foi possível alterar o contato.");
            account.PhoneE164 = pending.Destination; account.PhoneVerified = true;
        }
        account.UpdatedAtUtc = DateTime.UtcNow; account.RowVersion++;
        await database.ContactChangeRequests.Where(x => x.Id == pending.Id && x.AccountId == Actor).ExecuteUpdateAsync(s => s.SetProperty(x => x.CompletedAtUtc, DateTime.UtcNow), ct);
        await audit.WriteAsync("patient.contact_changed", Actor, null, ct);
        await sessions.RevokeAllAsync(Actor, "contact_changed", ct);
        await tx.CommitAsync(ct);
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return NoContent();
    }

    [HttpPost("password"), EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    public async Task<IActionResult> Password(ChangeOwnPasswordRequest request, CancellationToken ct)
    {
        await using var tx = await database.Database.BeginTransactionAsync(ct);
        var user = (await users.GetUserAsync(User))!;
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) throw PatientExperienceService.Invalid("Confira a senha atual. A nova senha precisa ter pelo menos 12 caracteres, maiúsculas, minúsculas, números e símbolo.");
        await sessions.RevokeAllAsync(Actor, "password_changed", ct);
        await audit.WriteAsync("patient.password_changed", Actor, null, ct);
        await tx.CommitAsync(ct);
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return NoContent();
    }

    [HttpPost("google/unlink"), EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    public async Task<IActionResult> UnlinkGoogle(CancellationToken ct)
    {
        await RequireRecentAsync(ct);
        await using var tx = await database.Database.BeginTransactionAsync(ct);
        var user = (await users.GetUserAsync(User))!;
        var alternative = (await users.HasPasswordAsync(user) && (user.EmailConfirmed || user.PhoneNumberConfirmed))
            || user.PhoneNumberConfirmed || await database.AccountPasskeys.AnyAsync(x => x.AccountId == Actor, ct);
        if (!alternative) throw PatientExperienceService.Conflict("Confirme um telefone, cadastre uma chave de acesso ou configure senha antes de desvincular o Google.");
        var login = (await users.GetLoginsAsync(user)).SingleOrDefault(x => x.LoginProvider == "Google");
        if (login is not null && !(await users.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey)).Succeeded) throw PatientExperienceService.Conflict("Não foi possível desvincular o Google.");
        await sessions.RevokeAllAsync(Actor, "google_unlinked", ct);
        await audit.WriteAsync("patient.google_unlinked", Actor, null, ct);
        await tx.CommitAsync(ct);
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return NoContent();
    }

    [HttpPost("sessions/revoke-others"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<IActionResult> RevokeOthers(CancellationToken ct)
    {
        var current = Guid.Parse(User.FindFirstValue(ViverAppClaimTypes.SessionId)!).ToByteArray();
        await database.AuthSessions.Where(x => x.AccountId == Actor && !x.Id.SequenceEqual(current) && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, DateTime.UtcNow).SetProperty(x => x.RevokeReasonCode, "user_revoked_others"), ct);
        await audit.WriteAsync("patient.other_sessions_revoked", Actor, null, ct);
        return NoContent();
    }
}
