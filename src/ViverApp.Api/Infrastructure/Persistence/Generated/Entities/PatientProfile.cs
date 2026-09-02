using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class PatientProfile
{
    public ulong AccountId { get; set; }

    public string? TaxId { get; set; }

    public DateTime? BirthDate { get; set; }

    public string? PreferredName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public virtual Account Account { get; set; } = null!;
}
