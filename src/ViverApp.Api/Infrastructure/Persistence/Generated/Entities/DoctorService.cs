using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class DoctorService
{
    public ulong DoctorAccountId { get; set; }

    public uint AppointmentTypeId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual AppointmentType AppointmentType { get; set; } = null!;

    public virtual DoctorProfile DoctorAccount { get; set; } = null!;
}
