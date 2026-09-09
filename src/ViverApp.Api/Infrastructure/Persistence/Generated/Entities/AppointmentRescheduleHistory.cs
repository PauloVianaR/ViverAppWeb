using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AppointmentRescheduleHistory
{
    public ulong Id { get; set; }

    public ulong AppointmentId { get; set; }

    public uint SequenceNumber { get; set; }

    public ulong ActorAccountId { get; set; }

    public DateTime PreviousStartsAtUtc { get; set; }

    public DateTime PreviousEndsAtUtc { get; set; }

    public DateTime NewStartsAtUtc { get; set; }

    public DateTime NewEndsAtUtc { get; set; }

    public string? Reason { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public virtual Account ActorAccount { get; set; } = null!;

    public virtual Appointment Appointment { get; set; } = null!;
}
