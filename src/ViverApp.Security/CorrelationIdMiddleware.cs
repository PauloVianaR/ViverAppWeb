using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ViverApp.Security;

public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    private const string ItemName = "ViverApp.CorrelationId";

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Items[ItemName] = correlationId;
        context.TraceIdentifier = correlationId;
        Activity.Current?.SetTag("viverapp.correlation_id", correlationId);
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
        }))
        {
            await next(context);
        }
    }

    public static string GetCorrelationId(HttpContext context)
    {
        return context.Items.TryGetValue(ItemName, out var value) && value is string correlationId
            ? correlationId
            : context.TraceIdentifier;
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName];
        if (supplied.Count == 1 && CorrelationIdFormat().IsMatch(supplied[0] ?? string.Empty))
        {
            return supplied[0]!;
        }

        var traceId = Activity.Current?.TraceId.ToString();
        return string.IsNullOrWhiteSpace(traceId) ? Guid.NewGuid().ToString("N") : traceId;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{15,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdFormat();
}
