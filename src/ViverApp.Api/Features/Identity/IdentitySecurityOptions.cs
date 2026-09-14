using Microsoft.AspNetCore.WebUtilities;

namespace ViverApp.Api.Features.Identity;

public sealed class IdentitySecurityOptions
{
    public byte[] ChallengePepper { get; private init; } = [];

    public bool GoogleEnabled { get; private init; }

    public string? GoogleClientId { get; private init; }

    public string? GoogleClientSecret { get; private init; }

    public string? GoogleProjectId { get; private init; }

    public Uri? GoogleRedirectUri { get; private init; }

    public Uri? WebReturnUrl { get; private init; }

    public static IdentitySecurityOptions Load(IConfiguration configuration, bool allowInsecureLoopbackHttp = false)
    {
        var encodedPepper = configuration["Authentication:ChallengePepper"];
        byte[] pepper;
        try
        {
            pepper = string.IsNullOrWhiteSpace(encodedPepper)
                ? []
                : Convert.FromBase64String(encodedPepper);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Authentication:ChallengePepper deve ser Base64 válido.",
                exception);
        }

        if (pepper.Length < 32)
        {
            throw new InvalidOperationException(
                "Authentication:ChallengePepper deve conter pelo menos 32 bytes aleatórios.");
        }

        var clientId = configuration["GoogleOAuth:ClientID"];
        var projectId = configuration["GoogleOAuth:ProjectID"];
        var clientSecret = configuration["GoogleOAuth:ClientSecret"];
        var redirectUriValue = configuration["GoogleOAuth:RedirectURI"];
        var googleValues = new[] { clientId, projectId, clientSecret, redirectUriValue };
        var configuredGoogleValues = googleValues.Count(value => !string.IsNullOrWhiteSpace(value));
        if (configuredGoogleValues != 0 && configuredGoogleValues != googleValues.Length)
        {
            throw new InvalidOperationException(
                "GoogleOAuth deve possuir ClientID, ProjectID, ClientSecret e RedirectURI em conjunto.");
        }

        Uri? googleRedirectUri = null;
        if (configuredGoogleValues == googleValues.Length
            && (!Uri.TryCreate(redirectUriValue, UriKind.Absolute, out googleRedirectUri)
                || googleRedirectUri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(googleRedirectUri.UserInfo)
                || !string.IsNullOrEmpty(googleRedirectUri.Query)
                || !string.IsNullOrEmpty(googleRedirectUri.Fragment)
                || !string.Equals(googleRedirectUri.AbsolutePath, "/signin-google", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "GoogleOAuth:RedirectURI deve ser uma URL HTTPS absoluta com o caminho exato /signin-google.");
        }

        var returnUrlValue = configuration["Authentication:WebReturnUrl"];
        Uri? returnUrl = null;
        if (!string.IsNullOrWhiteSpace(returnUrlValue))
        {
            if (!Uri.TryCreate(returnUrlValue, UriKind.Absolute, out returnUrl)
                || (returnUrl.Scheme != Uri.UriSchemeHttps
                    && !(allowInsecureLoopbackHttp && returnUrl.Scheme == Uri.UriSchemeHttp && returnUrl.IsLoopback))
                || !string.IsNullOrEmpty(returnUrl.UserInfo))
            {
                throw new InvalidOperationException(
                    "Authentication:WebReturnUrl deve ser uma URL HTTPS absoluta e sem credenciais.");
            }
        }

        return new IdentitySecurityOptions
        {
            ChallengePepper = pepper,
            GoogleEnabled = !string.IsNullOrWhiteSpace(clientId),
            GoogleClientId = clientId?.Trim(),
            GoogleClientSecret = clientSecret?.Trim(),
            GoogleProjectId = projectId?.Trim(),
            GoogleRedirectUri = googleRedirectUri,
            WebReturnUrl = returnUrl,
        };
    }

    public string BuildWebReturnUrl(string result)
    {
        if (WebReturnUrl is null)
        {
            return $"/api/v1/auth/result?result={Uri.EscapeDataString(result)}";
        }

        return QueryHelpers.AddQueryString(WebReturnUrl.ToString(), "result", result);
    }

    public string BuildWebReturnUrl(string result, string name, string value)
    {
        var returnUrl = BuildWebReturnUrl(result);
        return QueryHelpers.AddQueryString(returnUrl, name, value);
    }
}
