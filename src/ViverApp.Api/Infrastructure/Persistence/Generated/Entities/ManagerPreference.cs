using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ManagerPreference
{
    public ulong ManagerAccountId { get; set; }

    public bool EmailEnabled { get; set; }

    public bool SmsEnabled { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Account ManagerAccount { get; set; } = null!;
}
