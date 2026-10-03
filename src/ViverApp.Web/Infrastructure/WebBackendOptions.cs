using Microsoft.Extensions.Hosting;

namespace ViverApp.Web;

public sealed class WebBackendOptions
{
    private WebBackendOptions(Uri baseUrl, bool smsEnabled)
    {
        BaseUrl = baseUrl;
        SmsEnabled = smsEnabled;
        GoogleLoginUrl = new Uri(baseUrl, "api/v1/auth/google/start").ToString();
        GoogleLinkUrl = new Uri(baseUrl, "api/v1/auth/google/link/start").ToString();
    }

    public Uri BaseUrl { get; }

    public bool SmsEnabled { get; }

    public string GoogleLoginUrl { get; }

    public string GoogleLinkUrl { get; }

    public static WebBackendOptions Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var value = configuration["Backend:BaseUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var baseUrl)
            || (baseUrl.Scheme != Uri.UriSchemeHttps
                && !(environment.IsDevelopment()
                    && baseUrl.Scheme == Uri.UriSchemeHttp
                    && IsLoopback(baseUrl.Host)))
            || !string.IsNullOrEmpty(baseUrl.UserInfo)
            || !string.IsNullOrEmpty(baseUrl.Query)
            || !string.IsNullOrEmpty(baseUrl.Fragment))
        {
            throw new InvalidOperationException(
                "Backend:BaseUrl deve ser uma URL HTTPS absoluta e sem credenciais; HTTP é aceito somente em loopback no Development.");
        }

        return new WebBackendOptions(
            new Uri(baseUrl.ToString().TrimEnd('/') + "/"),
            configuration.GetValue("Authentication:Delivery:SmsEnabled", true));
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(host, "::1", StringComparison.Ordinal);
}
