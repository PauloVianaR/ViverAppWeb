using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AppointmentStatusHistory
{
    public ulong Id { get; set; }

    public ulong AppointmentId { get; set; }

    public ulong ActorAccountId { get; set; }

    public string? FromStatusCode { get; set; }

    public string ToStatusCode { get; set; } = null!;

    public string? Reason { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public virtual Account ActorAccount { get; set; } = null!;

    public virtual Appointment Appointment { get; set; } = null!;
}
