using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ViverApp.Api.Features.Identity;

public sealed class GoogleOnboardingProtector(IDataProtectionProvider provider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private readonly ITimeLimitedDataProtector protector = provider
        .CreateProtector("ViverApp.Identity.GoogleOnboarding.v1")
        .ToTimeLimitedDataProtector();

    public string Protect(string providerSubject, string email, string fullName) =>
        protector.Protect(JsonSerializer.Serialize(new GoogleOnboardingIdentity(
            providerSubject,
            email,
            fullName)), Lifetime);

    public bool TryUnprotect(string token, out GoogleOnboardingIdentity? identity)
    {
        identity = null;
        try
        {
            identity = JsonSerializer.Deserialize<GoogleOnboardingIdentity>(protector.Unprotect(token));
            return identity is not null
                && !string.IsNullOrWhiteSpace(identity.ProviderSubject)
                && IdentifierNormalizer.NormalizeEmail(identity.Email) is not null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return false;
        }
    }
}

public sealed record GoogleOnboardingIdentity(string ProviderSubject, string Email, string FullName);
