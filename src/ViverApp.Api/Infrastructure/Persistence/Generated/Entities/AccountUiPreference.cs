using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AccountUiPreference
{
    public ulong AccountId { get; set; }

    public string AppointmentViewMode { get; set; } = null!;

    public string CalendarViewMode { get; set; } = null!;

    public bool DesktopSidebarCollapsed { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Account Account { get; set; } = null!;
}
