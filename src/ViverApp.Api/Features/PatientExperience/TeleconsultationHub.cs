using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.PatientExperience;

public sealed class VideoInvocationLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(actor =>
        RateLimitPartition.GetFixedWindowLimiter(actor, _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    public bool Allow(string actor) { using var lease = limiter.AttemptAcquire(actor); return lease.IsAcquired; }
    public void Dispose() => limiter.Dispose();
}

public sealed class TeleconsultationAccess(ViverAppDbContext database, TimeProvider clock)
{
    public async Task<Appointment?> RequireAppointmentAsync(ulong id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var appointment = await database.Appointments.AsNoTracking().Include(x => x.CurrentPayment)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        return appointment is not null && appointment.ModalityCode == "online" && appointment.StatusCode == "confirmed"
            && (!appointment.RequiresPayment || appointment.CurrentPayment?.StatusCode == "paid")
            && appointment.StartsAtUtc <= now.AddMinutes(15) && appointment.EndsAtUtc >= now ? appointment : null;
    }

    public async Task<Appointment> RequireAsync(ClaimsPrincipal user, ulong id, CancellationToken ct)
    {
        if (!ulong.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
            || !Guid.TryParse(user.FindFirstValue(ViverAppClaimTypes.SessionId), out var sessionId)) throw new HubException("Entre novamente para acessar o atendimento.");
        var session = sessionId.ToByteArray(); var now = clock.GetUtcNow().UtcDateTime;
        if (!await database.AuthSessions.AnyAsync(x => x.Id.SequenceEqual(session) && x.AccountId == actor && x.RevokedAtUtc == null
            && x.ExpiresAtUtc > now && x.Account.StatusCode == "active" && x.MfaSatisfied, ct)) throw new HubException("Sua sessão expirou. Entre novamente.");
        var appointment = await RequireAppointmentAsync(id, ct);
        if (appointment is null || appointment.PatientAccountId != actor && appointment.ProfessionalAccountId != actor)
            throw new HubException("A sala não está disponível. A entrada é permitida 15 minutos antes e durante o atendimento confirmado.");
        if (appointment.PatientAccountId == actor && !user.IsInRole(ViverAppRoles.Patient)
            || appointment.ProfessionalAccountId == actor && !(user.IsInRole(ViverAppRoles.Doctor) || user.IsInRole(ViverAppRoles.Psychologist))) throw new HubException("Atendimento indisponível.");
        return appointment;
    }
}

public sealed class TeleconsultationHub(TeleconsultationPresence presence, TeleconsultationAccess access,
    TeleconsultationGuestService guests, IAntiforgery antiforgery, SecurityBaselineOptions options,
    VideoInvocationLimiter limiter, IdentityAuditWriter audit) : Hub
{
    private ulong? Actor => ulong.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ? actor : null;
    private Guid? Guest => Context.Items.TryGetValue("guest", out var value) && value is Guid id ? id : null;
    public override async Task OnConnectedAsync()
    {
        var origin = Context.GetHttpContext()?.Request.Headers.Origin.ToString();
        if (!options.AllowedCorsOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        { Context.Abort(); throw new HubException("Origem não permitida."); }
        await base.OnConnectedAsync();
    }

    public async Task<object> Join(ulong appointmentId, string csrfToken)
    {
        if (csrfToken.Length > 4096) throw new HubException("Validação inválida.");
        var http = Context.GetHttpContext()!;
        http.Request.Headers["X-CSRF-TOKEN"] = csrfToken;
        try { await antiforgery.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { throw new HubException("Atualize a página antes de entrar."); }
        var actor = Actor;
        Guid? guest = null;
        Appointment appointment;
        if (actor is not null)
            appointment = await access.RequireAsync(Context.User!, appointmentId, Context.ConnectionAborted);
        else
        {
            var credential = await guests.RequireGuestAsync(http, appointmentId, Context.ConnectionAborted);
            if (credential is null) throw new HubException("Convite inválido ou expirado.");
            guest = credential.Value.GuestId;
            appointment = await access.RequireAppointmentAsync(appointmentId, Context.ConnectionAborted)
                ?? throw new HubException("A sala não está disponível.");
        }
        Limit(actor?.ToString(CultureInfo.InvariantCulture) ?? guest!.Value.ToString("N"));
        await Leave();
        var existing = await presence.JoinAsync(appointmentId, actor, guest, Context.ConnectionId, Context.ConnectionAborted);
        Context.Items["appointment"] = appointmentId;
        if (guest is not null) Context.Items["guest"] = guest.Value;
        foreach (var peer in existing) await Clients.Client(peer).SendAsync("PeerJoined", Context.ConnectionId, Context.ConnectionAborted);
        await audit.WriteAsync("teleconsultation.joined", actor, "appointment", appointmentId.ToString(CultureInfo.InvariantCulture), null, Context.ConnectionAborted);
        return new { peers = existing, endsAtUtc = DateTime.SpecifyKind(appointment.EndsAtUtc, DateTimeKind.Utc) };
    }

    public async Task Heartbeat()
    {
        var appointment = await Joined();
        Limit(Actor?.ToString(CultureInfo.InvariantCulture) ?? Guest!.Value.ToString("N"));
        if (await presence.HeartbeatAsync(appointment.Id, Context.ConnectionId, Context.ConnectionAborted) != 1)
            throw new HubException("Esta conexão foi substituída. Reconecte para continuar.");
    }

    public async Task Signal(string targetConnectionId, string kind, string payload)
    {
        var appointment = await Joined();
        Limit(Actor?.ToString(CultureInfo.InvariantCulture) ?? Guest!.Value.ToString("N"));
        if (targetConnectionId.Length is < 1 or > 128 || targetConnectionId == Context.ConnectionId
            || kind is not ("offer" or "answer" or "candidate") || payload.Length is < 2 or > 48000)
            throw new HubException("Sinal inválido.");
        try
        {
            using var json = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 6 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            if (kind is "offer" or "answer")
            {
                if (json.RootElement.GetProperty("type").GetString() != kind ||
                    json.RootElement.GetProperty("sdp").GetString() is not { Length: > 0 and <= 46000 }) throw new JsonException();
            }
            else if (json.RootElement.GetProperty("candidate").GetString() is not { Length: <= 8000 }) throw new JsonException();
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new HubException("Sinal inválido."); }
        var permitted = await presence.ContainsAsync(appointment.Id, targetConnectionId, Context.ConnectionAborted);
        if (!permitted) throw new HubException("Participante indisponível nesta sala.");
        await Clients.Client(targetConnectionId).SendAsync("Signal", Context.ConnectionId, kind, payload, Context.ConnectionAborted);
    }

    public async Task Leave()
    {
        if (!Context.Items.TryGetValue("appointment", out var value) || value is not ulong id) return;
        var others = await presence.LeaveAsync(id, Context.ConnectionId, CancellationToken.None);
        var hostStillPresent = await guests.HostPresentAsync(id, CancellationToken.None);
        foreach (var peer in others)
        {
            await Clients.Client(peer).SendAsync("PeerLeft", Context.ConnectionId);
            if (!hostStillPresent) await Clients.Client(peer).SendAsync("RoomClosed");
        }
        Context.Items.Remove("appointment"); Context.Items.Remove("guest");
    }
    public override async Task OnDisconnectedAsync(Exception? exception) { await Leave(); await base.OnDisconnectedAsync(exception); }

    private void Limit(string identity)
    {
        if (!limiter.Allow(identity)) throw new HubException("Aguarde antes de tentar novamente.");
    }

    private async Task<Appointment> Joined()
    {
        if (!Context.Items.TryGetValue("appointment", out var value) || value is not ulong id)
            throw new HubException("Entre na sala primeiro.");
        var appointment = Actor is not null
            ? await access.RequireAsync(Context.User!, id, Context.ConnectionAborted)
            : await guests.RequireGuestAsync(Context.GetHttpContext()!, id, Context.ConnectionAborted) is not null
                ? await access.RequireAppointmentAsync(id, Context.ConnectionAborted)
                : null;
        if (appointment is null || Guest is not null && !await guests.HostPresentAsync(id, Context.ConnectionAborted))
            throw new HubException("A sala ou seu convite expirou.");
        if (!await presence.ContainsAsync(id, Context.ConnectionId, Context.ConnectionAborted))
            throw new HubException("Esta conexão foi substituída. Reconecte para continuar.");
        return appointment;
    }
}
