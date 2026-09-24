using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Notifications;

internal sealed class OutboxStore(ViverAppDbContext database, TimeProvider clock)
{
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromMinutes(2);

    public async Task<OutboxMessage?> ClaimAsync(
        bool identityMessages, string owner, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var query = identityMessages
            ? database.OutboxMessages.FromSqlInterpolated($$"""
                SELECT * FROM outbox_messages
                WHERE template_key LIKE 'identity.%'
                  AND ((status_code = 'pending' AND next_attempt_at_utc <= {{now}})
                    OR (status_code = 'processing' AND lease_until_utc < {{now}}))
                ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED
                """)
            : database.OutboxMessages.FromSqlInterpolated($$"""
                SELECT * FROM outbox_messages
                WHERE channel_code IN ('email', 'sms') AND template_key NOT LIKE 'identity.%'
                  AND ((status_code = 'pending' AND next_attempt_at_utc <= {{now}})
                    OR (status_code = 'processing' AND lease_until_utc < {{now}}))
                ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED
                """);
        var message = await query.SingleOrDefaultAsync(cancellationToken);
        if (message is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        message.StatusCode = "processing";
        message.LeaseOwner = owner;
        message.LeaseUntilUtc = now.Add(LeaseLifetime);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        database.Entry(message).State = EntityState.Detached;
        return message;
    }

    public async Task<OutboxMessage?> ClaimInternalAsync(string owner,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var message = await database.OutboxMessages.FromSqlInterpolated($$"""
            SELECT * FROM outbox_messages
            WHERE channel_code = 'internal'
              AND ((status_code = 'pending' AND next_attempt_at_utc <= {{now}})
                OR (status_code = 'processing' AND lease_until_utc < {{now}}))
            ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED
            """).SingleOrDefaultAsync(cancellationToken);
        if (message is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        message.StatusCode = "processing";
        message.LeaseOwner = owner;
        message.LeaseUntilUtc = now.Add(LeaseLifetime);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        database.Entry(message).State = EntityState.Detached;
        return message;
    }

    public Task<bool> MarkSentAsync(ulong id, string owner, string? providerReference,
        CancellationToken cancellationToken) => CompleteAsync(id, owner, "sent", null,
        providerReference, permanent: false, cancellationToken);

    public Task<bool> MarkSuppressedAsync(ulong id, string owner, string reason,
        CancellationToken cancellationToken) => CompleteAsync(id, owner, "suppressed", reason,
        null, permanent: false, cancellationToken);

    public Task<bool> MarkFailedAsync(ulong id, string owner, string errorCode, bool permanent,
        CancellationToken cancellationToken) => CompleteAsync(id, owner, "failed", errorCode,
        null, permanent, cancellationToken);

    private async Task<bool> CompleteAsync(ulong id, string owner, string outcome,
        string? errorCode, string? providerReference, bool permanent,
        CancellationToken cancellationToken)
    {
        var message = await database.OutboxMessages.SingleOrDefaultAsync(
            item => item.Id == id && item.StatusCode == "processing" && item.LeaseOwner == owner,
            cancellationToken);
        if (message is null) return false;

        var now = clock.GetUtcNow().UtcDateTime;
        message.LeaseOwner = null;
        message.LeaseUntilUtc = null;
        message.LastErrorCode = errorCode;
        if (outcome is "sent" or "suppressed")
        {
            message.StatusCode = outcome;
            message.CompletedAtUtc = now;
            message.SentAtUtc = outcome == "sent" ? now : null;
            message.ProviderReference = providerReference;
        }
        else
        {
            message.AttemptCount++;
            if (permanent || message.AttemptCount >= message.MaxAttempts)
            {
                message.StatusCode = "dead_letter";
                message.CompletedAtUtc = now;
            }
            else
            {
                message.StatusCode = "pending";
                var backoffSeconds = Math.Min(1_800, 30 * (1 << Math.Min(message.AttemptCount - 1, 6)));
                message.NextAttemptAtUtc = now.AddSeconds(backoffSeconds + Random.Shared.Next(5, 31));
            }
        }
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}
