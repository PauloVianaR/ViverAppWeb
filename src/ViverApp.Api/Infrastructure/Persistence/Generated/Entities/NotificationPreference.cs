using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class NotificationPreference
{
    public ulong AccountId { get; set; }

    public bool? ReminderEmailEnabled { get; set; }

    public bool ReminderSmsEnabled { get; set; }

    public bool? PremiumUpdatesEnabled { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Account Account { get; set; } = null!;
}
