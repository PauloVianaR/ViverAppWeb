using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ViverApp.Api.Features.Identity;
using ViverApp.Security;

namespace ViverApp.Api.Features.Payments;

[ApiController]
[Route("api/v1/patient/appointments/{appointmentId:long}/payment")]
[Authorize(Policy = ViverAppPolicies.Patient)]
public sealed class PatientPaymentsController(PaymentService payments) : ControllerBase
{
    [HttpPost("checkout")]
    [EnableRateLimiting(SecurityPolicyNames.CheckoutRateLimit)]
    public async Task<ActionResult<PaymentResponse>> CreateCheckout(
        ulong appointmentId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await payments.CreateCheckoutAsync(ActorId, appointmentId, idempotencyKey, cancellationToken);
            if (result.Replayed)
            {
                Response.Headers["Idempotent-Replayed"] = "true";
            }

            return Ok(result.Payment);
        }
        catch (PaymentRuleException exception)
        {
            return Problem(statusCode: exception.StatusCode, title: exception.Message);
        }
    }

    [HttpGet]
    public async Task<ActionResult<PaymentResponse>> Get(
        ulong appointmentId,
        [FromQuery] bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await payments.GetAsync(ActorId, appointmentId, refresh, cancellationToken));
        }
        catch (PaymentRuleException exception)
        {
            return Problem(statusCode: exception.StatusCode, title: exception.Message);
        }
    }

    private ulong ActorId => ulong.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier),
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var id)
        ? id
        : throw new InvalidOperationException("authenticated_account_id_missing");
}

[ApiController]
[Route("api/v1/management/payments")]
[Authorize(Policy = ViverAppPolicies.Management)]
public sealed class ManagementPaymentsController(PaymentService payments) : ControllerBase
{
    [HttpPost("{paymentId:long}/refund")]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    public async Task<ActionResult<PaymentResponse>> Refund(
        ulong paymentId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        [FromBody] PaymentRefundRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await payments.RefundAsync(ActorId, paymentId, idempotencyKey, request, cancellationToken));
        }
        catch (PaymentRuleException exception)
        {
            return Problem(statusCode: exception.StatusCode, title: exception.Message);
        }
    }

    private ulong ActorId => ulong.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier),
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var id)
        ? id
        : throw new InvalidOperationException("authenticated_account_id_missing");
}

[ApiController]
[Route("api/v1/payments/pagbank/webhook")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed class PagBankWebhookController(PaymentService payments) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(262_144)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var signature = Request.Headers[PagBankWebhookAuthenticator.HeaderName].ToString();
        try
        {
            var result = await payments.ProcessWebhookAsync(buffer.ToArray(), signature, cancellationToken);
            if (result.Replayed)
            {
                Response.Headers["Idempotent-Replayed"] = "true";
            }

            return NoContent();
        }
        catch (PaymentRuleException exception)
        {
            return Problem(statusCode: exception.StatusCode, title: exception.Message);
        }
    }
}

