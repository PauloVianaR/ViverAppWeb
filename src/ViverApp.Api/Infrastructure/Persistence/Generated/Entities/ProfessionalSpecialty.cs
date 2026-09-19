using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ProfessionalSpecialty
{
    public ulong ProfessionalAccountId { get; set; }

    public uint SpecialtyId { get; set; }

    public bool IsPrimary { get; set; }

    public virtual ProfessionalProfile ProfessionalAccount { get; set; } = null!;

    public virtual Specialty Specialty { get; set; } = null!;
}
