using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Features.Identity;
using ViverApp.Security;

namespace ViverApp.Api.Features.AdministratorExperience;

public sealed class MaintenanceModeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ViverAppDbContext database)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/health") || path.StartsWithSegments("/api/v1/auth")
            || context.User.Identity?.IsAuthenticated != true
            || context.User.IsInRole(ViverAppRoles.Administrator))
        {
            await next(context);
            return;
        }

        var value = await database.ApplicationSettings.AsNoTracking()
            .Where(x => x.SettingKey == "web.maintenance_mode")
            .Select(x => x.ValueJson)
            .SingleOrDefaultAsync(context.RequestAborted);
        if (!string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.RetryAfter = "300";
        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.6.4",
            title = "O ViverApp está em manutenção programada.",
            status = 503,
            correlationId = CorrelationIdMiddleware.GetCorrelationId(context),
        }, cancellationToken: context.RequestAborted);
    }
}
