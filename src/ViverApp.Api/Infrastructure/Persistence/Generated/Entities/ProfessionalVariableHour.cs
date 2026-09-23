using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ProfessionalVariableHour
{
    public ulong Id { get; set; }

    public ulong ProfessionalAccountId { get; set; }

    public DateTime AvailableDate { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public string ModalityCode { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual ProfessionalProfile ProfessionalAccount { get; set; } = null!;
}
