using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class MedicalRecordDocument
{
    public ulong Id { get; set; }

    public ulong HealthRecordId { get; set; }

    public ulong? AppointmentId { get; set; }

    public ulong? MedicalRecordVersionId { get; set; }

    public Guid PrivateDocumentId { get; set; }

    public ulong UploadedByAccountId { get; set; }

    public string CategoryCode { get; set; } = null!;

    public string StatusCode { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public ulong? DeletedByAccountId { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Appointment? Appointment { get; set; }

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ElectronicHealthRecord HealthRecord { get; set; } = null!;

    public virtual MedicalRecordVersion? MedicalRecordVersion { get; set; }

    public virtual PrivateDocument PrivateDocument { get; set; } = null!;

    public virtual Account UploadedByAccount { get; set; } = null!;
}
