using Microsoft.AspNetCore.WebUtilities;

namespace ViverApp.Api.Features.Identity;

public sealed class IdentitySecurityOptions
{
    public byte[] ChallengePepper { get; private init; } = [];

    public bool GoogleEnabled { get; private init; }

    public string? GoogleClientId { get; private init; }

    public string? GoogleClientSecret { get; private init; }

    public Uri? WebReturnUrl { get; private init; }

    public static IdentitySecurityOptions Load(IConfiguration configuration)
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

        var clientId = configuration["Authentication:Google:ClientId"];
        var clientSecret = configuration["Authentication:Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) != string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "As credenciais Google devem possuir ClientId e ClientSecret em conjunto.");
        }

        var returnUrlValue = configuration["Authentication:WebReturnUrl"];
        Uri? returnUrl = null;
        if (!string.IsNullOrWhiteSpace(returnUrlValue))
        {
            if (!Uri.TryCreate(returnUrlValue, UriKind.Absolute, out returnUrl)
                || returnUrl.Scheme != Uri.UriSchemeHttps
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
            GoogleClientId = clientId,
            GoogleClientSecret = clientSecret,
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
}
