using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class PaymentReversalEvent
{
    public ulong Id { get; set; }

    public ulong PaymentReversalId { get; set; }

    public string? FromStatusCode { get; set; }

    public string ToStatusCode { get; set; } = null!;

    public string SourceCode { get; set; } = null!;

    public DateTime OccurredAtUtc { get; set; }

    public virtual PaymentReversal PaymentReversal { get; set; } = null!;
}
