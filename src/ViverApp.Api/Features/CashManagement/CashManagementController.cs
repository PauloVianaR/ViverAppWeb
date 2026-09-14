using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Security;

namespace ViverApp.Api.Features.CashManagement;

public sealed class CashRuleExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var result = context.Exception switch
        {
            CashRuleException exception => (exception.StatusCode, exception.Message),
            DbUpdateConcurrencyException => (409, "Os dados financeiros foram alterados por outra sessão."),
            DbUpdateException => (409, "A operação financeira já foi processada ou viola uma regra do caixa."),
            _ => default,
        };
        if (result == default) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = result.Item1, Title = result.Item2 }) { StatusCode = result.Item1 };
        context.ExceptionHandled = true;
    }
}

[ApiController]
[Route("api/v1/management/cash")]
[ServiceFilter(typeof(CashRuleExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CashManagementController(CashManagementService service) : ControllerBase
{
    [HttpGet, Authorize(Policy = ViverAppPolicies.CashRead)]
    public Task<CashDayResponse> Day(DateOnly date, string? method = null, string? type = null,
        ulong? appointmentNumber = null, string? patient = null, string? responsible = null,
        string? cardLastFour = null, string? authorizationReference = null,
        int page = 1, int pageSize = 25, CancellationToken cancellationToken = default) =>
        service.DayAsync(date, method, type, appointmentNumber, patient, responsible, cardLastFour,
            authorizationReference, page, pageSize, cancellationToken);

    [HttpGet("print"), Authorize(Policy = ViverAppPolicies.CashPrint), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<CashPrintResponse> Print(DateOnly date, bool totalsOnly, string? method = null, string? type = null,
        ulong? appointmentNumber = null, string? patient = null, string? responsible = null,
        string? cardLastFour = null, string? authorizationReference = null,
        CancellationToken cancellationToken = default) =>
        service.PrintAsync(Actor, date, method, type, appointmentNumber, patient, responsible, cardLastFour,
            authorizationReference, totalsOnly, cancellationToken);

    [HttpPost("movements"), Authorize(Policy = ViverAppPolicies.CashWrite), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<CashMovementResponse> AddMovement([FromHeader(Name = "Idempotency-Key")] string key,
        CashManualMovementRequest request, CancellationToken cancellationToken) =>
        service.AddManualAsync(Actor, User.FindFirstValue(ClaimTypes.Role) ?? string.Empty, key, request, cancellationToken);

    [HttpPost("{date}/closure"), Authorize(Policy = ViverAppPolicies.CashClose), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<CashClosureResponse> Close(DateOnly date, CashCloseRequest request, CancellationToken cancellationToken) =>
        service.CloseAsync(Actor, date, request, cancellationToken);

    [HttpPost("payments/{paymentId:long}/reversal"), Authorize(Policy = ViverAppPolicies.PaymentReverse), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<PaymentReversalResponse> Reverse(ulong paymentId, [FromHeader(Name = "Idempotency-Key")] string key,
        PaymentReversalRequest request, CancellationToken cancellationToken) =>
        service.ReverseAsync(Actor, key, paymentId, request, cancellationToken);

    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
}
