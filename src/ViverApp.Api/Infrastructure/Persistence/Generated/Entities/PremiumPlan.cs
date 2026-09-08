using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class PremiumPlan
{
    public uint Id { get; set; }

    public string Name { get; set; } = null!;

    public decimal PriceAmount { get; set; }

    public decimal AppointmentDiscountPercent { get; set; }

    public ushort? ValidityDays { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual ICollection<PremiumMembership> PremiumMemberships { get; set; } = new List<PremiumMembership>();
}
