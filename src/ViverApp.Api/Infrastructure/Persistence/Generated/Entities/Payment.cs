using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class Payment
{
    public ulong Id { get; set; }

    public ulong AppointmentId { get; set; }

    public string ProviderCode { get; set; } = null!;

    public string StatusCode { get; set; } = null!;

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public Guid IdempotencyKey { get; set; }

    public string? ProviderCheckoutId { get; set; }

    public string? CheckoutUrl { get; set; }

    public DateTime? CheckoutExpiresAtUtc { get; set; }

    public string? ProviderTransactionId { get; set; }

    public string? ProviderStatusCode { get; set; }

    public DateTime? ProviderEventAtUtc { get; set; }

    public DateTime? LastReconciledAtUtc { get; set; }

    public DateTime? NextReconciliationAtUtc { get; set; }

    public ushort ReconciliationAttemptCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public DateTime? CanceledAtUtc { get; set; }

    public decimal? RefundAmount { get; set; }

    public DateTime? RefundedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public string? MethodCode { get; set; }

    public ulong? ProviderReferenceAppointmentId { get; set; }

    public ulong? ConfirmedByAccountId { get; set; }

    public string? CardLastFour { get; set; }

    public string? AuthorizationReference { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual Account? ConfirmedByAccount { get; set; }

    public virtual ICollection<PaymentEvent> PaymentEvents { get; set; } = new List<PaymentEvent>();

    public virtual Appointment? ProviderReferenceAppointment { get; set; }
}
