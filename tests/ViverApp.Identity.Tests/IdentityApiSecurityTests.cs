using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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

    private HttpClient CreateClient()
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });
    }
}
