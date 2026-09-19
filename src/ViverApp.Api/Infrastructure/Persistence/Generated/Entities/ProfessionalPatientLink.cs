using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ProfessionalPatientLink
{
    public ulong ProfessionalAccountId { get; set; }

    public ulong PatientAccountId { get; set; }

    public ulong CreatedByAccountId { get; set; }

    public string StatusCode { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Account CreatedByAccount { get; set; } = null!;

    public virtual Account PatientAccount { get; set; } = null!;

    public virtual ProfessionalProfile ProfessionalAccount { get; set; } = null!;
}
