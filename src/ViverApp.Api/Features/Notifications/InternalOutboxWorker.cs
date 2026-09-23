using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Notifications;

internal sealed class InternalNotificationHandler(ViverAppDbContext database, TimeProvider clock)
{
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.ChannelCode != "internal" || message.TemplateKey != "notification.queue_health"
            || message.TemplateVersion != 1)
            throw new NotificationProviderException("internal_event_unknown", true);

        using var document = JsonDocument.Parse(message.PayloadJson);
        if (!document.RootElement.TryGetProperty("deadLetters", out var deadLettersValue)
            || !deadLettersValue.TryGetInt32(out var deadLetters)
            || !document.RootElement.TryGetProperty("oldestPendingMinutes", out var ageValue)
            || !ageValue.TryGetInt32(out var age) || deadLetters < 0 || age < 0)
            throw new NotificationProviderException("internal_payload_invalid", true);

        var source = $"notification-health:{message.IdempotencyKey:N}";
        var now = clock.GetUtcNow().UtcDateTime;
        var body = $"Há {deadLetters} mensagem(ns) na fila de falhas; a mais antiga pendente aguarda {age} minuto(s). Revise a operação de notificações.";
        await database.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT IGNORE INTO administrator_notifications
                (administrator_account_id, source_key, type_code, severity_code,
                 title, message, created_at_utc, row_version)
            SELECT id, {{source}}, 'system_update', 'warning',
                   'Atenção à fila de notificações', {{body}}, {{now}}, 1
            FROM accounts WHERE role_code = 'administrator' AND status_code = 'active'
            """, cancellationToken);
    }
}

internal sealed class InternalOutboxWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<InternalOutboxWorker> logger) : BackgroundService
{
    private readonly string owner = $"internal-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            OutboxMessage? message = null;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<OutboxStore>();
                message = await store.ClaimInternalAsync(owner, stoppingToken);
                if (message is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }
                await scope.ServiceProvider.GetRequiredService<InternalNotificationHandler>()
                    .HandleAsync(message, stoppingToken);
                await store.MarkSentAsync(message.Id, owner, null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                if (message is not null)
                {
                    try
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var permanent = exception is NotificationProviderException { Permanent: true }
                            or JsonException;
                        await scope.ServiceProvider.GetRequiredService<OutboxStore>()
                            .MarkFailedAsync(message.Id, owner,
                                permanent ? exception.Message : "internal_processing_failed",
                                permanent, stoppingToken);
                    }
                    catch (Exception persistenceException) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError("Falha ao registrar processamento interno {MessageId}: {ErrorType}.",
                            message.Id, persistenceException.GetType().Name);
                    }
                }
                logger.LogWarning("Falha no evento interno {MessageId}: {ErrorType}.",
                    message?.Id, exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
