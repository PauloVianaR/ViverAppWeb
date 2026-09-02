using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ViverApp.Security;

public static class MinimalHealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/health+json";
        context.Response.Headers.CacheControl = "no-store";
        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            new { status = report.Status.ToString() },
            cancellationToken: context.RequestAborted);
    }
}
