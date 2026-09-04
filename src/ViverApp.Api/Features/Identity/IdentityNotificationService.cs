using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Identity;

public sealed class IdentityNotificationService(
    ViverAppDbContext database,
    IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(
        "ViverApp.Identity.OutboxMessage.v1");

    public void QueueProfessionalReview(Account account, string decisionCode)
    {
        var channel = account.EmailVerified && account.Email is not null ? "email" : "sms";
        var recipient = channel == "email" ? account.Email : account.PhoneE164;
        if (recipient is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var protectedMessage = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(new
        {
            code = decisionCode == "approved" ? "APROVADO" : "NÃO APROVADO",
            expiresAtUtc = now.AddDays(7),
        }));
        database.OutboxMessages.Add(new OutboxMessage
        {
            ChannelCode = channel,
            TemplateKey = decisionCode == "approved"
                ? "identity.professional_approved"
                : "identity.professional_rejected",
            Recipient = recipient,
            PayloadJson = JsonSerializer.Serialize(new
            {
                protectedPayload = Convert.ToBase64String(protectedMessage),
                protectionPurpose = "ViverApp.Identity.OutboxMessage.v1",
            }),
            StatusCode = "pending",
            IdempotencyKey = Guid.NewGuid(),
            AttemptCount = 0,
            MaxAttempts = 5,
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
        });
    }
}
