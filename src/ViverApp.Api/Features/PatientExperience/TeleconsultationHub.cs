using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
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
    public async Task<Appointment> RequireAsync(ClaimsPrincipal user, ulong id, CancellationToken ct)
    {
        if (!ulong.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
            || !Guid.TryParse(user.FindFirstValue(ViverAppClaimTypes.SessionId), out var sessionId)) throw new HubException("Entre novamente para acessar o atendimento.");
        var session = sessionId.ToByteArray(); var now = clock.GetUtcNow().UtcDateTime;
        if (!await database.AuthSessions.AnyAsync(x => x.Id.SequenceEqual(session) && x.AccountId == actor && x.RevokedAtUtc == null
            && x.ExpiresAtUtc > now && x.Account.StatusCode == "active" && x.MfaSatisfied, ct)) throw new HubException("Sua sessão expirou. Entre novamente.");
        var appointment = await database.Appointments.AsNoTracking().Include(x => x.CurrentPayment).SingleOrDefaultAsync(x => x.Id == id && (x.PatientAccountId == actor || x.DoctorAccountId == actor), ct);
        if (appointment is null || appointment.ModalityCode != "online" || appointment.StatusCode != "confirmed" || appointment.CurrentPayment?.StatusCode != "paid"
            || appointment.StartsAtUtc > now.AddMinutes(15) || appointment.EndsAtUtc < now) throw new HubException("A sala não está disponível. A entrada é permitida 15 minutos antes e durante o atendimento confirmado e pago.");
        if (appointment.PatientAccountId == actor && !user.IsInRole(ViverAppRoles.Patient)
            || appointment.DoctorAccountId == actor && !user.IsInRole(ViverAppRoles.Doctor)) throw new HubException("Atendimento indisponível.");
        return appointment;
    }
}

[Authorize(Roles = "patient,doctor")]
public sealed class TeleconsultationHub(ViverAppDbContext database, TeleconsultationAccess access,
    IAntiforgery antiforgery, SecurityBaselineOptions options, VideoInvocationLimiter limiter,
    IdentityAuditWriter audit) : Hub
{
    private ulong Actor => ulong.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    public override async Task OnConnectedAsync()
    {
        var origin = Context.GetHttpContext()?.Request.Headers.Origin.ToString();
        if (!options.AllowedCorsOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) { Context.Abort(); throw new HubException("Origem não permitida."); }
        await base.OnConnectedAsync();
    }

    public async Task<object> Join(ulong appointmentId, string csrfToken)
    {
        Limit();
        var http = Context.GetHttpContext()!;
        if (csrfToken.Length > 4096) throw new HubException("Validação inválida.");
        http.Request.Headers["X-CSRF-TOKEN"] = csrfToken;
        try { await antiforgery.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { throw new HubException("Atualize a página antes de entrar."); }
        var appointment = await access.RequireAsync(Context.User!, appointmentId, Context.ConnectionAborted);
        await Leave();
        var until = DateTime.UtcNow.AddSeconds(45);
        await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO teleconsultation_peers (appointment_id,account_id,connection_id,expires_at_utc) VALUES ({appointmentId},{Actor},{Context.ConnectionId},{until}) ON DUPLICATE KEY UPDATE connection_id={Context.ConnectionId},expires_at_utc={until}", Context.ConnectionAborted);
        Context.Items["appointment"] = appointmentId;
        var peer = await OtherPeer(appointment, Context.ConnectionAborted);
        if (peer is not null) await Clients.Client(peer).SendAsync("PeerReady", Context.ConnectionAborted);
        await audit.WriteAsync("teleconsultation.joined", Actor, "appointment", appointmentId.ToString(CultureInfo.InvariantCulture), null, Context.ConnectionAborted);
        return new { initiator = Actor == appointment.DoctorAccountId, peerPresent = peer is not null, endsAtUtc = DateTime.SpecifyKind(appointment.EndsAtUtc, DateTimeKind.Utc) };
    }

    public async Task Heartbeat()
    {
        Limit();
        var appointment = await Joined();
        await database.TeleconsultationPeers.Where(x => x.AppointmentId == appointment.Id && x.AccountId == Actor && x.ConnectionId == Context.ConnectionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAtUtc, DateTime.UtcNow.AddSeconds(45)), Context.ConnectionAborted);
    }

    public async Task Signal(string kind, string payload)
    {
        Limit();
        if (kind is not ("offer" or "answer" or "candidate") || payload.Length > 48000) throw new HubException("Sinal inválido.");
        try { using var json = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 6 }); if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(); }
        catch (JsonException) { throw new HubException("Sinal inválido."); }
        var appointment = await Joined();
        var peer = await OtherPeer(appointment, Context.ConnectionAborted);
        if (peer is not null) await Clients.Client(peer).SendAsync("Signal", kind, payload, Context.ConnectionAborted);
    }

    public async Task Leave()
    {
        if (Context.Items.TryGetValue("appointment", out var value) && value is ulong id)
        {
            var others = await database.TeleconsultationPeers.AsNoTracking().Where(x => x.AppointmentId == id && x.AccountId != Actor && x.ExpiresAtUtc > DateTime.UtcNow).Select(x => x.ConnectionId).ToArrayAsync();
            await database.TeleconsultationPeers.Where(x => x.AppointmentId == id && x.AccountId == Actor && x.ConnectionId == Context.ConnectionId).ExecuteDeleteAsync();
            foreach (var peer in others) await Clients.Client(peer).SendAsync("PeerLeft");
            Context.Items.Remove("appointment");
        }
    }
    public override async Task OnDisconnectedAsync(Exception? exception) { await Leave(); await base.OnDisconnectedAsync(exception); }
    private void Limit() { if (!limiter.Allow(Actor.ToString(CultureInfo.InvariantCulture))) throw new HubException("Aguarde antes de tentar novamente."); }
    private async Task<Appointment> Joined()
    {
        if (!Context.Items.TryGetValue("appointment", out var value) || value is not ulong id) throw new HubException("Entre na sala primeiro.");
        var appointment = await access.RequireAsync(Context.User!, id, Context.ConnectionAborted);
        if (!await database.TeleconsultationPeers.AnyAsync(x => x.AppointmentId == id && x.AccountId == Actor && x.ConnectionId == Context.ConnectionId && x.ExpiresAtUtc > DateTime.UtcNow, Context.ConnectionAborted)) throw new HubException("Esta conexão foi substituída. Reconecte para continuar.");
        return appointment;
    }
    private Task<string?> OtherPeer(Appointment appointment, CancellationToken ct) => database.TeleconsultationPeers.AsNoTracking()
        .Where(x => x.AppointmentId == appointment.Id && x.AccountId == (Actor == appointment.PatientAccountId ? appointment.DoctorAccountId : appointment.PatientAccountId) && x.ExpiresAtUtc > DateTime.UtcNow)
        .Select(x => x.ConnectionId).SingleOrDefaultAsync(ct);
}
