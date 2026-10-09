using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ManagerNotification
{
    public ulong Id { get; set; }

    public ulong ManagerAccountId { get; set; }

    public ulong AppointmentId { get; set; }

    public string TypeCode { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual Account ManagerAccount { get; set; } = null!;
}
