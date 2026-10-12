using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AppointmentOperationalReview
{
    public ulong AppointmentId { get; set; }

    public byte Rating { get; set; }

    public string? Feedback { get; set; }

    public ulong? UpdatedByAccountId { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual Account? UpdatedByAccount { get; set; }
}
