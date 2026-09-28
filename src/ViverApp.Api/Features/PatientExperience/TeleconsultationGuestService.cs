using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.PatientExperience;

public sealed class TeleconsultationGuestService(
    ViverAppDbContext database,
    TeleconsultationAccess access,
    IDataProtectionProvider protection,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    SecurityBaselineOptions securityOptions,
    TimeProvider clock)
{
    public const string CookieName = "ViverApp.VideoGuest";
    private readonly IDataProtector protector = protection.CreateProtector("ViverApp.Teleconsultation.Guest.v1");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<string> RotateAsync(ClaimsPrincipal user, ulong appointmentId, CancellationToken ct)
    {
        var appointment = await access.RequireAsync(user, appointmentId, ct);
        var actor = ulong.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (actor != appointment.ProfessionalAccountId ||
            !(user.IsInRole(ViverAppRoles.Doctor) || user.IsInRole(ViverAppRoles.Psychologist)))
            throw new HubException("Somente o profissional responsável pode convidar participantes.");

        var secret = RandomNumberGenerator.GetBytes(32);
        var linkId = Guid.NewGuid();
        var now = Now;
        var expiry = appointment.EndsAtUtc < now.AddHours(2) ? appointment.EndsAtUtc : now.AddHours(2);
        if (expiry <= now) throw new HubException("O atendimento terminou.");
        var requestOrigin = httpContextAccessor.HttpContext?.Request.Headers.Origin.ToString();
        var webOrigin = !string.IsNullOrWhiteSpace(requestOrigin) && securityOptions.AllowedCorsOrigins.Contains(requestOrigin, StringComparer.OrdinalIgnoreCase)
            ? requestOrigin : configuration.GetValue<string>("Teleconsultation:WebOrigin")?.TrimEnd('/');
        if (string.IsNullOrEmpty(webOrigin) || !Uri.TryCreate(webOrigin, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new InvalidOperationException("Teleconsultation:WebOrigin deve apontar para a origem Web permitida.");
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        await database.TeleconsultationGuestLinks.Where(x => x.AppointmentId == appointmentId).ExecuteDeleteAsync(ct);
        database.TeleconsultationGuestLinks.Add(new TeleconsultationGuestLink
        {
            Id = linkId.ToByteArray(),
            AppointmentId = appointmentId,
            CreatedByAccountId = actor,
            TokenHash = SHA256.HashData(secret),
            CreatedAtUtc = now,
            ExpiresAtUtc = expiry
        });
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return $"{webOrigin}/convidado/reuniao#{linkId:N}.{Base64Url(secret)}";
    }

    public async Task RevokeAsync(ClaimsPrincipal user, ulong appointmentId, CancellationToken ct)
    {
        var appointment = await access.RequireAsync(user, appointmentId, ct);
        var actor = ulong.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (actor != appointment.ProfessionalAccountId) throw new HubException("Somente o profissional responsável pode revogar o convite.");
        await database.TeleconsultationGuestLinks.Where(x => x.AppointmentId == appointmentId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, Now), ct);
    }

    public async Task<(ulong AppointmentId, DateTime ExpiresAtUtc, Guid GuestId)?> ExchangeAsync(
        string linkIdText, string secretText, CancellationToken ct)
    {
        if (!Guid.TryParseExact(linkIdText, "N", out var id) || !TryBase64Url(secretText, out var secret) || secret.Length != 32)
            return null;
        var link = await database.TeleconsultationGuestLinks.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id.SequenceEqual(id.ToByteArray()) && x.RevokedAtUtc == null && x.ExpiresAtUtc > Now, ct);
        if (link is null || !CryptographicOperations.FixedTimeEquals(link.TokenHash, SHA256.HashData(secret))) return null;
        var appointment = await access.RequireAppointmentAsync(link.AppointmentId, ct);
        if (appointment is null || !await HostPresentAsync(link.AppointmentId, ct)) return null;
        return (link.AppointmentId, link.ExpiresAtUtc, Guid.NewGuid());
    }

    public string Protect(Guid linkId, Guid guestId) => protector.Protect($"{linkId:N}.{guestId:N}");

    public async Task<(ulong AppointmentId, Guid GuestId)?> CurrentGuestAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var cookie) || cookie.Length > 2048) return null;
        Guid linkId, guestId;
        try
        {
            var parts = protector.Unprotect(cookie).Split('.', 2);
            if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out linkId)
                || !Guid.TryParseExact(parts[1], "N", out guestId)) return null;
        }
        catch (CryptographicException) { return null; }
        var link = await database.TeleconsultationGuestLinks.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id.SequenceEqual(linkId.ToByteArray())
                && x.RevokedAtUtc == null && x.ExpiresAtUtc > Now, ct);
        if (link is null || await access.RequireAppointmentAsync(link.AppointmentId, ct) is null) return null;
        return (link.AppointmentId, guestId);
    }

    public async Task<(ulong AppointmentId, Guid GuestId)?> RequireGuestAsync(HttpContext http, ulong appointmentId, CancellationToken ct)
    {
        var current = await CurrentGuestAsync(http, ct);
        return current?.AppointmentId == appointmentId ? current : null;
    }

    public Task<bool> HostPresentAsync(ulong appointmentId, CancellationToken ct) => database.TeleconsultationPeers
        .AnyAsync(x => x.AppointmentId == appointmentId && x.AccountId == x.Appointment.ProfessionalAccountId
            && x.ExpiresAtUtc > Now, ct);

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool TryBase64Url(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length is < 40 or > 48 || text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return false;
        try { bytes = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4)); return true; }
        catch (FormatException) { return false; }
    }
}
