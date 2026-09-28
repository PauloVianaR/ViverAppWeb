using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Notifications;

internal sealed class ReminderScheduler(ViverAppDbContext database, TimeProvider clock)
{
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromMinutes(2);

    public async Task<int> DiscoverAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var earliest = now.AddHours(1);
        var latest = now.AddDays(30);
        var appointments = await database.Appointments.AsNoTracking()
            .Where(item => (item.StatusCode == "confirmed" || item.StatusCode == "arrived")
                && item.StartsAtUtc >= earliest && item.StartsAtUtc <= latest)
            .OrderBy(item => item.StartsAtUtc)
            .Select(item => new { item.Id, item.StartsAtUtc })
            .Take(2_000)
            .ToArrayAsync(cancellationToken);
        var inserted = 0;
        foreach (var item in appointments)
        {
            var key = $"appointment_reminder:{item.Id}:{item.StartsAtUtc.Ticks}";
            var due = item.StartsAtUtc.AddHours(-24);
            if (due < now) due = now;
            inserted += await database.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT IGNORE INTO scheduled_jobs
                    (job_key, job_type_code, appointment_id, due_at_utc, status_code,
                     attempt_count, max_attempts, next_attempt_at_utc, created_at_utc)
                VALUES ({{key}}, 'appointment_reminder', {{item.Id}}, {{due}}, 'pending',
                        0, 5, {{due}}, {{now}})
                """, cancellationToken);
        }
        var healthKey = $"notification_health:{now:yyyyMMdd}";
        inserted += await database.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT IGNORE INTO scheduled_jobs
                (job_key, job_type_code, due_at_utc, status_code, attempt_count,
                 max_attempts, next_attempt_at_utc, created_at_utc)
            VALUES ({{healthKey}}, 'notification_health', {{now}}, 'pending', 0,
                    5, {{now}}, {{now}})
            """, cancellationToken);
        return inserted;
    }

    public async Task<ScheduledJob?> ClaimAsync(string owner, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var job = await database.ScheduledJobs.FromSqlInterpolated($$"""
            SELECT * FROM scheduled_jobs
            WHERE (status_code = 'pending' AND next_attempt_at_utc <= {{now}})
               OR (status_code = 'processing' AND lease_until_utc < {{now}})
            ORDER BY due_at_utc, id LIMIT 1 FOR UPDATE SKIP LOCKED
            """).SingleOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        job.StatusCode = "processing";
        job.LeaseOwner = owner;
        job.LeaseUntilUtc = now.Add(LeaseLifetime);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        database.Entry(job).State = EntityState.Detached;
        return job;
    }

    public async Task ProcessAsync(ulong jobId, string owner, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var job = await database.ScheduledJobs.FromSqlInterpolated($$"""
            SELECT * FROM scheduled_jobs WHERE id={{jobId}} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (job is null || job.StatusCode != "processing" || job.LeaseOwner != owner)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (job.JobTypeCode == "notification_health")
        {
            await ProcessHealthAsync(job, now, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        var appointment = job.AppointmentId.HasValue
            ? await database.Appointments.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == job.AppointmentId.Value, cancellationToken)
            : null;
        var startTicks = long.TryParse(job.JobKey.AsSpan(job.JobKey.LastIndexOf(':') + 1),
            out var parsedTicks) ? parsedTicks : 0;
        if (appointment is null || job.JobTypeCode != "appointment_reminder"
            || appointment.StartsAtUtc.Ticks != startTicks
            || appointment.StatusCode is not ("confirmed" or "arrived")
            || appointment.StartsAtUtc <= now)
        {
            Finish(job, "skipped", now, "event_stale");
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var account = await database.Accounts.AsNoTracking()
            .Include(item => item.NotificationPreference)
            .Include(item => item.AccountConsent)
            .Include(item => item.PatientPreference)
            .SingleAsync(item => item.Id == appointment.PatientAccountId, cancellationToken);
        if (account.StatusCode != "active" || !account.PortalAccessEnabled
            || account.AccountConsent is null)
        {
            Finish(job, "skipped", now, "reminder_consent_missing");
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var targets = new List<(string Channel, string Recipient)>();
        if (account.EmailVerified && account.Email is not null
            && account.PatientPreference?.EmailEnabled != false
            && account.NotificationPreference?.ReminderEmailEnabled != false)
            targets.Add(("email", account.Email));
        if (account.PhoneVerified && account.PhoneE164 is not null
            && account.PatientPreference?.SmsEnabled != false
            && account.NotificationPreference?.ReminderSmsEnabled == true)
            targets.Add(("sms", account.PhoneE164));

        foreach (var (channel, recipient) in targets)
        {
            var idempotencyKey = DeterministicId($"{job.JobKey}:{channel}");
            if (await database.OutboxMessages.AnyAsync(item => item.IdempotencyKey == idempotencyKey,
                    cancellationToken)) continue;
            database.OutboxMessages.Add(new OutboxMessage
            {
                AccountId = account.Id,
                ChannelCode = channel,
                TemplateKey = "appointment.reminder",
                TemplateVersion = 1,
                Recipient = recipient,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    appointmentId = appointment.Id,
                    startsAtUtc = appointment.StartsAtUtc,
                }),
                StatusCode = "pending",
                IdempotencyKey = idempotencyKey,
                AttemptCount = 0,
                MaxAttempts = 5,
                NextAttemptAtUtc = now,
                CreatedAtUtc = now,
            });
        }
        Finish(job, targets.Count > 0 ? "succeeded" : "skipped", now,
            targets.Count > 0 ? null : "no_verified_channel");
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ProcessHealthAsync(ScheduledJob job, DateTime now,
        CancellationToken cancellationToken)
    {
        var deadLetters = await database.OutboxMessages.CountAsync(
            item => item.StatusCode == "dead_letter", cancellationToken);
        var oldest = await database.OutboxMessages
            .Where(item => item.StatusCode == "pending")
            .MinAsync(item => (DateTime?)item.CreatedAtUtc, cancellationToken);
        var age = oldest.HasValue ? Math.Max(0, (int)(now - oldest.Value).TotalMinutes) : 0;
        if (deadLetters > 0 || age >= 10)
        {
            var idempotencyKey = DeterministicId(job.JobKey);
            if (!await database.OutboxMessages.AnyAsync(
                    item => item.IdempotencyKey == idempotencyKey, cancellationToken))
                database.OutboxMessages.Add(new OutboxMessage
                {
                    ChannelCode = "internal",
                    TemplateKey = "notification.queue_health",
                    TemplateVersion = 1,
                    Recipient = "administrator",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        deadLetters,
                        oldestPendingMinutes = age,
                    }),
                    StatusCode = "pending",
                    IdempotencyKey = idempotencyKey,
                    AttemptCount = 0,
                    MaxAttempts = 5,
                    NextAttemptAtUtc = now,
                    CreatedAtUtc = now,
                });
        }
        Finish(job, "succeeded", now, null);
    }

    public async Task MarkFailedAsync(ulong jobId, string owner, string code,
        CancellationToken cancellationToken)
    {
        var job = await database.ScheduledJobs.SingleOrDefaultAsync(item => item.Id == jobId
            && item.StatusCode == "processing" && item.LeaseOwner == owner, cancellationToken);
        if (job is null) return;
        var now = clock.GetUtcNow().UtcDateTime;
        job.AttemptCount++;
        job.LastErrorCode = code[..Math.Min(code.Length, 100)];
        job.LeaseOwner = null;
        job.LeaseUntilUtc = null;
        job.StatusCode = job.AttemptCount >= job.MaxAttempts ? "dead_letter" : "pending";
        if (job.StatusCode == "dead_letter") job.CompletedAtUtc = now;
        else job.NextAttemptAtUtc = now.AddSeconds(
            Math.Min(1_800, 30 * (1 << Math.Min(job.AttemptCount - 1, 6))) + Random.Shared.Next(5, 31));
        await database.SaveChangesAsync(cancellationToken);
    }

    private static void Finish(ScheduledJob job, string status, DateTime now, string? code)
    {
        job.StatusCode = status;
        job.LeaseOwner = null;
        job.LeaseUntilUtc = null;
        job.LastErrorCode = code;
        job.CompletedAtUtc = now;
    }

    private static Guid DeterministicId(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }
}
