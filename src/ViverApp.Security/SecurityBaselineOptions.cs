using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ViverApp.Security;

public sealed class SecurityBaselineOptions
{
    public const string SectionName = "Security";

    public string[] AllowedCorsOrigins { get; set; } = [];

    public string[] AllowedConnectSources { get; set; } = [];

    public string? DataProtectionKeysPath { get; set; }

    public string? DataProtectionCertificatePath { get; set; }

    public string? DataProtectionCertificatePassword { get; set; }

    internal static SecurityBaselineOptions Load(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration
            .GetSection(SectionName)
            .Get<SecurityBaselineOptions>() ?? new SecurityBaselineOptions();

        options.AllowedCorsOrigins = ValidateOrigins(
            options.AllowedCorsOrigins,
            nameof(AllowedCorsOrigins));
        options.AllowedConnectSources = ValidateOrigins(
            options.AllowedConnectSources,
            nameof(AllowedConnectSources));

        if (!environment.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(options.DataProtectionKeysPath))
            {
                throw new InvalidOperationException(
                    "Security:DataProtectionKeysPath é obrigatório fora de Development.");
            }

            if (string.IsNullOrWhiteSpace(options.DataProtectionCertificatePath)
                || string.IsNullOrWhiteSpace(options.DataProtectionCertificatePassword))
            {
                throw new InvalidOperationException(
                    "Certificado e senha para proteger o key ring são obrigatórios fora de Development.");
            }
        }

        return options;
    }

    private static string[] ValidateOrigins(IEnumerable<string>? configuredOrigins, string optionName)
    {
        var origins = configuredOrigins?
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        foreach (var origin in origins)
        {
            if (origin.Contains('*', StringComparison.Ordinal)
                || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                || uri.AbsolutePath != "/")
            {
                throw new InvalidOperationException(
                    $"Security:{optionName} contém uma origem inválida.");
            }

            if (uri.Scheme == Uri.UriSchemeHttp && !IsLoopback(uri.Host))
            {
                throw new InvalidOperationException(
                    $"Security:{optionName} aceita HTTP somente para loopback local.");
            }
        }

        return origins;
    }

    private static bool IsLoopback(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(host, "::1", StringComparison.Ordinal);
    }
}
