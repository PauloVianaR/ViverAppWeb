namespace ViverApp.Security;

public sealed class ObservabilityOptions
{
    public const string SectionName = "OpenTelemetry";

    public string? OtlpEndpoint { get; set; }

    public double TraceSamplingRatio { get; set; } = 0.1;

    internal Uri? ValidateAndGetEndpoint(bool isDevelopment)
    {
        if (TraceSamplingRatio is <= 0 or > 1)
        {
            throw new InvalidOperationException(
                "OpenTelemetry:TraceSamplingRatio deve ser maior que zero e menor ou igual a um.");
        }

        if (string.IsNullOrWhiteSpace(OtlpEndpoint))
        {
            return null;
        }

        if (!Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new InvalidOperationException("OpenTelemetry:OtlpEndpoint é inválido.");
        }

        if (!isDevelopment && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("O endpoint OTLP deve usar HTTPS fora de Development.");
        }

        return endpoint;
    }
}
