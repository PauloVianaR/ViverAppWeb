using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.StaffCommunication;

public sealed record StaffContact(ulong Id, string FullName, string Role, bool Online, int Unread);
public sealed record StaffMessageView(ulong Id, ulong SenderId, ulong RecipientId, string Body, DateTime CreatedAtUtc, bool IsRead);
public sealed record ManagerAlertView(ulong Id, ulong AppointmentId, ulong AppointmentNumber, string Type, DateTime CreatedAtUtc, bool IsRead);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SendStaffMessageRequest(ulong RecipientId, Guid ClientNonce, [Required, StringLength(2000, MinimumLength = 1)] string Body);

public sealed class StaffPresence
{
    private readonly ConcurrentDictionary<ulong, int> connections = new();
    public bool Online(ulong id) => connections.ContainsKey(id);
    public bool Add(ulong id) => connections.AddOrUpdate(id, 1, (_, count) => count + 1) == 1;
    public bool Remove(ulong id)
    {
        while (connections.TryGetValue(id, out var count))
        {
            if (count > 1 && connections.TryUpdate(id, count - 1, count)) return false;
            if (count == 1 && connections.TryRemove(new KeyValuePair<ulong, int>(id, count))) return true;
        }
        return false;
    }
}

[Authorize(Roles = "doctor,psychologist,manager,administrator")]
public sealed class StaffChatHub(StaffPresence presence, ViverAppDbContext database) : Hub
{
    public static string Group(ulong id) => $"staff:{id.ToString(CultureInfo.InvariantCulture)}";
    public override async Task OnConnectedAsync()
    {
        if (!ulong.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            || !await database.Accounts.AsNoTracking().AnyAsync(a => a.Id == id && a.StatusCode == "active"
                && (a.RoleCode == "doctor" || a.RoleCode == "psychologist"
                    || a.RoleCode == "manager" || a.RoleCode == "administrator")))
        { Context.Abort(); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(id));
        if (presence.Add(id)) await Clients.All.SendAsync("PresenceChanged", id, true);
        await base.OnConnectedAsync();
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ulong.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && presence.Remove(id))
            await Clients.All.SendAsync("PresenceChanged", id, false);
        await base.OnDisconnectedAsync(exception);
    }
}

public sealed class StaffCommunicationService(
    ViverAppDbContext database, StaffPresence presence, TimeProvider clock, IHubContext<StaffChatHub> hub,
    ILogger<StaffCommunicationService> logger)
{
    private static readonly string[] StaffRoles = ["administrator", "manager", "doctor", "psychologist"];
    public async Task<IReadOnlyList<StaffContact>> ContactsAsync(ulong actor, CancellationToken ct)
    {
        await RequireActorAsync(actor, ct);
        var accounts = await database.Accounts.AsNoTracking()
            .Where(a => a.Id != actor && a.StatusCode == "active" && StaffRoles.Contains(a.RoleCode))
            .Select(a => new { a.Id, a.FullName, a.RoleCode }).ToArrayAsync(ct);
        var unread = await database.StaffMessages.AsNoTracking()
            .Where(m => m.RecipientAccountId == actor && m.ReadAtUtc == null)
            .GroupBy(m => m.SenderAccountId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return accounts.Select(a => new StaffContact(a.Id, a.FullName, a.RoleCode, presence.Online(a.Id), unread.GetValueOrDefault(a.Id)))
            .OrderByDescending(a => a.Online).ThenBy(a => a.FullName, StringComparer.Create(new CultureInfo("pt-BR"), true)).ToArray();
    }
    public async Task<IReadOnlyList<StaffMessageView>> ConversationAsync(ulong actor, ulong other, CancellationToken ct)
    {
        await RequireContactAsync(actor, other, ct);
        var messages = await database.StaffMessages.AsNoTracking()
            .Where(m => m.SenderAccountId == actor && m.RecipientAccountId == other
                || m.SenderAccountId == other && m.RecipientAccountId == actor)
            .OrderByDescending(m => m.Id).Take(100).ToArrayAsync(ct);
        return messages.Reverse().Select(Map).ToArray();
    }
    public async Task<StaffMessageView> SendAsync(ulong actor, SendStaffMessageRequest request, CancellationToken ct)
    {
        await RequireContactAsync(actor, request.RecipientId, ct);
        var body = request.Body?.Trim();
        if (request.ClientNonce == Guid.Empty || body is null || body.Length is < 1 or > 2000)
            throw new StaffRuleException(400, "Informe uma mensagem de até 2000 caracteres.");
        var existing = await database.StaffMessages.AsNoTracking()
            .SingleOrDefaultAsync(m => m.SenderAccountId == actor && m.ClientNonce == request.ClientNonce, ct);
        if (existing is not null)
        {
            if (existing.RecipientAccountId != request.RecipientId || existing.Body != body)
                throw new StaffRuleException(409, "Esta mensagem já foi enviada com outro conteúdo.");
            return Map(existing);
        }
        var message = new StaffMessage
        {
            SenderAccountId = actor,
            RecipientAccountId = request.RecipientId,
            ClientNonce = request.ClientNonce,
            Body = body,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        database.StaffMessages.Add(message);
        try { await database.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is MySql.Data.MySqlClient.MySqlException { Number: 1062 })
        {
            database.Entry(message).State = EntityState.Detached;
            var winner = await database.StaffMessages.AsNoTracking().SingleOrDefaultAsync(
                m => m.SenderAccountId == actor && m.ClientNonce == request.ClientNonce, ct);
            if (winner is null || winner.RecipientAccountId != request.RecipientId || winner.Body != body)
                throw new StaffRuleException(409, "Esta mensagem já foi enviada com outro conteúdo.");
            return Map(winner);
        }
        var view = Map(message);
        try { await hub.Clients.Groups(StaffChatHub.Group(actor), StaffChatHub.Group(request.RecipientId)).SendAsync("StaffMessage", view, CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "Mensagem persistida; entrega em tempo real indisponível."); }
        return view;
    }
    public async Task ReadConversationAsync(ulong actor, ulong other, CancellationToken ct)
    {
        await RequireContactAsync(actor, other, ct);
        await database.StaffMessages.Where(m => m.RecipientAccountId == actor && m.SenderAccountId == other && m.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAtUtc, clock.GetUtcNow().UtcDateTime), ct);
        try { await hub.Clients.Group(StaffChatHub.Group(other)).SendAsync("MessagesRead", actor, CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "Leitura persistida; confirmação em tempo real indisponível."); }
    }
    public async Task<IReadOnlyList<ManagerAlertView>> ManagerAlertsAsync(ulong actor, CancellationToken ct)
    {
        await RequireActorAsync(actor, ct);
        var alerts = await database.ManagerNotifications.AsNoTracking().Where(n => n.ManagerAccountId == actor)
            .OrderByDescending(n => n.Id).Take(50)
            .Select(n => new ManagerAlertView(n.Id, n.AppointmentId, n.Appointment.AppointmentNumber,
                n.TypeCode, n.CreatedAtUtc, n.ReadAtUtc != null)).ToArrayAsync(ct);
        return alerts;
    }
    public async Task ReadManagerAlertsAsync(ulong actor, CancellationToken ct)
    {
        await RequireActorAsync(actor, ct);
        await database.ManagerNotifications.Where(n => n.ManagerAccountId == actor && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, clock.GetUtcNow().UtcDateTime), ct);
    }
    public async Task<IReadOnlyList<ManagerAlertView>> QueueManagerAlertsAsync(Appointment appointment, string type, CancellationToken ct)
    {
        var ids = await database.Accounts.AsNoTracking().Where(a => a.RoleCode == "manager" && a.StatusCode == "active")
            .Select(a => a.Id).ToArrayAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var entries = ids.Select(id => new ManagerNotification
        {
            ManagerAccountId = id,
            AppointmentId = appointment.Id,
            TypeCode = type,
            CreatedAtUtc = now
        }).ToArray();
        database.ManagerNotifications.AddRange(entries);
        await database.SaveChangesAsync(ct);
        return entries.Select(n => new ManagerAlertView(n.Id, n.AppointmentId, appointment.AppointmentNumber, n.TypeCode, now, false)).ToArray();
    }
    public async Task PublishManagerAlertsAsync(IReadOnlyList<ManagerAlertView> alerts)
    {
        if (alerts.Count == 0) return;
        var ids = await database.ManagerNotifications.AsNoTracking()
            .Where(n => alerts.Select(a => a.Id).Contains(n.Id))
            .Select(n => new { n.Id, n.ManagerAccountId }).ToArrayAsync();
        foreach (var alert in alerts)
        {
            var target = ids.FirstOrDefault(n => n.Id == alert.Id);
            if (target is null) continue;
            try { await hub.Clients.Group(StaffChatHub.Group(target.ManagerAccountId)).SendAsync("ManagerAlert", alert); }
            catch (Exception ex) { logger.LogWarning(ex, "Alerta gerencial persistido; entrega em tempo real indisponível."); }
        }
    }
    private async Task RequireContactAsync(ulong actor, ulong other, CancellationToken ct)
    {
        await RequireActorAsync(actor, ct);
        if (actor == other || other == 0 || !await database.Accounts.AsNoTracking()
            .AnyAsync(a => a.Id == other && a.StatusCode == "active" && StaffRoles.Contains(a.RoleCode), ct))
            throw new StaffRuleException(404, "Destinatário indisponível.");
    }
    private async Task RequireActorAsync(ulong actor, CancellationToken ct)
    {
        if (!await database.Accounts.AsNoTracking().AnyAsync(a => a.Id == actor && a.StatusCode == "active"
            && StaffRoles.Contains(a.RoleCode), ct))
            throw new StaffRuleException(403, "Sua conta não pode acessar o chat da equipe.");
    }
    private static StaffMessageView Map(StaffMessage m) => new(m.Id, m.SenderAccountId, m.RecipientAccountId, m.Body, m.CreatedAtUtc, m.ReadAtUtc.HasValue);
}

public sealed class StaffRuleException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

[ApiController]
[Authorize(Roles = "doctor,psychologist,manager,administrator")]
[Route("api/v1/staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffCommunicationController(StaffCommunicationService service) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    [HttpGet("contacts")]
    public Task<IActionResult> Contacts(CancellationToken ct) => Respond(() => service.ContactsAsync(Actor, ct));
    [HttpGet("messages/{other}")]
    public async Task<IActionResult> Conversation(ulong other, CancellationToken ct) => await Respond(() => service.ConversationAsync(Actor, other, ct));
    [HttpPost("messages")]
    [EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<IActionResult> Send(SendStaffMessageRequest request, CancellationToken ct) => await Respond(() => service.SendAsync(Actor, request, ct));
    [HttpPost("messages/{other}/read")]
    public async Task<IActionResult> Read(ulong other, CancellationToken ct) => await Respond(async () => { await service.ReadConversationAsync(Actor, other, ct); return new { ok = true }; });
    [HttpGet("manager-alerts")]
    [Authorize(Roles = "manager")]
    public Task<IActionResult> ManagerAlerts(CancellationToken ct) => Respond(() => service.ManagerAlertsAsync(Actor, ct));
    [HttpPost("manager-alerts/read-all")]
    [Authorize(Roles = "manager")]
    public Task<IActionResult> ReadManagerAlerts(CancellationToken ct) =>
        Respond(async () => { await service.ReadManagerAlertsAsync(Actor, ct); return new { ok = true }; });
    private async Task<IActionResult> Respond<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (StaffRuleException ex) { return Problem(statusCode: ex.Status, title: ex.Message); }
    }
}
