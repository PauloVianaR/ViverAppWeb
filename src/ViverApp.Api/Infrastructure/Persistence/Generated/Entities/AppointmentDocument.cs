using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AppointmentDocument
{
    public ulong Id { get; set; }

    public ulong AppointmentId { get; set; }

    public ulong UploadedByAccountId { get; set; }

    public string CategoryCode { get; set; } = null!;

    public string ObjectKey { get; set; } = null!;

    public string OriginalFileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public ulong SizeBytes { get; set; }

    public byte[] Sha256 { get; set; } = null!;

    public string StatusCode { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? AvailableAtUtc { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public ulong? DeletedByAccountId { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual Account? DeletedByAccount { get; set; }

    public virtual Account UploadedByAccount { get; set; } = null!;
}
