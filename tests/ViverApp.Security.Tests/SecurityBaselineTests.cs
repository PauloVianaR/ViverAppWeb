using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ViverApp.Security;
using Xunit;

namespace ViverApp.Security.Tests;

public sealed class SecurityBaselineTests
{
    [Fact]
    public async Task CorrelationIdMiddleware_PreservesOnlyValidClientIdentifiers()
    {
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        var validContext = new DefaultHttpContext();
        validContext.Request.Headers[CorrelationIdMiddleware.HeaderName] =
            "client-request-0001";

        await middleware.InvokeAsync(
            validContext,
            NullLogger<CorrelationIdMiddleware>.Instance);

        Assert.Equal(
            "client-request-0001",
            validContext.Response.Headers[CorrelationIdMiddleware.HeaderName]);

        var invalidContext = new DefaultHttpContext();
        invalidContext.Request.Headers[CorrelationIdMiddleware.HeaderName] = "short";

        await middleware.InvokeAsync(
            invalidContext,
            NullLogger<CorrelationIdMiddleware>.Instance);

        var generated = invalidContext.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.NotEqual("short", generated);
        Assert.Matches(new Regex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant), generated);
    }

    [Fact]
    public async Task ApiSecurityHeaders_BlockEmbeddingAndCaching()
    {
        var middleware = new SecurityHeadersMiddleware(
            _ => Task.CompletedTask,
            new SecurityBaselineOptions(),
            CreateEnvironment(Environments.Development),
            SecuritySurface.Api);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal("DENY", context.Response.Headers.XFrameOptions);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Contains(
            "default-src 'none'",
            context.Response.Headers.ContentSecurityPolicy.ToString());
    }

    [Fact]
    public async Task WebSecurityHeaders_UsePerRequestNonceWithoutUnsafeInline()
    {
        var middleware = new SecurityHeadersMiddleware(
            _ => Task.CompletedTask,
            new SecurityBaselineOptions
            {
                AllowedConnectSources = ["https://localhost:7176"],
            },
            CreateEnvironment(Environments.Development),
            SecuritySurface.Web);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        var nonce = SecurityHeadersMiddleware.GetCspNonce(context);
        var policy = context.Response.Headers.ContentSecurityPolicy.ToString();
        Assert.False(string.IsNullOrWhiteSpace(nonce));
        Assert.Contains($"'nonce-{nonce}'", policy);
        Assert.Contains("https://localhost:7176", policy);
        Assert.DoesNotContain("'unsafe-inline'", policy);
    }

    [Fact]
    public void HoneypotDetector_RejectsOnlyFilledTrapFields()
    {
        var detector = new HoneypotDetector();

        Assert.False(detector.IsTriggered([null, string.Empty, "  "]));
        Assert.True(detector.IsTriggered([null, "spam.example"]));
    }

    [Fact]
    public void ProductionBaseline_RequiresProtectedPersistentKeyRing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddViverAppSecurityBaseline(
                configuration,
                CreateEnvironment(Environments.Production),
                SecuritySurface.Api));

        Assert.Contains("DataProtectionKeysPath", exception.Message);
    }

    [Fact]
    public void Baseline_RejectsWildcardAndInsecureRemoteOrigins()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:AllowedCorsOrigins:0"] = "*",
                ["Security:AllowedCorsOrigins:1"] = "http://example.com",
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddViverAppSecurityBaseline(
                configuration,
                CreateEnvironment(Environments.Development),
                SecuritySurface.Api));

        Assert.Contains("origem inválida", exception.Message);
    }

    [Fact]
    public void DevelopmentBaseline_RejectsUnsafeLocalDataProtectionScope()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:LocalDataProtectionScope"] = "../shared-ring",
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddViverAppSecurityBaseline(
                configuration,
                CreateEnvironment(Environments.Development),
                SecuritySurface.Api));

        Assert.Contains("identificador local simples", exception.Message);
    }

    [Fact]
    public void DevelopmentBaseline_AllowsExplicitLocalHttpWithoutChangingTheSecureDefault()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:AllowInsecureLocalHttp"] = "true",
            })
            .Build();

        services.AddViverAppSecurityBaseline(
            configuration,
            CreateEnvironment(Environments.Development),
            SecuritySurface.Api);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        Assert.Equal("ViverApp.Api.Antiforgery.Local", options.Cookie.Name);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void AuthenticatedAccountsHaveAnOperationalGlobalQuotaWithoutRemovingAnonymousProtection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddViverAppSecurityBaseline(
            new ConfigurationBuilder().Build(),
            CreateEnvironment(Environments.Development),
            SecuritySurface.Api);

        using var provider = services.BuildServiceProvider();
        var limiter = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter!;
        var anonymous = new DefaultHttpContext();
        anonymous.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.10");
        for (var index = 0; index < 120; index++)
        {
            using var lease = limiter.AttemptAcquire(anonymous);
            Assert.True(lease.IsAcquired);
        }
        using (var rejected = limiter.AttemptAcquire(anonymous))
            Assert.False(rejected.IsAcquired);

        var authenticated = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "rate-limit-test-account")],
                "test"))
        };
        for (var index = 0; index < 200; index++)
        {
            using var lease = limiter.AttemptAcquire(authenticated);
            Assert.True(lease.IsAcquired);
        }
    }

    private static IWebHostEnvironment CreateEnvironment(string name)
    {
        return new TestWebHostEnvironment
        {
            EnvironmentName = name,
            ContentRootPath = AppContext.BaseDirectory,
            ContentRootFileProvider = new NullFileProvider(),
        };
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ViverApp.Security.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public string EnvironmentName { get; set; } = Environments.Development;

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
