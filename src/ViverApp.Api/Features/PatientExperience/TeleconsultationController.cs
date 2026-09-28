using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Security;

namespace ViverApp.Api.Features.PatientExperience;

public sealed record GuestExchangeRequest(string LinkId, string Secret);

[ApiController, Route("api/v1/teleconsultation"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeleconsultationController(
    TeleconsultationGuestService guests,
    TeleconsultationAccess access,
    IConfiguration configuration,
    IHostEnvironment environment,
    TimeProvider clock,
    IdentityAuditWriter audit,
    ViverAppDbContext database,
    IHubContext<TeleconsultationHub> hub) : ControllerBase
{
    [Authorize(Roles = "doctor,psychologist")]
    [HttpPost("appointments/{id:long}/invite"), EnableRateLimiting(SecurityPolicyNames.VideoRateLimit)]
    public async Task<IActionResult> Invite(ulong id, CancellationToken ct)
    {
        try
        {
            var link = await guests.RotateAsync(User, id, ct);
            await DisconnectGuests(id, ct);
            await audit.WriteAsync("teleconsultation.invite_rotated", ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), "appointment", id.ToString(), null, ct);
            return Ok(new { link });
        }
        catch (HubException) { return Forbid(); }
    }

    [Authorize(Roles = "doctor,psychologist")]
    [HttpDelete("appointments/{id:long}/invite"), EnableRateLimiting(SecurityPolicyNames.VideoRateLimit)]
    public async Task<IActionResult> Revoke(ulong id, CancellationToken ct)
    {
        try
        {
            await guests.RevokeAsync(User, id, ct);
            await DisconnectGuests(id, ct);
            await audit.WriteAsync("teleconsultation.invite_revoked", ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), "appointment", id.ToString(), null, ct);
            return NoContent();
        }
        catch (HubException) { return Forbid(); }
    }

    [AllowAnonymous]
    [HttpPost("guest/exchange"), EnableRateLimiting(SecurityPolicyNames.PublicFormRateLimit)]
    public async Task<IActionResult> Exchange(GuestExchangeRequest request, CancellationToken ct)
    {
        var exchanged = await guests.ExchangeAsync(request.LinkId, request.Secret, ct);
        if (exchanged is null) return BadRequest(new ProblemDetails { Status = 400, Title = "Convite inválido, expirado ou sala ainda não aberta." });
        var (appointmentId, expiresAtUtc, guestId) = exchanged.Value;
        Response.Cookies.Append(TeleconsultationGuestService.CookieName,
            guests.Protect(Guid.ParseExact(request.LinkId, "N"), guestId), new CookieOptions
            {
                HttpOnly = true,
                Secure = !environment.IsDevelopment() || !configuration.GetValue("Security:AllowInsecureLocalHttp", false),
                SameSite = SameSiteMode.Strict,
                Path = "/",
                Expires = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero),
                IsEssential = true
            });
        await audit.WriteAsync("teleconsultation.guest_exchanged", null, "appointment", appointmentId.ToString(), null, ct);
        return Ok(new { appointmentId, expiresAtUtc });
    }

    [AllowAnonymous]
    [HttpGet("guest/current")]
    public async Task<IActionResult> CurrentGuest(CancellationToken ct)
    {
        var current = await guests.CurrentGuestAsync(HttpContext, ct);
        return current is null ? Unauthorized() : Ok(new { current.Value.AppointmentId });
    }

    [AllowAnonymous]
    [HttpGet("appointments/{id:long}/ice")]
    public async Task<IActionResult> Ice(ulong id, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            try { await access.RequireAsync(User, id, ct); }
            catch (HubException) { return Forbid(); }
        }
        else if (await guests.RequireGuestAsync(HttpContext, id, ct) is null) return Unauthorized();

        var stun = configuration.GetSection("Teleconsultation:StunUrls").Get<string[]>() ?? [];
        var turn = configuration.GetSection("Teleconsultation:TurnUrls").Get<string[]>() ?? [];
        var secret = configuration["Teleconsultation:TurnSharedSecret"];
        if (turn.Length > 0 && string.IsNullOrWhiteSpace(secret))
            return Problem(statusCode: 503, title: "Rede da videochamada indisponível. Contate o administrador.");
        if (!environment.IsDevelopment() && (turn.Length == 0 || string.IsNullOrWhiteSpace(secret)))
            return Problem(statusCode: 503, title: "Rede da videochamada indisponível. Contate o administrador.");
        var servers = new List<object>();
        if (stun.Length > 0) servers.Add(new { urls = stun });
        if (turn.Length > 0)
        {
            var username = $"{clock.GetUtcNow().AddMinutes(30).ToUnixTimeSeconds()}:{Guid.NewGuid():N}";
            var credential = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret!), Encoding.UTF8.GetBytes(username)));
            servers.Add(new { urls = turn, username, credential });
        }
        return Ok(new { iceServers = servers });
    }

    private async Task DisconnectGuests(ulong appointmentId, CancellationToken ct)
    {
        var connections = await database.TeleconsultationPeers.AsNoTracking()
            .Where(x => x.AppointmentId == appointmentId && x.GuestId != null && x.ExpiresAtUtc > clock.GetUtcNow().UtcDateTime)
            .Select(x => x.ConnectionId).ToArrayAsync(ct);
        foreach (var connection in connections) await hub.Clients.Client(connection).SendCoreAsync("RoomClosed", [], ct);
        await database.TeleconsultationPeers.Where(x => x.AppointmentId == appointmentId && x.GuestId != null)
            .ExecuteDeleteAsync(ct);
        var remaining = await database.TeleconsultationPeers.AsNoTracking()
            .Where(x => x.AppointmentId == appointmentId && x.ExpiresAtUtc > clock.GetUtcNow().UtcDateTime)
            .Select(x => x.ConnectionId).ToArrayAsync(ct);
        foreach (var peer in remaining)
            foreach (var departed in connections)
                await hub.Clients.Client(peer).SendCoreAsync("PeerLeft", [departed], ct);
    }
}
