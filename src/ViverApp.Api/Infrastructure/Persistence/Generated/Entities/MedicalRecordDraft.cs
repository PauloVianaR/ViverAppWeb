using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class MedicalRecordDraft
{
    public ulong Id { get; set; }

    public ulong HealthRecordId { get; set; }

    public ulong AppointmentId { get; set; }

    public ulong AuthorAccountId { get; set; }

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

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual Account AuthorAccount { get; set; } = null!;

    public virtual ElectronicHealthRecord HealthRecord { get; set; } = null!;
}
