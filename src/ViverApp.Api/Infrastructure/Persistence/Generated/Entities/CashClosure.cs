using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class CashClosure
{
    public ulong Id { get; set; }

    public DateTime OperationalDate { get; set; }

    public ulong ClosedByAccountId { get; set; }

    public ulong? LastMovementId { get; set; }

    public decimal GrossEntries { get; set; }

    public decimal PaymentReversals { get; set; }

    public decimal Supplies { get; set; }

    public decimal Withdrawals { get; set; }

    public decimal AdjustmentsNet { get; set; }

    public decimal NetTotal { get; set; }

    public uint MovementCount { get; set; }

    public string TotalsByMethodJson { get; set; } = null!;

    public DateTime ClosedAtUtc { get; set; }

    public virtual Account ClosedByAccount { get; set; } = null!;

    public virtual CashMovement? LastMovement { get; set; }
}
