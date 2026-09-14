using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ElectronicHealthRecord
{
    public ulong Id { get; set; }

    public ulong PatientAccountId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual ICollection<MedicalRecordDocument> MedicalRecordDocuments { get; set; } = new List<MedicalRecordDocument>();

    public virtual ICollection<MedicalRecordDraft> MedicalRecordDrafts { get; set; } = new List<MedicalRecordDraft>();

    public virtual ICollection<MedicalRecordEntry> MedicalRecordEntries { get; set; } = new List<MedicalRecordEntry>();

    public virtual Account PatientAccount { get; set; } = null!;
}
