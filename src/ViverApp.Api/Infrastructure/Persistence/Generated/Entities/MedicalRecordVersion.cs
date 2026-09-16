using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class MedicalRecordVersion
{
    public ulong Id { get; set; }

    public ulong MedicalRecordEntryId { get; set; }

    public uint VersionNumber { get; set; }

    public ulong? SupersedesVersionId { get; set; }

    public ulong AuthorAccountId { get; set; }

    public string? CorrectionReason { get; set; }

    public string? ChiefComplaint { get; set; }

    public string? PresentIllnessHistory { get; set; }

    public string? PersonalHistory { get; set; }

    public string? FamilyHistory { get; set; }

    public string? Allergies { get; set; }

    public string? Medications { get; set; }

    public string? RelevantHabits { get; set; }

    public string? PhysicalExamination { get; set; }

    public string? DiagnosticHypotheses { get; set; }

    public string? ConductAndGuidance { get; set; }

    public string? FollowUpPlan { get; set; }

    public string? ClinicalEvolution { get; set; }

    public string? AdditionalNotes { get; set; }

    public ushort? SystolicPressureMmhg { get; set; }

    public ushort? DiastolicPressureMmhg { get; set; }

    public ushort? HeartRateBpm { get; set; }

    public decimal? TemperatureCelsius { get; set; }

    public decimal? WeightKg { get; set; }

    public decimal? HeightCm { get; set; }

    public byte[] ContentSha256 { get; set; } = null!;

    public DateTime FinalizedAtUtc { get; set; }

    public virtual Account AuthorAccount { get; set; } = null!;

    public virtual MedicalRecordVersion? InverseSupersedesVersion { get; set; }

    public virtual ICollection<MedicalRecordDocument> MedicalRecordDocuments { get; set; } = new List<MedicalRecordDocument>();

    public virtual ICollection<MedicalRecordEntry> MedicalRecordEntries { get; set; } = new List<MedicalRecordEntry>();

    public virtual MedicalRecordEntry MedicalRecordEntry { get; set; } = null!;

    public virtual MedicalRecordVersion? SupersedesVersion { get; set; }
}
