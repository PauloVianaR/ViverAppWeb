using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AppointmentServiceItem
{
    public ulong Id { get; set; }

    public ulong AppointmentId { get; set; }

    public uint AppointmentTypeId { get; set; }

    public byte Ordinal { get; set; }

    public string NameSnapshot { get; set; } = null!;

    public string CategoryCode { get; set; } = null!;

    public ushort DurationMinutes { get; set; }

    public decimal BasePriceAmount { get; set; }

    public bool RequiresPayment { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual AppointmentType AppointmentType { get; set; } = null!;
}
