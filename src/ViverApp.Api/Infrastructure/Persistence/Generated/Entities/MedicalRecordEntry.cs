using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class MedicalRecordEntry
{
    public ulong Id { get; set; }

    public ulong HealthRecordId { get; set; }

    public ulong AppointmentId { get; set; }

    public ulong AuthorAccountId { get; set; }

    public ulong? CurrentVersionId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual Account AuthorAccount { get; set; } = null!;

    public virtual MedicalRecordVersion? CurrentVersion { get; set; }

    public virtual ElectronicHealthRecord HealthRecord { get; set; } = null!;

    public virtual ICollection<MedicalRecordVersion> MedicalRecordVersions { get; set; } = new List<MedicalRecordVersion>();
}
