using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ProfessionalNotification
{
    public ulong Id { get; set; }

    public ulong ProfessionalAccountId { get; set; }

    public ulong AppointmentId { get; set; }

    public string SourceKey { get; set; } = null!;

    public string TypeCode { get; set; } = null!;

    public DateTime? ReadAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual ProfessionalProfile ProfessionalAccount { get; set; } = null!;
}
