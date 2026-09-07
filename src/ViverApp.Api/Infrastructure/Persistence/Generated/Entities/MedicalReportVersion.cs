using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class MedicalReportVersion
{
    public ulong Id { get; set; }

    public ulong MedicalReportId { get; set; }

    public uint VersionNumber { get; set; }

    public ulong AuthorDoctorAccountId { get; set; }

    public string ClinicalSummary { get; set; } = null!;

    public string? Recommendations { get; set; }

    public string? ChangeReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual DoctorProfile AuthorDoctorAccount { get; set; } = null!;

    public virtual MedicalReport MedicalReport { get; set; } = null!;
}
