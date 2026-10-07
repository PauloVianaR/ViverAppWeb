using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class MedicalReport
{
    public ulong Id { get; set; }

    public ulong AppointmentId { get; set; }

    public ulong AuthorProfessionalAccountId { get; set; }

    public string StatusCode { get; set; } = null!;

    public string ClinicalSummary { get; set; } = null!;

    public string? Recommendations { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public string? OphthalmicHistory { get; set; }

    public string? VisualAcuity { get; set; }

    public string? Refraction { get; set; }

    public string? Biomicroscopy { get; set; }

    public string? Tonometry { get; set; }

    public string? FundusExam { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual ProfessionalProfile AuthorProfessionalAccount { get; set; } = null!;

    public virtual ICollection<MedicalReportVersion> MedicalReportVersions { get; set; } = new List<MedicalReportVersion>();
}
