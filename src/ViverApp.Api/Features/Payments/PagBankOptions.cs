using Microsoft.Extensions.Hosting;

namespace ViverApp.Api.Features.Payments;

public sealed class PagBankOptions
{
    public const string SectionName = "PagBank";
    private const string SandboxHost = "sandbox.api.pagseguro.com";
    private const string ProductionHost = "api.pagseguro.com";

    public bool Enabled { get; init; }
    public string Environment { get; init; } = "Sandbox";
    public bool ProductionEnabled { get; init; }
    public bool RefundsEnabled { get; init; }
    public required Uri ApiBaseUrl { get; init; }
    public required Uri WebPublicBaseUrl { get; init; }
    public required Uri ApiPublicBaseUrl { get; init; }
    public required string Token { get; init; }
    public int CheckoutLifetimeMinutes { get; init; } = 120;
    public int ReconciliationIntervalMinutes { get; init; } = 5;
    public int ReconciliationBatchSize { get; init; } = 20;

    public bool IsProduction => string.Equals(Environment, "Production", StringComparison.Ordinal);
    public Uri ReturnUrl => new(WebPublicBaseUrl, "paciente/pagamentos/retorno");
    public Uri WebhookUrl => new(ApiPublicBaseUrl, "api/v1/payments/pagbank/webhook");

    public static PagBankOptions Load(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        var section = configuration.GetSection(SectionName);
        var enabled = section.GetValue("Enabled", false);
        var selectedEnvironment = section["Environment"]?.Trim() ?? "Sandbox";
        if (selectedEnvironment is not ("Sandbox" or "Production"))
        {
            throw new InvalidOperationException("PagBank:Environment deve ser Sandbox ou Production.");
        }

        var production = selectedEnvironment == "Production";
        var productionEnabled = section.GetValue("ProductionEnabled", false);
        if (enabled && production && !productionEnabled)
        {
            throw new InvalidOperationException(
                "O PagBank em produção exige PagBank:ProductionEnabled=true de forma explícita.");
        }

        var apiBaseUrl = RequiredOfficialApiUrl(
            section[production ? "ProductionUrl" : "SandboxUrl"]
                ?? (production ? "https://api.pagseguro.com/" : "https://sandbox.api.pagseguro.com/"),
            production ? ProductionHost : SandboxHost);
        var webBaseUrl = PublicBaseUrl(
            section["WebPublicBaseUrl"] ?? "https://localhost:7110/",
            hostEnvironment,
            "PagBank:WebPublicBaseUrl");
        var apiPublicBaseUrl = PublicBaseUrl(
            section["ApiPublicBaseUrl"] ?? "https://localhost:7087/",
            hostEnvironment,
            "PagBank:ApiPublicBaseUrl");
        var token = section[production ? "TokenProduction" : "TokenSandbox"]?.Trim() ?? string.Empty;
        if (enabled && string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                $"O token PagBank de {selectedEnvironment} deve ser fornecido por user-secrets ou pelo cofre do ambiente.");
        }

        var options = new PagBankOptions
        {
            Enabled = enabled,
            Environment = selectedEnvironment,
            ProductionEnabled = productionEnabled,
            RefundsEnabled = section.GetValue("RefundsEnabled", false),
            ApiBaseUrl = apiBaseUrl,
            WebPublicBaseUrl = webBaseUrl,
            ApiPublicBaseUrl = apiPublicBaseUrl,
            Token = token,
            CheckoutLifetimeMinutes = section.GetValue("CheckoutLifetimeMinutes", 120),
            ReconciliationIntervalMinutes = section.GetValue("ReconciliationIntervalMinutes", 5),
            ReconciliationBatchSize = section.GetValue("ReconciliationBatchSize", 20),
        };
        options.Validate();
        if (enabled && (webBaseUrl.IsLoopback || apiPublicBaseUrl.IsLoopback))
        {
            throw new InvalidOperationException("O PagBank habilitado exige URLs públicas HTTPS; loopback não é aceito.");
        }

        return options;
    }

    private void Validate()
    {
        if (CheckoutLifetimeMinutes is < 15 or > 1440
            || ReconciliationIntervalMinutes is < 1 or > 60
            || ReconciliationBatchSize is < 1 or > 100)
        {
            throw new InvalidOperationException("Os limites operacionais do PagBank são inválidos.");
        }

        if (WebhookUrl.AbsoluteUri.Length > 100)
        {
            throw new InvalidOperationException("A URL pública do webhook PagBank excede 100 caracteres.");
        }

        if (ReturnUrl.AbsoluteUri.Length > 220)
        {
            throw new InvalidOperationException("A URL pública de retorno PagBank é longa demais para incluir o agendamento.");
        }
    }

    private static Uri RequiredOfficialApiUrl(string value, string expectedHost)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("A URL da API PagBank deve apontar ao host oficial do ambiente.");
        }

        return new Uri(uri.ToString().TrimEnd('/') + "/");
    }

    private static Uri PublicBaseUrl(string value, IHostEnvironment environment, string key)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.Scheme != Uri.UriSchemeHttps
                && !(environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            throw new InvalidOperationException($"{key} deve ser uma URL HTTPS pública sem credenciais.");
        }

        return new Uri(uri.ToString().TrimEnd('/') + "/");
    }
}
