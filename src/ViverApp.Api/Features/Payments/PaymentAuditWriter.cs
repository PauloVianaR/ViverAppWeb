using ViverApp.Api.Features.Identity;

namespace ViverApp.Api.Features.Payments;

public interface IPaymentAuditWriter
{
    Task WriteAsync(
        string eventCode,
        ulong? actorAccountId,
        ulong paymentId,
        IReadOnlyDictionary<string, string>? safeData,
        CancellationToken cancellationToken);
}

internal sealed class PaymentAuditWriter(IdentityAuditWriter writer) : IPaymentAuditWriter
{
    public Task WriteAsync(
        string eventCode,
        ulong? actorAccountId,
        ulong paymentId,
        IReadOnlyDictionary<string, string>? safeData,
        CancellationToken cancellationToken) =>
        writer.WriteAsync(
            eventCode,
            actorAccountId,
            "payment",
            paymentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            safeData,
            cancellationToken);
}

