using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientExperience;
using Xunit;

namespace ViverApp.Identity.Tests;

public sealed class IdentityApiSecurityTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Delivery:Enabled"] = "false",
                    ["GoogleOAuth:ClientID"] = "test-client-id",
                    ["GoogleOAuth:ProjectID"] = "test-project-id",
                    ["GoogleOAuth:ClientSecret"] = "test-client-secret",
                    ["GoogleOAuth:RedirectURI"] = "https://localhost:7176/signin-google",
                    ["Storage:Private:Provider"] = "Database",
                }));
        });

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task AnonymousMutationWithoutAntiforgeryToken_IsRejected()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login/password",
            new { identifier = "nobody@example.com", password = "invalid-password" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownAccountWithValidAntiforgeryToken_ReturnsGenericUnauthorized()
    {
        using var client = CreateClient();
        using var tokenResponse = await client.GetAsync("/api/v1/auth/antiforgery");
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDocument = JsonDocument.Parse(
            await tokenResponse.Content.ReadAsStringAsync());
        var token = tokenDocument.RootElement.GetProperty("requestToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login/password")
        {
            Content = JsonContent.Create(new
            {
                identifier = "nobody@example.com",
                password = "invalid-password",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Credenciais inválidas", body, StringComparison.Ordinal);
        Assert.DoesNotContain("nobody@example.com", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionsEndpoint_IsDenyByDefault()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void AdministratorAccountAlias_UsesActualAdministratorRole()
    {
        var policy = Assert.Single(typeof(PatientAccountController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        var roles = policy.Roles!.Split(',', StringSplitOptions.TrimEntries);
        Assert.Contains(ViverAppRoles.Administrator, roles);
        Assert.DoesNotContain("admin", roles);
    }

    [Fact]
    public void PasskeyRoutes_AreNoLongerRegistered()
    {
        using var client = CreateClient();
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty);
        Assert.DoesNotContain(routes, route =>
            route.Contains("auth/passkeys", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GoogleStart_UsesAuthorizationCodePkceAndExactCallback()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/google/start");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("accounts.google.com", location.Host);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"].ToString()));
        Assert.False(string.IsNullOrWhiteSpace(query["state"].ToString()));
        Assert.Equal("https://localhost:7176/signin-google", query["redirect_uri"].ToString());
    }

    [Fact]
    public async Task GoogleRegistrationStart_AcceptsOnlyPublicRoles()
    {
        using var client = CreateClient();

        using var validResponse = await client.GetAsync("/api/v1/auth/google/start?role=doctor");
        using var invalidResponse = await client.GetAsync("/api/v1/auth/google/start?role=administrator");

        Assert.Equal(HttpStatusCode.Redirect, validResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task RegistrationOptions_ListEveryRecognizedMedicalSpecialty()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/registration/options");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var specialties = body.RootElement.GetProperty("specialties");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-09-21", body.RootElement.GetProperty("termsVersion").GetString());
        Assert.Equal("2026-09-21", body.RootElement.GetProperty("privacyVersion").GetString());
        Assert.Equal("noindex, nofollow, noarchive", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.True(specialties.GetArrayLength() >= 55);
        Assert.Contains(
            specialties.EnumerateArray(),
            item => item.GetProperty("name").GetString() == "Oftalmologia");
    }

    [Fact]
    public async Task PublicRegistration_NeverAcceptsAdministratorRole()
    {
        using var client = CreateClient();
        using var tokenResponse = await client.GetAsync("/api/v1/auth/antiforgery");
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new
            {
                fullName = "Pessoa Teste",
                email = "pessoa@example.com",
                phone = "+5511999999999",
                password = "Senha-Forte-2026!",
                verificationChannel = "email",
                roleCode = "administrator",
                taxId = "52998224725",
                birthDate = "1990-01-01",
                termsAccepted = true,
                address = (object?)null,
                doctor = (object?)null,
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", tokenDocument.RootElement.GetProperty("requestToken").GetString());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("administrator", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateClient()
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost:7176"),
            HandleCookies = true,
        });
    }
}
