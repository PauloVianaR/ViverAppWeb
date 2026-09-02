using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class DoctorSpecialty
{
    public ulong DoctorAccountId { get; set; }

    public uint SpecialtyId { get; set; }

    public bool IsPrimary { get; set; }

    public virtual DoctorProfile DoctorAccount { get; set; } = null!;

    public virtual Specialty Specialty { get; set; } = null!;
}
