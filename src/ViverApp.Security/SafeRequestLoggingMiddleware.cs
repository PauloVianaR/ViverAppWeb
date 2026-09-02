using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ViverApp.Security;

public sealed class SafeRequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<SafeRequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var statusCode = StatusCodes.Status500InternalServerError;
        try
        {
            await next(context);
            statusCode = context.Response.StatusCode;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var endpoint = context.GetEndpoint()?.DisplayName ?? "<unmatched>";
            var correlationId = CorrelationIdMiddleware.GetCorrelationId(context);

            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                SecurityLog.RequestFailed(
                    logger,
                    context.Request.Method,
                    endpoint,
                    statusCode,
                    elapsed,
                    correlationId);
            }
            else if (statusCode >= StatusCodes.Status400BadRequest)
            {
                SecurityLog.RequestRejected(
                    logger,
                    context.Request.Method,
                    endpoint,
                    statusCode,
                    elapsed,
                    correlationId);
            }
            else
            {
                SecurityLog.RequestCompleted(
                    logger,
                    context.Request.Method,
                    endpoint,
                    statusCode,
                    elapsed,
                    correlationId);
            }
        }
    }
}
