using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class PaymentEvent
{
    public ulong Id { get; set; }

    public ulong PaymentId { get; set; }

    public ulong? WebhookReceiptId { get; set; }

    public string SourceCode { get; set; } = null!;

    public string? ProviderResourceId { get; set; }

    public string? ProviderStatusCode { get; set; }

    public string NormalizedStatusCode { get; set; } = null!;

    public byte[] EventFingerprint { get; set; } = null!;

    public DateTime? ProviderOccurredAtUtc { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public bool WasApplied { get; set; }

    public string? IgnoredReasonCode { get; set; }

    public virtual Payment Payment { get; set; } = null!;

    public virtual PaymentWebhookReceipt? WebhookReceipt { get; set; }
}
