using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace ViverApp.Security;

public sealed class SecurityHeadersMiddleware(
    RequestDelegate next,
    SecurityBaselineOptions options,
    IHostEnvironment environment,
    SecuritySurface surface)
{
    public const string CspNonceItemName = "ViverApp.CspNonce";

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["X-XSS-Protection"] = "0";
        headers["Referrer-Policy"] = surface == SecuritySurface.Api
            ? "no-referrer"
            : "strict-origin-when-cross-origin";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";
        headers["Permissions-Policy"] = surface == SecuritySurface.Api
            ? "camera=(), microphone=(), geolocation=(), payment=(), usb=()"
            : "camera=(self), microphone=(self), geolocation=(), payment=(self), usb=()";

        if (surface == SecuritySurface.Api)
        {
            headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            headers["Content-Security-Policy"] =
                "default-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'; sandbox";
            headers.CacheControl = "no-store";
            headers.Pragma = "no-cache";
        }
        else
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            context.Items[CspNonceItemName] = nonce;
            headers["Content-Security-Policy"] = BuildWebContentSecurityPolicy(
                nonce,
                options.AllowedConnectSources,
                context.Request.Host,
                environment.IsDevelopment());
        }

        await next(context);
    }

    public static string? GetCspNonce(HttpContext? context)
    {
        return context?.Items.TryGetValue(CspNonceItemName, out var value) == true
            ? value as string
            : null;
    }

    private static string BuildWebContentSecurityPolicy(
        string nonce,
        IReadOnlyCollection<string> allowedConnectSources,
        HostString requestHost,
        bool isDevelopment)
    {
        var connectSources = new List<string> { "'self'" };
        if (requestHost.HasValue)
        {
            connectSources.Add($"wss://{requestHost.Value}");
            if (isDevelopment)
            {
                connectSources.Add($"ws://{requestHost.Value}");
            }
        }
        connectSources.AddRange(allowedConnectSources);
        foreach (var source in allowedConnectSources)
        {
            var origin = new Uri(source);
            connectSources.Add($"{(origin.Scheme == Uri.UriSchemeHttps ? "wss" : "ws")}://{origin.Authority}");
        }

        var directives = new List<string>
        {
            "default-src 'self'",
            "base-uri 'self'",
            "object-src 'none'",
            "frame-ancestors 'none'",
            "form-action 'self'",
            "img-src 'self' data:",
            "font-src 'self'",
            "style-src 'self'",
            $"script-src 'self' 'nonce-{nonce}' 'wasm-unsafe-eval'",
            $"connect-src {string.Join(' ', connectSources.Distinct(StringComparer.OrdinalIgnoreCase))}",
            "media-src 'self' blob:",
            "worker-src 'self' blob:",
            "manifest-src 'self'",
        };
        if (!isDevelopment)
        {
            directives.Add("upgrade-insecure-requests");
        }

        return string.Join("; ", directives);
    }
}
