using Microsoft.AspNetCore.Builder;

namespace ViverApp.Security;

public static class SecurityApplicationBuilderExtensions
{
    public static IApplicationBuilder UseViverAppSecurityBaseline(
        this IApplicationBuilder app,
        SecuritySurface surface)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>(surface);
        app.UseMiddleware<SafeRequestLoggingMiddleware>();
        return app;
    }
}
