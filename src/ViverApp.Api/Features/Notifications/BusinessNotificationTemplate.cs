using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Notifications;

internal sealed record RenderedNotification(string Subject, string EmailBody, string SmsBody);

internal sealed class BusinessNotificationTemplate(ViverAppDbContext database)
{
    public async Task<RenderedNotification?> RenderAsync(
        OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.TemplateVersion != 1)
            throw new NotificationProviderException("template_version_unknown", true);

        return message.TemplateKey switch
        {
            "manager.premium.approved" => await RenderPremiumAsync(message, "active", cancellationToken),
            "manager.premium.rejected" => await RenderPremiumAsync(message, "rejected", cancellationToken),
            "appointment.reminder" => await RenderReminderAsync(message, cancellationToken),
            _ => throw new NotificationProviderException("template_unknown", true),
        };
    }

    private async Task<RenderedNotification?> RenderPremiumAsync(
        OutboxMessage message, string expectedStatus, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(message.PayloadJson);
        if (!payload.RootElement.TryGetProperty("membershipId", out var idValue)
            || !idValue.TryGetUInt64(out var membershipId))
            throw new NotificationProviderException("premium_payload_invalid", true);
        var membership = await database.PremiumMemberships.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == membershipId, cancellationToken);
        if (membership is null || membership.AccountId != message.AccountId
            || membership.StatusCode != expectedStatus)
            return null;
        return expectedStatus == "active"
            ? new RenderedNotification("Benefício Premium aprovado — Viver",
                "Seu benefício Premium foi aprovado. Consulte os detalhes na sua conta Viver.",
                "Viver: seu beneficio Premium foi aprovado. Consulte sua conta.")
            : new RenderedNotification("Atualização do benefício Premium — Viver",
                "Sua solicitação de benefício Premium não foi aprovada. Consulte os detalhes na sua conta Viver ou entre em contato com a clínica.",
                "Viver: sua solicitacao Premium nao foi aprovada. Consulte sua conta.");
    }

    private async Task<RenderedNotification?> RenderReminderAsync(
        OutboxMessage message, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(message.PayloadJson);
        if (!payload.RootElement.TryGetProperty("appointmentId", out var idValue)
            || !idValue.TryGetUInt64(out var appointmentId)
            || !payload.RootElement.TryGetProperty("startsAtUtc", out var startValue)
            || !startValue.TryGetDateTime(out var originalStart))
            throw new NotificationProviderException("reminder_payload_invalid", true);
        var appointment = await database.Appointments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == appointmentId, cancellationToken);
        if (appointment is null || appointment.PatientAccountId != message.AccountId
            || appointment.StatusCode is not ("confirmed" or "arrived")
            || appointment.StartsAtUtc != originalStart)
            return null;
        var zoneName = await database.Clinics.AsNoTracking()
            .Select(item => item.TimezoneName).SingleAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneName);
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(appointment.StartsAtUtc, DateTimeKind.Utc), zone);
        var when = local.ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
        var smsWhen = local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        return new RenderedNotification(
            "Lembrete de atendimento — Viver",
            $"Lembrete: você tem um atendimento na clínica em {when}. Consulte os detalhes na sua conta Viver. Caso não possa comparecer, entre em contato com a clínica.",
            $"Viver: lembrete de atendimento em {smsWhen}. Consulte sua conta ou contate a clinica.");
    }
}
