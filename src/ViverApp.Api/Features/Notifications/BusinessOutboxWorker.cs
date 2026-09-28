using System.Net;
using System.Text.Json;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Notifications;

internal sealed class BusinessOutboxWorker(
    IServiceScopeFactory scopeFactory,
    INotificationEmailSender emailSender,
    INotificationSmsSender smsSender,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<BusinessOutboxWorker> logger) : BackgroundService
{
    private readonly string owner = $"business-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Notifications:BusinessDelivery:Enabled", false))
        {
            logger.LogInformation("Entrega externa de notificações de negócio desativada.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            OutboxMessage? message = null;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<OutboxStore>();
                message = await store.ClaimAsync(identityMessages: false, owner, stoppingToken);
                if (message is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                var policy = scope.ServiceProvider.GetRequiredService<NotificationDeliveryPolicy>();
                var decision = await policy.EvaluateAsync(message, stoppingToken);
                if (!decision.Allowed)
                {
                    await store.MarkSuppressedAsync(message.Id, owner, decision.ReasonCode, stoppingToken);
                    NotificationTelemetry.RecordSuppressed(message.ChannelCode);
                    continue;
                }

                var template = scope.ServiceProvider.GetRequiredService<BusinessNotificationTemplate>();
                var rendered = await template.RenderAsync(message, stoppingToken);
                if (rendered is null)
                {
                    await store.MarkSuppressedAsync(message.Id, owner, "event_stale", stoppingToken);
                    NotificationTelemetry.RecordSuppressed(message.ChannelCode);
                    continue;
                }

                using var deliveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deliveryTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                var reference = message.ChannelCode switch
                {
                    "email" => await emailSender.SendAsync(message.Recipient, rendered.Subject,
                        rendered.EmailBody, message.IdempotencyKey, deliveryTimeout.Token),
                    "sms" => await smsSender.SendAsync(message.Recipient, rendered.SmsBody,
                        deliveryTimeout.Token),
                    _ => throw new NotificationProviderException("channel_unknown", true),
                };
                if (await store.MarkSentAsync(message.Id, owner, reference, stoppingToken))
                {
                    NotificationTelemetry.RecordSent(message.ChannelCode, message.CreatedAtUtc,
                        clock.GetUtcNow().UtcDateTime);
                    logger.LogInformation("Notificação {MessageId} entregue pelo canal {Channel}.",
                        message.Id, message.ChannelCode);
                }
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
                        var store = scope.ServiceProvider.GetRequiredService<OutboxStore>();
                        var permanent = exception is NotificationProviderException { Permanent: true }
                            or JsonException
                            || exception is HttpRequestException
                            { StatusCode: >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError };
                        var code = exception is NotificationProviderException provider
                            ? provider.Message
                            : $"provider_{exception.GetType().Name.ToLowerInvariant()}";
                        await store.MarkFailedAsync(message.Id, owner, code[..Math.Min(100, code.Length)],
                            permanent, stoppingToken);
                        NotificationTelemetry.RecordFailed(message.ChannelCode);
                        logger.LogWarning("Falha na notificação {MessageId} pelo canal {Channel}: {ErrorCode}.",
                            message.Id, message.ChannelCode, code);
                    }
                    catch (Exception persistenceException) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError("Falha ao persistir resultado da notificação {MessageId}: {ErrorType}.",
                            message.Id, persistenceException.GetType().Name);
                    }
                }
                else
                    logger.LogError("Falha ao consultar a outbox: {ErrorType}.", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
