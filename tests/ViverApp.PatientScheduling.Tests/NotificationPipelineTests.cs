using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.Notifications;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.PatientScheduling.Tests;

public sealed class NotificationPipelineTests
{
    [Fact]
    public async Task InternalMessage_UsesSeparateWorkerClaim()
    {
        var configuration = Configuration();
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        ulong id = 0;
        try
        {
            await using (var database = Context(configuration))
            {
                var message = new OutboxMessage
                {
                    ChannelCode = "internal", TemplateKey = "notification.queue_health",
                    TemplateVersion = 1, Recipient = "administrator",
                    PayloadJson = "{\"deadLetters\":1,\"oldestPendingMinutes\":10}",
                    StatusCode = "pending", IdempotencyKey = Guid.NewGuid(),
                    AttemptCount = 0, MaxAttempts = 5,
                    NextAttemptAtUtc = now.UtcDateTime, CreatedAtUtc = now.UtcDateTime,
                };
                database.OutboxMessages.Add(message);
                await database.SaveChangesAsync();
                id = message.Id;
            }
            await using (var database = Context(configuration))
            {
                var store = new OutboxStore(database, new FixedClock(now));
                Assert.Equal(id, (await store.ClaimInternalAsync("internal-test", CancellationToken.None))?.Id);
                Assert.True(await store.MarkSentAsync(id, "internal-test", null, CancellationToken.None));
            }
        }
        finally
        {
            if (id != 0)
            {
                await using var database = Context(configuration);
                await database.OutboxMessages.Where(item => item.Id == id).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task FailedDelivery_BacksOff_AndPermanentFailureGoesToDeadLetter()
    {
        var configuration = Configuration();
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        ulong id = 0;
        try
        {
            await using (var database = Context(configuration))
            {
                var message = new OutboxMessage
                {
                    ChannelCode = "email", TemplateKey = "manager.premium.approved",
                    TemplateVersion = 1, Recipient = "backoff-fixture@example.test",
                    PayloadJson = "{}", StatusCode = "processing", IdempotencyKey = Guid.NewGuid(),
                    LeaseOwner = "backoff-test", LeaseUntilUtc = now.UtcDateTime.AddMinutes(2),
                    AttemptCount = 0, MaxAttempts = 5,
                    NextAttemptAtUtc = now.UtcDateTime, CreatedAtUtc = now.UtcDateTime,
                };
                database.OutboxMessages.Add(message);
                await database.SaveChangesAsync();
                id = message.Id;
            }
            await using (var database = Context(configuration))
            {
                var store = new OutboxStore(database, new FixedClock(now));
                Assert.True(await store.MarkFailedAsync(id, "backoff-test", "provider_timeout",
                    false, CancellationToken.None));
                var after = await database.OutboxMessages.AsNoTracking().SingleAsync(item => item.Id == id);
                Assert.Equal("pending", after.StatusCode);
                Assert.InRange(after.NextAttemptAtUtc, now.UtcDateTime.AddSeconds(35),
                    now.UtcDateTime.AddSeconds(60));
            }
            await using (var database = Context(configuration))
            {
                var message = await database.OutboxMessages.SingleAsync(item => item.Id == id);
                message.StatusCode = "processing";
                message.LeaseOwner = "backoff-test";
                await database.SaveChangesAsync();
                var store = new OutboxStore(database, new FixedClock(now));
                Assert.True(await store.MarkFailedAsync(id, "backoff-test", "provider_rejected",
                    true, CancellationToken.None));
                Assert.Equal("dead_letter", message.StatusCode);
                Assert.Equal(now.UtcDateTime, message.CompletedAtUtc);
            }
        }
        finally
        {
            if (id != 0)
            {
                await using var database = Context(configuration);
                await database.OutboxMessages.Where(item => item.Id == id).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task ClaimedMessage_IsRecoveredAfterLeaseExpiry_AndOnlyCurrentOwnerCanComplete()
    {
        var configuration = Configuration();
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var key = Guid.NewGuid();
        ulong id = 0;
        try
        {
            await using (var database = Context(configuration))
            {
                var message = new OutboxMessage
                {
                    ChannelCode = "email", TemplateKey = "manager.premium.approved",
                    TemplateVersion = 1, Recipient = "notification-fixture@example.test",
                    PayloadJson = "{}", StatusCode = "pending", IdempotencyKey = key,
                    AttemptCount = 0, MaxAttempts = 5, NextAttemptAtUtc = now.UtcDateTime,
                    CreatedAtUtc = now.UtcDateTime,
                };
                database.OutboxMessages.Add(message);
                await database.SaveChangesAsync();
                id = message.Id;
            }

            await using (var database = Context(configuration))
            {
                var store = new OutboxStore(database, new FixedClock(now));
                var claimed = await store.ClaimAsync(false, "owner-first", CancellationToken.None);
                Assert.Equal(id, claimed?.Id);
            }
            await using (var database = Context(configuration))
                await database.OutboxMessages.Where(item => item.Id == id)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(item => item.LeaseUntilUtc, now.UtcDateTime.AddSeconds(-1)));
            await using (var database = Context(configuration))
            {
                var store = new OutboxStore(database, new FixedClock(now));
                var reclaimed = await store.ClaimAsync(false, "owner-second", CancellationToken.None);
                Assert.Equal(id, reclaimed?.Id);
                Assert.False(await store.MarkSentAsync(id, "owner-first", null, CancellationToken.None));
                Assert.True(await store.MarkSentAsync(id, "owner-second", "test-reference", CancellationToken.None));
            }
            await using (var database = Context(configuration))
            {
                var result = await database.OutboxMessages.AsNoTracking().SingleAsync(item => item.Id == id);
                Assert.Equal("sent", result.StatusCode);
                Assert.Equal("test-reference", result.ProviderReference);
            }
        }
        finally
        {
            if (id != 0)
            {
                await using var database = Context(configuration);
                await database.OutboxMessages.Where(item => item.Id == id).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task NotificationPreferences_DefaultsAndConcurrency_ArePerAccount()
    {
        var configuration = Configuration();
        ulong accountId = 0;
        ulong suppressionId = 0;
        string? email = null;
        try
        {
            await using (var database = Context(configuration))
            {
                var marker = Guid.NewGuid().ToString("N");
                email = $"notification-{marker}@example.test";
                var account = new Account
                {
                    RoleCode = ViverAppRoles.Patient, StatusCode = "active",
                    FullName = $"Notificação Teste {marker}",
                    Email = email,
                    NormalizedEmail = $"NOTIFICATION-{marker}@EXAMPLE.TEST",
                    EmailVerified = true, PhoneVerified = false,
                    SecurityStamp = RandomNumberGenerator.GetBytes(32),
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                    RowVersion = 1,
                };
                database.Accounts.Add(account);
                await database.SaveChangesAsync();
                accountId = account.Id;
            }
            await using (var database = Context(configuration))
            {
                var preferences = new NotificationPreferencesService(database, TimeProvider.System);
                Assert.Equal(new NotificationPreferencesResponse(true, false, true, 0),
                    await preferences.GetAsync(accountId, CancellationToken.None));
                var saved = await preferences.UpdateAsync(accountId,
                    new NotificationPreferencesUpdateRequest(false, true, false, 0), CancellationToken.None);
                Assert.Equal(new NotificationPreferencesResponse(false, true, false, 1), saved);
                var policy = new NotificationDeliveryPolicy(database, TimeProvider.System);
                var message = new OutboxMessage
                {
                    AccountId = accountId, ChannelCode = "email", TemplateKey = "manager.premium.approved",
                    TemplateVersion = 1, Recipient = email!,
                };
                Assert.Equal("premium_updates_disabled",
                    (await policy.EvaluateAsync(message, CancellationToken.None)).ReasonCode);
                var suppression = new NotificationSuppression
                {
                    ChannelCode = "email",
                    RecipientHash = NotificationDeliveryPolicy.HashRecipient("email", message.Recipient),
                    ReasonCode = "user_request", CreatedAtUtc = DateTime.UtcNow,
                };
                database.NotificationSuppressions.Add(suppression);
                await database.SaveChangesAsync();
                suppressionId = suppression.Id;
                Assert.Equal("destination_suppressed",
                    (await policy.EvaluateAsync(message, CancellationToken.None)).ReasonCode);
                await Assert.ThrowsAsync<NotificationRuleException>(() => preferences.UpdateAsync(accountId,
                    new NotificationPreferencesUpdateRequest(true, false, true, 0), CancellationToken.None));
            }
        }
        finally
        {
            if (accountId != 0)
            {
                await using var database = Context(configuration);
                if (suppressionId != 0)
                    await database.NotificationSuppressions.Where(item => item.Id == suppressionId)
                        .ExecuteDeleteAsync();
                await database.Accounts.Where(item => item.Id == accountId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task SmsBaratoAdapter_UsesDocumentedSendEndpoint_WithoutExternalRequest()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://sistema81.smsbarato.com.br/") };
        var options = new IdentityDeliveryOptions
        {
            SmtpHost = "smtp.example.test", SmtpPort = 587,
            SmtpUser = "test@example.test", SmtpPassword = "unused",
            SmsBaratoBaseUrl = client.BaseAddress,
            SmsBaratoApiKey = "fixture-key",
        };
        var sender = new SmsBaratoNotificationSender(client, options);
        Assert.Equal("12345", await sender.SendAsync("+5511999999999",
            "Lembrete da clínica às 08:00", CancellationToken.None));
        Assert.Equal("/send", handler.Path);
        Assert.Contains("dest=11999999999", handler.Body, StringComparison.Ordinal);
        Assert.Contains("clinica", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("cl%C3%ADnica", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmsBaratoAdapter_ClassifiesTransientAndPermanentFailures()
    {
        async Task<bool> PermanentAsync(HttpStatusCode status)
        {
            using var client = new HttpClient(new CapturingHandler(status, "unavailable"))
            { BaseAddress = new Uri("https://sistema81.smsbarato.com.br/") };
            var sender = new SmsBaratoNotificationSender(client, new IdentityDeliveryOptions
            {
                SmtpHost = "smtp.example.test", SmtpPort = 587,
                SmtpUser = "test@example.test", SmtpPassword = "unused",
                SmsBaratoBaseUrl = client.BaseAddress,
                SmsBaratoApiKey = "fixture-key",
            });
            var error = await Assert.ThrowsAsync<NotificationProviderException>(() =>
                sender.SendAsync("+5511999999999", "Lembrete de teste", CancellationToken.None));
            return error.Permanent;
        }
        Assert.False(await PermanentAsync(HttpStatusCode.TooManyRequests));
        Assert.True(await PermanentAsync(HttpStatusCode.BadRequest));
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddUserSecrets(typeof(NotificationPipelineTests).Assembly, optional: false).Build();

    private static ViverAppDbContext Context(IConfiguration configuration)
    {
        var connection = new MySqlConnectionStringBuilder(
            configuration.GetConnectionString("LocalConnection"));
        if (!string.Equals(connection.Database, "viverappweb", StringComparison.Ordinal))
            throw new InvalidOperationException("Os testes só podem usar viverappweb.");
        return new ViverAppDbContext(new DbContextOptionsBuilder<ViverAppDbContext>()
            .UseMySQL(connection.ConnectionString).Options);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingHandler(
        HttpStatusCode status = HttpStatusCode.OK, string responseBody = "12345") : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }
}
