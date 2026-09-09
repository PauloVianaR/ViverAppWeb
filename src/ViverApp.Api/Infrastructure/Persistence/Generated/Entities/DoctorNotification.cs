using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class DoctorNotification
{
    public ulong Id { get; set; }

    public ulong DoctorAccountId { get; set; }

    public ulong AppointmentId { get; set; }

    public string SourceKey { get; set; } = null!;

    public string TypeCode { get; set; } = null!;

    public DateTime? ReadAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual DoctorProfile DoctorAccount { get; set; } = null!;
}
