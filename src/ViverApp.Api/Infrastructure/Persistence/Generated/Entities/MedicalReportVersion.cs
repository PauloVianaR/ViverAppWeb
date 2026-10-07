using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class MedicalReportVersion
{
    public ulong Id { get; set; }

    public ulong MedicalReportId { get; set; }

    public uint VersionNumber { get; set; }

    public ulong AuthorProfessionalAccountId { get; set; }

    public string ClinicalSummary { get; set; } = null!;

    public string? Recommendations { get; set; }

    public string? ChangeReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? OphthalmicHistory { get; set; }

    public string? VisualAcuity { get; set; }

    public string? Refraction { get; set; }

    public string? Biomicroscopy { get; set; }

    public string? Tonometry { get; set; }

    public string? FundusExam { get; set; }

    public ulong EditorAccountId { get; set; }

    public virtual ProfessionalProfile AuthorProfessionalAccount { get; set; } = null!;

    public virtual Account EditorAccount { get; set; } = null!;

    public virtual MedicalReport MedicalReport { get; set; } = null!;
}
