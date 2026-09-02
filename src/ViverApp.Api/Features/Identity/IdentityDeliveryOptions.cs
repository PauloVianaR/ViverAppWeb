using System.Net.Mail;

namespace ViverApp.Api.Features.Identity;

public sealed class IdentityDeliveryOptions
{
    public required string SmtpHost { get; init; }

    public required int SmtpPort { get; init; }

    public required string SmtpUser { get; init; }

    public required string SmtpPassword { get; init; }

    public required Uri SmsBaratoBaseUrl { get; init; }

    public required string SmsBaratoApiKey { get; init; }

    public static IdentityDeliveryOptions Load(IConfiguration configuration)
    {
        var smtpHost = Required(configuration, "Smtp:Host");
        var smtpUser = Required(configuration, "Smtp:User");
        var smtpPassword = Required(configuration, "Smtp:Password");
        var smsApiKey = Required(configuration, "SmsBarato:ApiKey");
        if (!int.TryParse(configuration["Smtp:Port"], out var smtpPort)
            || smtpPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("Smtp:Port deve ser uma porta válida.");
        }

        if (smtpHost.Any(char.IsControl)
            || smtpHost.Contains('/', StringComparison.Ordinal)
            || smtpHost.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Smtp:Host é inválido.");
        }

        try
        {
            _ = new MailAddress(smtpUser);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Smtp:User deve ser um e-mail válido.", exception);
        }

        var baseUrlValue = Required(configuration, "SmsBarato:BaseUrl");
        if (!Uri.TryCreate(baseUrlValue, UriKind.Absolute, out var smsBaseUrl)
            || smsBaseUrl.Scheme != Uri.UriSchemeHttps
            || !string.Equals(
                smsBaseUrl.Host,
                "sistema81.smsbarato.com.br",
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(smsBaseUrl.UserInfo))
        {
            throw new InvalidOperationException(
                "SmsBarato:BaseUrl deve usar HTTPS no host oficial sistema81.smsbarato.com.br.");
        }

        return new IdentityDeliveryOptions
        {
            SmtpHost = smtpHost,
            SmtpPort = smtpPort,
            SmtpUser = smtpUser,
            SmtpPassword = smtpPassword,
            SmsBaratoBaseUrl = new Uri(smsBaseUrl.GetLeftPart(UriPartial.Authority) + "/"),
            SmsBaratoApiKey = smsApiKey,
        };
    }

    private static string Required(IConfiguration configuration, string key)
    {
        return string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"{key} deve ser configurado fora do repositório.")
            : configuration[key]!;
    }
}
