using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class PremiumMembership
{
    public ulong Id { get; set; }

    public ulong AccountId { get; set; }

    public uint PremiumPlanId { get; set; }

    public string StatusCode { get; set; } = null!;

    public DateTime? StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual PremiumPlan PremiumPlan { get; set; } = null!;
}
