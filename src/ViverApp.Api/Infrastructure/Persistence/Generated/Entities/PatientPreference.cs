using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class PatientPreference
{
    public ulong AccountId { get; set; }

    public bool EmailEnabled { get; set; }

    public bool SmsEnabled { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public virtual Account Account { get; set; } = null!;
}
