using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class PaymentWebhookReceipt
{
    public ulong Id { get; set; }

    public string ProviderCode { get; set; } = null!;

    public byte[] PayloadSha256 { get; set; } = null!;

    public byte[] AuthenticitySha256 { get; set; } = null!;

    public string? ProviderResourceId { get; set; }

    public string ProcessingStatusCode { get; set; } = null!;

    public string? ResultCode { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public virtual PaymentEvent? PaymentEvent { get; set; }
}
