using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ViverApp.Security;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddViverAppSecurityBaseline(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        SecuritySurface surface)
    {
        var settings = SecurityBaselineOptions.Load(configuration, environment);
        services.AddSingleton(settings);
        services.AddHttpContextAccessor();
        services.AddSingleton<HoneypotDetector>();
        services.AddScoped<HoneypotActionFilter>();

        ConfigureDataProtection(services, settings, environment, surface);
        var allowInsecureLocalHttp = environment.IsDevelopment()
            && configuration.GetValue("Security:AllowInsecureLocalHttp", false);
        ConfigureAntiforgery(services, surface, allowInsecureLocalHttp);
        ConfigureRateLimiting(services, surface);

        if (surface == SecuritySurface.Api)
        {
            ConfigureApiBoundary(services, settings);
        }

        return services;
    }

    private static void ConfigureApiBoundary(
        IServiceCollection services,
        SecurityBaselineOptions settings)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(SecurityPolicyNames.WebClientCors, policy =>
            {
                if (settings.AllowedCorsOrigins.Length == 0)
                {
                    policy.SetIsOriginAllowed(_ => false);
                }
                else
                {
                    policy.WithOrigins(settings.AllowedCorsOrigins)
                        .AllowCredentials();
                }

                policy.WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                    .WithHeaders(
                        "Accept",
                        "Content-Type",
                        "Idempotency-Key",
                        "X-Clinical-Purpose",
                        "X-CSRF-TOKEN",
                        CorrelationIdMiddleware.HeaderName,
                        "traceparent",
                        "tracestate",
                        "baggage")
                    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "Retry-After", "Idempotent-Replayed", "Content-Disposition")
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            });
        });

        services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromSeconds(30),
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
                WriteTimeoutResponse = WriteTimeoutProblemAsync,
            };
            options.AddPolicy(
                SecurityPolicyNames.LongRunningRequestTimeout,
                TimeSpan.FromMinutes(2));
        });

    }

    private static void ConfigureAntiforgery(
        IServiceCollection services,
        SecuritySurface surface,
        bool allowInsecureLocalHttp)
    {
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = allowInsecureLocalHttp
                ? surface == SecuritySurface.Api
                    ? "ViverApp.Api.Antiforgery.Local"
                    : "ViverApp.Web.Antiforgery.Local"
                : surface == SecuritySurface.Api
                    ? "__Host-ViverApp.Api.Antiforgery"
                    : "__Host-ViverApp.Web.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = allowInsecureLocalHttp
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.FormFieldName = "__RequestVerificationToken";
            options.HeaderName = "X-CSRF-TOKEN";
            options.SuppressXFrameOptionsHeader = true;
        });
    }

    private static void ConfigureDataProtection(
        IServiceCollection services,
        SecurityBaselineOptions settings,
        IHostEnvironment environment,
        SecuritySurface surface)
    {
        var dataProtection = services.AddDataProtection()
            .SetApplicationName(surface == SecuritySurface.Api ? "ViverApp.Api" : "ViverApp.Web")
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

        if (string.IsNullOrWhiteSpace(settings.DataProtectionKeysPath))
        {
            ConfigureLocalDevelopmentKeyRing(dataProtection, environment, surface, settings.LocalDataProtectionScope);
            return;
        }

        var keysPath = ResolvePath(environment.ContentRootPath, settings.DataProtectionKeysPath);
        var certificatePath = ResolvePath(
            environment.ContentRootPath,
            settings.DataProtectionCertificatePath
                ?? throw new InvalidOperationException(
                    "Um certificado é obrigatório quando o key ring possui caminho explícito."));
        if (string.IsNullOrWhiteSpace(settings.DataProtectionCertificatePassword))
        {
            throw new InvalidOperationException(
                "A senha do certificado de Data Protection não foi configurada.");
        }

        Directory.CreateDirectory(keysPath);
        if (!File.Exists(certificatePath))
        {
            throw new InvalidOperationException(
                "O certificado configurado para Data Protection não foi encontrado.");
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            settings.DataProtectionCertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        dataProtection
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .ProtectKeysWithCertificate(certificate);
    }

    private static void ConfigureLocalDevelopmentKeyRing(
        IDataProtectionBuilder dataProtection,
        IHostEnvironment environment,
        SecuritySurface surface,
        string? localScope)
    {
        if (!environment.IsDevelopment() || !OperatingSystem.IsWindows())
        {
            return;
        }

        var repositoryRoot = Path.GetFullPath(
            Path.Combine(environment.ContentRootPath, "..", ".."));
        var surfaceDirectory = surface.ToString().ToLowerInvariant();
        var scopeSegments = string.IsNullOrWhiteSpace(localScope)
            ? new[] { "data-protection", surfaceDirectory }
            : new[] { "data-protection", "scopes", localScope, surfaceDirectory };
        var keysPath = Path.Combine(
            repositoryRoot,
            ".local",
            Path.Combine(scopeSegments));

        Directory.CreateDirectory(keysPath);
        dataProtection
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .ProtectKeysWithDpapi();
    }

    private static void ConfigureRateLimiting(
        IServiceCollection services,
        SecuritySurface surface)
    {
        var globalPermitLimit = surface == SecuritySurface.Api ? 120 : 300;
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(context),
                    _ => CreateFixedWindowOptions(
                        context.User.Identity?.IsAuthenticated == true ? 1_200 : globalPermitLimit,
                        TimeSpan.FromMinutes(1))));
            options.AddPolicy(
                SecurityPolicyNames.PublicFormRateLimit,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(context),
                    _ => CreateFixedWindowOptions(8, TimeSpan.FromMinutes(1))));
            options.AddPolicy(
                SecurityPolicyNames.SensitiveRateLimit,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(context),
                    _ => CreateFixedWindowOptions(10, TimeSpan.FromMinutes(5))));
            options.AddPolicy(
                SecurityPolicyNames.AuthenticatedOperationRateLimit,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(context),
                    _ => CreateFixedWindowOptions(600, TimeSpan.FromMinutes(1))));
            options.AddPolicy(
                SecurityPolicyNames.WriteRateLimit,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(context),
                    _ => CreateFixedWindowOptions(
                        context.User.Identity?.IsAuthenticated == true ? 300 : 30,
                        TimeSpan.FromMinutes(1))));
            options.OnRejected = OnRateLimitRejectedAsync;
            foreach (var (name, limit) in new[] { (SecurityPolicyNames.UploadRateLimit, 3), (SecurityPolicyNames.SlotRateLimit, 30), (SecurityPolicyNames.CheckoutRateLimit, 5), (SecurityPolicyNames.VideoRateLimit, 10) })
                options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => CreateFixedWindowOptions(limit, TimeSpan.FromMinutes(1))));
        });
    }

    private static FixedWindowRateLimiterOptions CreateFixedWindowOptions(
        int permitLimit,
        TimeSpan window)
    {
        return new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = permitLimit,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            Window = window,
        };
    }

    private static string GetPartitionKey(HttpContext context)
    {
        var accountId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(accountId)
            ? $"account:{accountId}"
            : $"address:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }

    private static async ValueTask OnRateLimitRejectedAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }

        var correlationId = CorrelationIdMiddleware.GetCorrelationId(context.HttpContext);
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("ViverApp.Security.RateLimiting");
        SecurityLog.RateLimitRejected(logger, correlationId);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Limite de requisições excedido.",
            Type = "https://www.rfc-editor.org/rfc/rfc9110#name-429-too-many-requests",
        };
        problemDetails.Extensions["correlationId"] = correlationId;
        await response.WriteAsJsonAsync(
            problemDetails,
            (JsonSerializerOptions?)null,
            "application/problem+json",
            cancellationToken);
    }

    private static Task WriteTimeoutProblemAsync(HttpContext context)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status504GatewayTimeout,
            Title = "A requisição excedeu o tempo permitido.",
            Type = "https://www.rfc-editor.org/rfc/rfc9110#name-504-gateway-timeout",
        };
        problemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.GetCorrelationId(context);
        return context.Response.WriteAsJsonAsync(
            problemDetails,
            (JsonSerializerOptions?)null,
            "application/problem+json",
            context.RequestAborted);
    }

    private static string ResolvePath(string contentRoot, string configuredPath)
    {
        return Path.GetFullPath(
            Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(contentRoot, configuredPath));
    }
}
