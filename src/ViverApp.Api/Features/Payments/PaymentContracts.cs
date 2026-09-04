using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ViverApp.Api.Features.Payments;

public sealed record PaymentResponse(
    ulong Id,
    ulong AppointmentId,
    string StatusCode,
    decimal Amount,
    string CurrencyCode,
    string? CheckoutUrl,
    DateTime? CheckoutExpiresAtUtc,
    string? ProviderStatusCode,
    DateTime UpdatedAtUtc,
    DateTime? PaidAtUtc,
    DateTime? RefundedAtUtc,
    ulong RowVersion);

public sealed record PaymentCheckoutResult(PaymentResponse Payment, bool Replayed);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PaymentRefundRequest(
    [param: Range(1, long.MaxValue)] ulong RowVersion,
    [param: Required, StringLength(500, MinimumLength = 5)] string Reason) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Reason) || Reason.Trim().Length < 5)
        {
            yield return new ValidationResult("O motivo deve conter pelo menos 5 caracteres úteis.", [nameof(Reason)]);
        }
    }
}

public sealed record PagBankNotificationResult(bool Replayed, string ResultCode);

internal sealed class PaymentRuleException(int statusCode, string title) : Exception(title)
{
    public int StatusCode { get; } = statusCode;
}

