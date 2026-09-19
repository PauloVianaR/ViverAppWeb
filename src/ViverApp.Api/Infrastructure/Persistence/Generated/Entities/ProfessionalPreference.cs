using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ProfessionalPreference
{
    public ulong ProfessionalAccountId { get; set; }

    public bool EmailEnabled { get; set; }

    public bool SmsEnabled { get; set; }

    public bool OnlineEnabled { get; set; }

    public ushort MaxOnlineDaily { get; set; }

    public ushort MaxInPersonDaily { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual ProfessionalProfile ProfessionalAccount { get; set; } = null!;
}
