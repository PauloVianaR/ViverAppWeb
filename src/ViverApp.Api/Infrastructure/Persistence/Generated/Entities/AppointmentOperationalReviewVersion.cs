using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AppointmentOperationalReviewVersion
{
    public ulong Id { get; set; }

    public ulong AppointmentId { get; set; }

    public ulong VersionNumber { get; set; }

    public byte Rating { get; set; }

    public string? Feedback { get; set; }

    public ulong? EditorAccountId { get; set; }

    public string EditorRoleCode { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual Account? EditorAccount { get; set; }
}
