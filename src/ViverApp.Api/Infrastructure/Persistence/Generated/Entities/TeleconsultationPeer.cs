using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class TeleconsultationPeer
{
    public ulong Id { get; set; }

    public ulong AppointmentId { get; set; }

    public ulong? AccountId { get; set; }

    public byte[]? GuestId { get; set; }

    public string ConnectionId { get; set; } = null!;

    public DateTime ExpiresAtUtc { get; set; }

    public virtual Account? Account { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;
}
