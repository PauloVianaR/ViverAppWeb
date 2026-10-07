namespace ViverApp.Web;

public sealed record WebClinicalPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record WebClinicalContext(
    ulong AccountId,
    string RoleCode,
    string FullName,
    IReadOnlyList<WebClinicalDoctorOption> Doctors);

public sealed record WebClinicalDoctorOption(ulong AccountId, string FullName, string LicenseLabel);

public sealed record WebClinicalReport(
    ulong Id,
    string StatusCode,
    string? ClinicalSummary,
    string? Recommendations,
    DateTime UpdatedAtUtc,
    DateTime? PublishedAtUtc,
    ulong RowVersion,
    bool ContentVisible)
{
    public WebOphthalmologyReportFields? Ophthalmology { get; init; }
}

public sealed record WebOphthalmologyReportFields(
    string? OphthalmicHistory, string? VisualAcuity, string? Refraction,
    string? Biomicroscopy, string? Tonometry, string? FundusExam);

public sealed record WebClinicalReportVersion(
    uint VersionNumber, DateTime CreatedAtUtc, string EditorName, string EditorRoleCode,
    string? ChangeReason, string? ClinicalSummary, WebOphthalmologyReportFields? Ophthalmology,
    string? Recommendations);

public sealed class WebOphthalmologyReportDraft
{
    public string OphthalmicHistory { get; set; } = "";
    public string VisualAcuity { get; set; } = "";
    public string Refraction { get; set; } = "";
    public string Biomicroscopy { get; set; } = "";
    public string Tonometry { get; set; } = "";
    public string FundusExam { get; set; } = "";

    public void Load(WebOphthalmologyReportFields? content)
    {
        OphthalmicHistory = content?.OphthalmicHistory ?? "";
        VisualAcuity = content?.VisualAcuity ?? "";
        Refraction = content?.Refraction ?? "";
        Biomicroscopy = content?.Biomicroscopy ?? "";
        Tonometry = content?.Tonometry ?? "";
        FundusExam = content?.FundusExam ?? "";
    }

    public WebOphthalmologyReportFields ToContent() => new(
        OphthalmicHistory, VisualAcuity, Refraction, Biomicroscopy, Tonometry, FundusExam);
}

public sealed record WebClinicalAppointment(
    ulong Id,
    ulong AppointmentNumber,
    ulong PatientAccountId,
    string PatientName,
    string? PatientEmail,
    string? PatientPhone,
    ulong ProfessionalAccountId,
    string DoctorName,
    string AppointmentTypeName,
    string StatusCode,
    string ModalityCode,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string? PatientNotes,
    ulong RowVersion,
    WebClinicalReport? MedicalReport,
    bool CanComplete,
    bool CanMarkNoShow)
{
    public bool IsOphthalmology { get; init; }
}

public sealed record WebClinicalPatient(
    ulong AccountId,
    string FullName,
    string? PreferredName,
    string? Email,
    string? Phone,
    DateOnly? BirthDate,
    int AppointmentCount,
    DateTime? LastAppointmentAtUtc,
    DateTime? NextAppointmentAtUtc);

public sealed record WebProfessionalWeeklyHour(
    ulong Id,
    byte DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    bool IsActive,
    ulong RowVersion);

public sealed record WebMedicalReportWriteRequest(ulong RowVersion, string ClinicalSummary, string? Recommendations);

public sealed record WebCompleteAppointmentRequest(
    ulong AppointmentRowVersion,
    ulong ReportRowVersion,
    string ClinicalSummary,
    string? Recommendations);

public sealed record WebRecordNoShowRequest(ulong AppointmentRowVersion);

public sealed record WebProfessionalWeeklyHourWriteRequest(
    byte DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    bool IsActive,
    ulong RowVersion);
