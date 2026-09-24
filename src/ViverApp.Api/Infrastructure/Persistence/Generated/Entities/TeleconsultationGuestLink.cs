using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class TeleconsultationGuestLink
{
    public byte[] Id { get; set; } = null!;

    public ulong AppointmentId { get; set; }

    public ulong CreatedByAccountId { get; set; }

    public byte[] TokenHash { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual Account CreatedByAccount { get; set; } = null!;
}
