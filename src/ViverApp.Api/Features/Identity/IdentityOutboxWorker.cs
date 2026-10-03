using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Notifications;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Identity;

public sealed partial class IdentityOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IDataProtectionProvider dataProtectionProvider,
    SmtpIdentitySender emailSender,
    IServiceProvider serviceProvider,
    IdentityDeliveryOptions deliveryOptions,
    IConfiguration configuration,
    ILogger<IdentityOutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromMinutes(2);
    private readonly string leaseOwner = $"identity-{Guid.NewGuid():N}";
    private readonly IDataProtector payloadProtector = dataProtectionProvider.CreateProtector(
        "ViverApp.Identity.OutboxMessage.v1");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Authentication:Delivery:Enabled", true))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            OutboxMessage? message = null;
            try
            {
                message = await ClaimAsync(stoppingToken);
                if (message is null)
                {
                    await Task.Delay(EmptyQueueDelay, stoppingToken);
                    continue;
                }

                await DeliverAsync(message, stoppingToken);
                await CompleteAsync(message.Id, succeeded: true, null, stoppingToken);
                NotificationTelemetry.RecordSent(message.ChannelCode, message.CreatedAtUtc, DateTime.UtcNow);
                DeliverySucceeded(logger, message.Id, message.ChannelCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                if (message is null)
                {
                    WorkerIterationFailed(logger, exception.GetType().Name);
                }
                else
                {
                    await TryRegisterFailureAsync(message, exception, stoppingToken);
                    NotificationTelemetry.RecordFailed(message.ChannelCode);
                    DeliveryFailed(
                        logger,
                        message.Id,
                        message.ChannelCode,
                        exception.GetType().Name);
                }

                await Task.Delay(EmptyQueueDelay, stoppingToken);
            }
        }
    }

    private async Task TryRegisterFailureAsync(
        OutboxMessage message,
        Exception deliveryException,
        CancellationToken cancellationToken)
    {
        try
        {
            await CompleteAsync(
                message.Id,
                succeeded: false,
                NormalizeErrorCode(deliveryException),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception persistenceException)
        {
            FailureRegistrationFailed(
                logger,
                message.Id,
                persistenceException.GetType().Name);
        }
    }

    private async Task<OutboxMessage?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var message = await database.OutboxMessages
            .FromSqlInterpolated($$"""
                SELECT *
                FROM outbox_messages
                WHERE template_key LIKE 'identity.%'
                  AND ({{deliveryOptions.SmsEnabled}} OR channel_code = 'email')
                  AND (
                    (status_code = 'pending' AND next_attempt_at_utc <= {{now}})
                    OR (status_code = 'processing' AND lease_until_utc < {{now}})
                  )
                ORDER BY id
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (message is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        message.StatusCode = "processing";
        message.LeaseOwner = leaseOwner;
        message.LeaseUntilUtc = now.Add(LeaseLifetime);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return message;
    }

    private async Task DeliverAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.TemplateVersion != 1)
            throw new InvalidOperationException("identity_template_version_invalid");
        var wrapper = JsonSerializer.Deserialize<ProtectedPayloadEnvelope>(message.PayloadJson)
            ?? throw new InvalidOperationException("identity_payload_envelope_invalid");
        var protectedPayload = Convert.FromBase64String(wrapper.ProtectedPayload);
        var payload = JsonSerializer.Deserialize<IdentityMessagePayload>(
                payloadProtector.Unprotect(protectedPayload))
            ?? throw new InvalidOperationException("identity_payload_invalid");
        if (payload.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("identity_payload_expired");
        }

        var template = ResolveTemplate(message.TemplateKey, payload.Code);
        if (message.ChannelCode == "email")
        {
            await emailSender.SendAsync(
                message.Recipient,
                template.Subject,
                template.Body,
                cancellationToken,
                message.IdempotencyKey);
        }
        else if (message.ChannelCode == "sms")
        {
            if (!deliveryOptions.SmsEnabled)
                throw new InvalidOperationException("identity_sms_unavailable");
            var smsSender = serviceProvider.GetRequiredService<SmsBaratoIdentitySender>();
            await smsSender.SendAsync(
                message.Recipient,
                template.SmsTemplate,
                payload.Code,
                cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("identity_channel_invalid");
        }
    }

    private async Task CompleteAsync(
        ulong messageId,
        bool succeeded,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        var message = await database.OutboxMessages.SingleOrDefaultAsync(
            item => item.Id == messageId
                && item.StatusCode == "processing"
                && item.LeaseOwner == leaseOwner,
            cancellationToken);
        if (message is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        message.LeaseOwner = null;
        message.LeaseUntilUtc = null;
        if (succeeded)
        {
            message.StatusCode = "sent";
            message.SentAtUtc = now;
            message.CompletedAtUtc = now;
            message.LastErrorCode = null;
        }
        else
        {
            message.AttemptCount++;
            message.LastErrorCode = errorCode;
            message.StatusCode = message.AttemptCount >= message.MaxAttempts
                ? "dead_letter"
                : "pending";
            message.CompletedAtUtc = message.StatusCode == "dead_letter" ? now : null;
            var backoffMinutes = Math.Min(30, 1 << Math.Min(message.AttemptCount, (ushort)5));
            message.NextAttemptAtUtc = now.AddMinutes(backoffMinutes)
                .AddSeconds(Random.Shared.Next(5, 31));
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private static IdentityTemplate ResolveTemplate(string key, string code)
    {
        return key switch
        {
            "identity.contact_change" => new IdentityTemplate(
                "Confirme seu novo contato ViverApp",
                $"Seu código para confirmar o novo contato é {code}. Ele expira em 10 minutos. Se não solicitou esta mudança, não compartilhe o código.",
                "sms_cadastro"),
            "identity.contact_verification" => new IdentityTemplate(
                "Confirme sua conta ViverApp",
                $"Seu código para confirmar a conta é {code}. Ele expira em 10 minutos. Não compartilhe este código.",
                "sms_cadastro"),
            "identity.login" => new IdentityTemplate(
                "Código de acesso ViverApp",
                $"Seu código de acesso é {code}. Ele expira em 10 minutos. Não compartilhe este código.",
                "sms_login_empresa"),
            "identity.password_reset" => new IdentityTemplate(
                "Redefinição de senha ViverApp",
                $"Seu código para redefinir a senha é {code}. Ele expira em 10 minutos. Não compartilhe este código.",
                "sms_recuperacao_senha"),
            "identity.professional_approved" => new IdentityTemplate(
                "Cadastro profissional aprovado",
                "Seu cadastro profissional foi aprovado. Você já pode entrar no ViverApp.",
                "sms_cadastro_aprovado"),
            "identity.professional_rejected" => new IdentityTemplate(
                "Atualização do cadastro profissional",
                "Seu cadastro profissional não foi aprovado. Entre em contato com a clínica para mais informações.",
                "sms_cadastro_rejeitado"),
            _ => throw new InvalidOperationException("identity_template_invalid"),
        };
    }

    private static string NormalizeErrorCode(Exception exception)
    {
        var code = exception switch
        {
            HttpRequestException { StatusCode: not null } http =>
                $"provider_http_{(int)http.StatusCode.Value}",
            TimeoutException => "provider_timeout",
            _ => exception.Message.StartsWith("identity_", StringComparison.Ordinal)
                || exception.Message.StartsWith("sms_", StringComparison.Ordinal)
                    ? exception.Message
                    : $"provider_{exception.GetType().Name.ToLowerInvariant()}",
        };
        return code.Length <= 100 ? code : code[..100];
    }

    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Information,
        Message = "Mensagem de identidade {MessageId} entregue pelo canal {Channel}.")]
    private static partial void DeliverySucceeded(ILogger logger, ulong messageId, string channel);

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Warning,
        Message = "Falha ao entregar mensagem de identidade {MessageId} pelo canal {Channel}. Tipo: {ErrorType}.")]
    private static partial void DeliveryFailed(
        ILogger logger,
        ulong messageId,
        string channel,
        string errorType);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Error,
        Message = "Falha na iteração do worker de identidade. Tipo: {ErrorType}.")]
    private static partial void WorkerIterationFailed(ILogger logger, string errorType);

    [LoggerMessage(
        EventId = 2103,
        Level = LogLevel.Error,
        Message = "Falha ao registrar a tentativa da mensagem de identidade {MessageId}. Tipo: {ErrorType}.")]
    private static partial void FailureRegistrationFailed(
        ILogger logger,
        ulong messageId,
        string errorType);

    private sealed record ProtectedPayloadEnvelope(
        [property: JsonPropertyName("protectedPayload")] string ProtectedPayload);

    private sealed record IdentityMessagePayload(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("expiresAtUtc")] DateTime ExpiresAtUtc);

    private sealed record IdentityTemplate(string Subject, string Body, string SmsTemplate);
}
