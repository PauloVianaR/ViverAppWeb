using System.Globalization;
using System.Text;
using ViverApp.Api.Features.Identity;

namespace ViverApp.Api.Features.Notifications;

internal interface INotificationEmailSender
{
    Task<string> SendAsync(string recipient, string subject, string body,
        Guid idempotencyKey, CancellationToken cancellationToken);
}

internal interface INotificationSmsSender
{
    Task<string> SendAsync(string recipient, string body, CancellationToken cancellationToken);
}

internal sealed class SmtpNotificationSender(SmtpIdentitySender sender) : INotificationEmailSender
{
    public async Task<string> SendAsync(string recipient, string subject, string body,
        Guid idempotencyKey, CancellationToken cancellationToken)
    {
        await sender.SendAsync(recipient, subject, body, cancellationToken, idempotencyKey);
        return $"{idempotencyKey:N}@viveralmenara.com";
    }
}

internal sealed class NotificationProviderException(string code, bool permanent) : Exception(code)
{
    public bool Permanent { get; } = permanent;
}

internal sealed class SmsBaratoNotificationSender(
    HttpClient client, IdentityDeliveryOptions options) : INotificationSmsSender
{
    public async Task<string> SendAsync(string recipient, string body,
        CancellationToken cancellationToken)
    {
        if (!recipient.StartsWith("+55", StringComparison.Ordinal)
            || recipient.Length is < 13 or > 14
            || !recipient[1..].All(char.IsAsciiDigit))
            throw new NotificationProviderException("sms_destination_invalid", true);

        var ascii = RemoveAccents(body);
        if (ascii.Length is < 1 or > 160 || ascii.Any(c => c is < ' ' or > '~'))
            throw new NotificationProviderException("sms_text_invalid", true);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chave"] = options.SmsBaratoApiKey,
            ["dest"] = recipient[3..],
            ["text"] = ascii,
        });
        using var response = await client.PostAsync("send", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new NotificationProviderException($"sms_http_{(int)response.StatusCode}",
                (int)response.StatusCode is >= 400 and < 500 and not 429);
        var result = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (result.StartsWith("ERRO", StringComparison.OrdinalIgnoreCase))
            throw new NotificationProviderException(result switch
            {
                "ERRO1-1" => "sms_key_invalid",
                "ERRO1-2" => "sms_ip_unauthorized",
                "ERRO1-3" => "sms_balance_insufficient",
                "ERRO2" => "sms_destination_invalid",
                "ERRO3" => "sms_text_invalid",
                _ => "sms_provider_rejected",
            }, result is "ERRO1-1" or "ERRO1-2" or "ERRO1-3" or "ERRO2" or "ERRO3");
        if (!ulong.TryParse(result, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            throw new NotificationProviderException("sms_response_invalid", false);
        return result;
    }

    private static string RemoveAccents(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(character switch { '–' or '—' => '-', '“' or '”' => '"', '’' => '\'', _ => character });
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
