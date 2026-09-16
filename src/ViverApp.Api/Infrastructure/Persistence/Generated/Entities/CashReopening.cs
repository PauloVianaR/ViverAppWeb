using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class CashReopening
{
    public ulong Id { get; set; }

    public ulong CashClosureId { get; set; }

    public ulong ReopenedByAccountId { get; set; }

    public string Reason { get; set; } = null!;

    public DateTime ReopenedAtUtc { get; set; }

    public virtual CashClosure CashClosure { get; set; } = null!;

    public virtual Account ReopenedByAccount { get; set; } = null!;
}
