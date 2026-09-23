using System.Globalization;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ViverApp.Api.Features.Identity;

public sealed class SmtpIdentitySender(IdentityDeliveryOptions options)
{
    public async Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken,
        Guid? idempotencyKey = null)
    {
        var message = new MimeMessage
        {
            Subject = subject,
            Body = new TextPart("plain")
            {
                Text = body,
            },
        };
        if (idempotencyKey.HasValue)
            message.MessageId = $"{idempotencyKey.Value:N}@viveralmenara.com";
        message.From.Add(MailboxAddress.Parse(options.SmtpUser));
        message.To.Add(MailboxAddress.Parse(recipient));

        using var client = new SmtpClient();
        var socketOptions = options.SmtpPort == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;
        await client.ConnectAsync(
            options.SmtpHost,
            options.SmtpPort,
            socketOptions,
            cancellationToken);
        await client.AuthenticateAsync(
            options.SmtpUser,
            options.SmtpPassword,
            cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

public sealed class SmsBaratoIdentitySender(
    HttpClient client,
    IdentityDeliveryOptions options)
{
    public async Task SendAsync(
        string recipient,
        string template,
        string code,
        CancellationToken cancellationToken)
    {
        var destination = recipient.StartsWith("+55", StringComparison.Ordinal)
            ? recipient[3..]
            : throw new InvalidOperationException("sms_recipient_outside_brazil");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chave"] = options.SmsBaratoApiKey,
            ["dest"] = destination,
            ["template"] = template,
            ["empresa"] = "ViverApp",
            ["codigo"] = code,
        });
        using var response = await client.PostAsync("2fasend", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                "sms_provider_rejected",
                inner: null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("sent", out var sent)
            || sent.ValueKind != JsonValueKind.True)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"sms_provider_invalid_response_{(int)response.StatusCode}"));
        }
    }
}
