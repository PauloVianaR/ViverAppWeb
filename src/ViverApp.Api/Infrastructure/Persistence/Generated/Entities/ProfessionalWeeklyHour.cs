using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ProfessionalWeeklyHour
{
    public ulong Id { get; set; }

    public ulong ProfessionalAccountId { get; set; }

    public byte DayOfWeek { get; set; }

    public string ModalityCode { get; set; } = null!;

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public DateTime? ValidFrom { get; set; }

    public DateTime? ValidUntil { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual ProfessionalProfile ProfessionalAccount { get; set; } = null!;
}
