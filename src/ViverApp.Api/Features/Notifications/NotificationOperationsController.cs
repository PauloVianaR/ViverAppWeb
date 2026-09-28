using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.AdministratorExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.Notifications;

public sealed record NotificationQueueSummary(
    int Pending, int Processing, int Sent, int Suppressed, int DeadLetter,
    double? OldestPendingMinutes, int ScheduledPending, int ScheduledDeadLetter);

public sealed record NotificationDeadLetterItem(
    ulong Id, string Channel, string TemplateKey, ushort TemplateVersion,
    ushort Attempts, string? ErrorCode, DateTime CreatedAtUtc);

public sealed record NotificationRetryRequest([Required, MinLength(5), MaxLength(300)] string Reason);

public sealed record NotificationSuppressionRequest(
    [Required] string Channel,
    [Required, MaxLength(254)] string Recipient,
    [Required, MinLength(5), MaxLength(40)] string ReasonCode,
    DateTime? ExpiresAtUtc);

[ApiController]
[Route("api/v1/administrator/notification-operations")]
[Authorize(Policy = ViverAppPolicies.Administrator)]
[ServiceFilter(typeof(AdministratorStepUpFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationOperationsController(
    ViverAppDbContext database, IdentityAuditWriter audit, TimeProvider clock) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<NotificationQueueSummary> Summary(CancellationToken cancellationToken)
    {
        var counts = await database.OutboxMessages.AsNoTracking()
            .GroupBy(item => item.StatusCode)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
        var scheduled = await database.ScheduledJobs.AsNoTracking()
            .GroupBy(item => item.StatusCode)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
        var oldest = await database.OutboxMessages.AsNoTracking()
            .Where(item => item.StatusCode == "pending")
            .MinAsync(item => (DateTime?)item.CreatedAtUtc, cancellationToken);
        var age = oldest.HasValue
            ? Math.Max(0, (clock.GetUtcNow().UtcDateTime - oldest.Value).TotalMinutes)
            : (double?)null;
        return new NotificationQueueSummary(
            Count("pending"), Count("processing"), Count("sent"),
            Count("suppressed"), Count("dead_letter"), age,
            ScheduledCount("pending"), ScheduledCount("dead_letter"));

        int Count(string status) => counts.GetValueOrDefault(status);
        int ScheduledCount(string status) => scheduled.GetValueOrDefault(status);
    }

    [HttpGet("dead-letters")]
    public async Task<IReadOnlyList<NotificationDeadLetterItem>> DeadLetters(
        [FromQuery, Range(1, 100)] int limit = 30, CancellationToken cancellationToken = default) =>
        await database.OutboxMessages.AsNoTracking()
            .Where(item => item.StatusCode == "dead_letter")
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(limit)
            .Select(item => new NotificationDeadLetterItem(item.Id, item.ChannelCode,
                item.TemplateKey, item.TemplateVersion, item.AttemptCount,
                item.LastErrorCode, item.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

    [HttpPost("dead-letters/{id:long}/retry")]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<IActionResult> Retry(ulong id, NotificationRetryRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var message = await database.OutboxMessages.FromSqlInterpolated($"SELECT * FROM outbox_messages WHERE id={id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (message is null) return NotFound();
        if (message.StatusCode != "dead_letter")
            return Conflict(new ProblemDetails { Status = 409, Title = "Somente mensagens na fila de falhas podem ser reenviadas." });
        if (message.TemplateKey.StartsWith("identity.", StringComparison.Ordinal))
            return Conflict(new ProblemDetails { Status = 409, Title = "Códigos de acesso expirados não podem ser reenviados. Gere um novo código." });
        message.StatusCode = "pending";
        message.AttemptCount = 0;
        message.NextAttemptAtUtc = clock.GetUtcNow().UtcDateTime;
        message.LastErrorCode = null;
        message.CompletedAtUtc = null;
        message.LeaseOwner = null;
        message.LeaseUntilUtc = null;
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("notifications.dead_letter_retried", ActorId,
            "outbox_message", id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["reason"] = request.Reason.Trim() }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("suppressions")]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<IActionResult> Suppress(NotificationSuppressionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Channel is not ("email" or "sms")
            || request.ReasonCode is not ("user_request" or "provider_bounce" or "invalid_contact" or "other")
            || request.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime)
            return BadRequest(new ProblemDetails { Status = 400, Title = "Revise o canal e a validade da supressão." });
        var recipient = request.Channel == "email"
            ? IdentifierNormalizer.NormalizeEmail(request.Recipient)
            : IdentifierNormalizer.NormalizePhone(request.Recipient);
        if (recipient is null)
            return BadRequest(new ProblemDetails { Status = 400, Title = "Informe um e-mail ou telefone válido para o canal." });
        var hash = NotificationDeliveryPolicy.HashRecipient(request.Channel, recipient);
        var existing = await database.NotificationSuppressions.SingleOrDefaultAsync(
            item => item.ChannelCode == request.Channel && item.RecipientHash == hash,
            cancellationToken);
        if (existing is null)
        {
            database.NotificationSuppressions.Add(new NotificationSuppression
            {
                ChannelCode = request.Channel,
                RecipientHash = hash,
                ReasonCode = request.ReasonCode.Trim(),
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
                ExpiresAtUtc = request.ExpiresAtUtc,
            });
        }
        else
        {
            existing.ReasonCode = request.ReasonCode.Trim();
            existing.ExpiresAtUtc = request.ExpiresAtUtc;
        }
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("notifications.destination_suppressed", ActorId,
            "notification_suppression", null,
            new Dictionary<string, string>
            {
                ["channel"] = request.Channel,
                ["reasonCode"] = request.ReasonCode.Trim()
            }, cancellationToken);
        return NoContent();
    }

    private ulong ActorId => ulong.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
}
