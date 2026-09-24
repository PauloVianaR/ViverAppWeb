using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ScheduledJob
{
    public ulong Id { get; set; }

    public string JobKey { get; set; } = null!;

    public string JobTypeCode { get; set; } = null!;

    public ulong? AppointmentId { get; set; }

    public DateTime DueAtUtc { get; set; }

    public string StatusCode { get; set; } = null!;

    public ushort AttemptCount { get; set; }

    public ushort MaxAttempts { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTime? LeaseUntilUtc { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    public string? LastErrorCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public virtual Appointment? Appointment { get; set; }
}
