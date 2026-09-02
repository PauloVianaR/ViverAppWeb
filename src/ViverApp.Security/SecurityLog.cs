using Microsoft.Extensions.Logging;

namespace ViverApp.Security;

internal static partial class SecurityLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Requisição recusada por rate limiting. CorrelationId: {CorrelationId}")]
    public static partial void RateLimitRejected(ILogger logger, string correlationId);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Formulário recusado pelo honeypot. CorrelationId: {CorrelationId}")]
    public static partial void HoneypotTriggered(ILogger logger, string correlationId);

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "HTTP {Method} {Endpoint} respondeu {StatusCode} em {ElapsedMilliseconds} ms. CorrelationId: {CorrelationId}")]
    public static partial void RequestCompleted(
        ILogger logger,
        string method,
        string endpoint,
        int statusCode,
        double elapsedMilliseconds,
        string correlationId);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = "HTTP {Method} {Endpoint} respondeu {StatusCode} em {ElapsedMilliseconds} ms. CorrelationId: {CorrelationId}")]
    public static partial void RequestRejected(
        ILogger logger,
        string method,
        string endpoint,
        int statusCode,
        double elapsedMilliseconds,
        string correlationId);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Error,
        Message = "HTTP {Method} {Endpoint} respondeu {StatusCode} em {ElapsedMilliseconds} ms. CorrelationId: {CorrelationId}")]
    public static partial void RequestFailed(
        ILogger logger,
        string method,
        string endpoint,
        int statusCode,
        double elapsedMilliseconds,
        string correlationId);
}
