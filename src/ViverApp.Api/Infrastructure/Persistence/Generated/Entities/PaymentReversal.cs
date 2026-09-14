using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class PaymentReversal
{
    public ulong Id { get; set; }

    public ulong PaymentId { get; set; }

    public ulong RequestedByAccountId { get; set; }

    public string StatusCode { get; set; } = null!;

    public string Reason { get; set; } = null!;

    public string IdempotencyKey { get; set; } = null!;

    public string? ProviderReference { get; set; }

    public string? FailureCode { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Payment Payment { get; set; } = null!;

    public virtual ICollection<PaymentReversalEvent> PaymentReversalEvents { get; set; } = new List<PaymentReversalEvent>();

    public virtual Account RequestedByAccount { get; set; } = null!;
}
