using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class PaymentPendingDailyNotice
{
    public ulong Id { get; set; }

    public ulong AccountId { get; set; }

    public DateTime OperationalDate { get; set; }

    public uint PendingCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    public virtual Account Account { get; set; } = null!;
}
