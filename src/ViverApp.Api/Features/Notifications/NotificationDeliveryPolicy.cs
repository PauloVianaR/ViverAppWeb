using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Notifications;

internal sealed record NotificationDeliveryDecision(bool Allowed, string ReasonCode)
{
    public static NotificationDeliveryDecision Allow { get; } = new(true, "allowed");
}

internal sealed class NotificationDeliveryPolicy(ViverAppDbContext database, TimeProvider clock)
{
    public async Task<NotificationDeliveryDecision> EvaluateAsync(
        OutboxMessage message, CancellationToken cancellationToken)
    {
        var hash = HashRecipient(message.ChannelCode, message.Recipient);
        var now = clock.GetUtcNow().UtcDateTime;
        if (await database.NotificationSuppressions.AsNoTracking().AnyAsync(
                item => item.ChannelCode == message.ChannelCode
                    && item.RecipientHash == hash
                    && (item.ExpiresAtUtc == null || item.ExpiresAtUtc > now), cancellationToken))
            return new(false, "destination_suppressed");

        if (message.AccountId is null)
            return new(false, "account_missing");
        var account = await database.Accounts.AsNoTracking()
            .Include(item => item.NotificationPreference)
            .Include(item => item.AccountConsent)
            .Include(item => item.PatientPreference)
            .SingleOrDefaultAsync(item => item.Id == message.AccountId.Value, cancellationToken);
        if (account is null || account.StatusCode != "active")
            return new(false, "account_inactive");
        if (message.ChannelCode == "email"
            ? !account.EmailVerified || !string.Equals(account.Email, message.Recipient,
                StringComparison.OrdinalIgnoreCase)
            : !account.PhoneVerified || !string.Equals(account.PhoneE164, message.Recipient,
                StringComparison.Ordinal))
            return new(false, "contact_unverified_or_changed");

        if (message.TemplateKey == "appointment.reminder")
        {
            if (!account.PortalAccessEnabled || account.AccountConsent is null)
                return new(false, "reminder_consent_missing");
            if (message.ChannelCode == "email" && account.PatientPreference?.EmailEnabled == false)
                return new(false, "email_channel_disabled");
            if (message.ChannelCode == "sms" && account.PatientPreference?.SmsEnabled == false)
                return new(false, "sms_channel_disabled");
            if (message.ChannelCode == "email" && account.NotificationPreference?.ReminderEmailEnabled == false)
                return new(false, "email_reminder_disabled");
            if (message.ChannelCode == "sms" && account.NotificationPreference?.ReminderSmsEnabled != true)
                return new(false, "sms_reminder_disabled");
        }
        else if (message.TemplateKey.StartsWith("manager.premium.", StringComparison.Ordinal)
                 && account.NotificationPreference?.PremiumUpdatesEnabled == false)
            return new(false, "premium_updates_disabled");

        return NotificationDeliveryDecision.Allow;
    }

    public static byte[] HashRecipient(string channel, string recipient)
    {
        var normalized = channel == "email"
            ? recipient.Trim().ToUpperInvariant()
            : recipient.Trim();
        return SHA256.HashData(Encoding.UTF8.GetBytes($"{channel}:{normalized}"));
    }
}
