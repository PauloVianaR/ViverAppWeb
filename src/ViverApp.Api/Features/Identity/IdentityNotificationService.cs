using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Identity;

public sealed class IdentityNotificationService(
    ViverAppDbContext database,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration)
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(
        "ViverApp.Identity.OutboxMessage.v1");

    public async Task QueueAdministratorRegistrationAsync(Account account, CancellationToken ct)
    {
        var clinic = await database.Clinics.AsNoTracking().Select(c => c.DisplayName).SingleOrDefaultAsync(ct) ?? "Clínica Viver";
        var configuredUrl = configuration["Authentication:WebReturnUrl"];
        var origin = Uri.TryCreate(configuredUrl, UriKind.Absolute, out var configured)
            ? configured.GetLeftPart(UriPartial.Authority) : "https://viveralmenara.com";
        var now = DateTime.UtcNow;
        var payload = new AdministratorWelcomePayload(account.FullName, clinic, new Uri(new Uri(origin), "/acesso").AbsoluteUri, now.AddDays(7));
        _ = AdministratorWelcomeEmail.Render(payload);
        var protectedMessage = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(payload));
        database.OutboxMessages.Add(new OutboxMessage
        {
            AccountId = account.Id,
            ChannelCode = "email",
            TemplateKey = "identity.administrator_registration",
            TemplateVersion = 1,
            Recipient = account.Email!,
            PayloadJson = JsonSerializer.Serialize(new { protectedPayload = Convert.ToBase64String(protectedMessage), protectionPurpose = "ViverApp.Identity.OutboxMessage.v1" }),
            StatusCode = "pending",
            IdempotencyKey = Guid.NewGuid(),
            AttemptCount = 0,
            MaxAttempts = 5,
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
        });
    }

    public void QueueProfessionalReview(Account account, string decisionCode)
    {
        var channel = account.EmailVerified && account.Email is not null ? "email" : "sms";
        if (channel == "sms" && !configuration.GetValue("Authentication:Delivery:SmsEnabled", true))
            return;
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
            TemplateVersion = 1,
            Recipient = recipient,
            AccountId = account.Id,
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
