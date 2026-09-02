using System.Security.Claims;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ViverApp.Api.Features.Identity;
using Xunit;

namespace ViverApp.Identity.Tests;

public sealed class IdentitySecurityTests
{
    [Fact]
    public void PasswordHasher_UsesSaltedOneWayVersionedHashes()
    {
        var hasher = new PasswordHasher<ViverAppUser>();
        var user = new ViverAppUser();
        const string password = "Uma-Senha-Forte-2026!";

        var first = hasher.HashPassword(user, password);
        var second = hasher.HashPassword(user, password);

        Assert.NotEqual(first, second);
        Assert.DoesNotContain(password, first, StringComparison.Ordinal);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(user, first, password));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(user, first, "senha-incorreta"));
    }

    [Theory]
    [InlineData(" Pessoa@Example.COM ", "PESSOA@EXAMPLE.COM")]
    [InlineData("invalido", null)]
    [InlineData("nome@example.com extra", null)]
    public void EmailNormalization_IsStrict(string input, string? expected)
    {
        Assert.Equal(expected, IdentifierNormalizer.NormalizeEmail(input));
    }

    [Theory]
    [InlineData("+5511999999999", "+5511999999999")]
    [InlineData("+14155552671", null)]
    [InlineData("5511999999999", null)]
    [InlineData("+550", null)]
    [InlineData("+55119999999999999", null)]
    public void PhoneNormalization_RequiresE164(string input, string? expected)
    {
        Assert.Equal(expected, IdentifierNormalizer.NormalizePhone(input));
    }

    [Fact]
    public void Contracts_RejectUnknownInputMembers()
    {
        const string json = """
            {
              "identifier": "pessoa@example.com",
              "channel": "email",
              "role": "administrator"
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<IdentifierChallengeRequest>(json));
    }

    [Fact]
    public void IdentityConfiguration_RejectsPartialGoogleCredentials()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "client-id",
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            IdentitySecurityOptions.Load(configuration));

        Assert.Contains("ClientId e ClientSecret", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://example.com/auth/result")]
    [InlineData("https://user:password@example.com/auth/result")]
    [InlineData("/auth/result")]
    public void IdentityConfiguration_RejectsUnsafeReturnUrls(string returnUrl)
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Authentication:WebReturnUrl"] = returnUrl,
        });

        Assert.Throws<InvalidOperationException>(() =>
            IdentitySecurityOptions.Load(configuration));
    }

    [Fact]
    public void ReturnUrl_IsFixedByServerConfiguration()
    {
        var options = IdentitySecurityOptions.Load(CreateConfiguration());

        var result = options.BuildWebReturnUrl("success&returnUrl=https://evil.example");

        var uri = new Uri(result);
        Assert.Equal("localhost", uri.Host);
        Assert.Equal("/auth/result", uri.AbsolutePath);
        Assert.DoesNotContain("evil.example/", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorization_IsDenyByDefaultAndAdministratorRequiresMfa()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddViverAppIdentity(CreateConfiguration());
        await using var provider = services.BuildServiceProvider();

        var identityOptions = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.Equal(12, identityOptions.Password.RequiredLength);
        Assert.Equal(5, identityOptions.Lockout.MaxFailedAccessAttempts);

        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var defaultPolicy = await policies.GetDefaultPolicyAsync();
        AssertMfaRequirement(defaultPolicy);

        var adminPolicy = await policies.GetPolicyAsync(ViverAppPolicies.Administrator);
        Assert.NotNull(adminPolicy);
        AssertMfaRequirement(adminPolicy);
        var roles = Assert.Single(adminPolicy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Contains(ViverAppRoles.Administrator, roles.AllowedRoles);
    }

    [Fact]
    public async Task CookiesAreHostOnlySecureAndGoogleUsesPkce()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "client-id",
            ["Authentication:Google:ClientSecret"] = "client-secret",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddViverAppIdentity(configuration);
        await using var provider = services.BuildServiceProvider();

        var cookies = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var applicationCookie = cookies.Get(IdentityConstants.ApplicationScheme);
        Assert.Equal("__Host-ViverApp.Session", applicationCookie.Cookie.Name);
        Assert.True(applicationCookie.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, applicationCookie.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, applicationCookie.Cookie.SameSite);
        Assert.False(applicationCookie.SlidingExpiration);

        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.NotNull(await schemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme));
        var google = provider.GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme);
        Assert.True(google.UsePkce);
        Assert.False(google.SaveTokens);
        Assert.StartsWith(
            "__Host-ViverApp.Google.Correlation.",
            google.CorrelationCookie.Name,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RoleSet_IsExactAndSingleRoleStoreDoesNotRemoveRole()
    {
        Assert.Equal(
            ["administrator", "doctor", "manager", "patient"],
            ViverAppRoles.All.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task SmsSender_UsesOfficialHttpsEndpointAndKeepsKeyOutOfUrl()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://sistema81.smsbarato.com.br/"),
        };
        var sender = new SmsBaratoIdentitySender(
            client,
            IdentityDeliveryOptions.Load(CreateConfiguration()));

        await sender.SendAsync(
            "+5511999999999",
            "sms_login_empresa",
            "123456",
            CancellationToken.None);

        Assert.Equal(
            "https://sistema81.smsbarato.com.br/2fasend",
            handler.RequestUri?.ToString());
        Assert.DoesNotContain("test-only-api-key", handler.RequestUri?.ToString(), StringComparison.Ordinal);
        Assert.Contains("chave=test-only-api-key", handler.Body, StringComparison.Ordinal);
        Assert.Contains("dest=11999999999", handler.Body, StringComparison.Ordinal);
        Assert.Contains("codigo=123456", handler.Body, StringComparison.Ordinal);
    }

    private static void AssertMfaRequirement(AuthorizationPolicy policy)
    {
        Assert.Contains(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == ViverAppClaimTypes.MfaSatisfied
                && requirement.AllowedValues?.Contains(bool.TrueString) == true);
    }

    private static IConfiguration CreateConfiguration(
        Dictionary<string, string?>? additionalValues = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Authentication:ChallengePepper"] = Convert.ToBase64String(new byte[32]),
            ["Authentication:WebReturnUrl"] = "https://localhost:7110/auth/result",
            ["Authentication:Passkeys:ServerDomain"] = "localhost",
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = "587",
            ["Smtp:User"] = "sender@example.com",
            ["Smtp:Password"] = "test-only-password",
            ["SmsBarato:BaseUrl"] = "https://sistema81.smsbarato.com.br",
            ["SmsBarato:ApiKey"] = "test-only-api-key",
        };
        if (additionalValues is not null)
        {
            foreach (var value in additionalValues)
            {
                values[value.Key] = value.Value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"sent\":true}"),
            };
        }
    }
}
