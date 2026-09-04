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
    bool ContentVisible);

public sealed record WebClinicalAppointment(
    ulong Id,
    ulong PatientAccountId,
    string PatientName,
    string? PatientEmail,
    string? PatientPhone,
    ulong DoctorAccountId,
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
    bool CanMarkNoShow);

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

public sealed record WebDoctorWeeklyHour(
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

public sealed record WebDoctorWeeklyHourWriteRequest(
    byte DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    bool IsActive,
    ulong RowVersion);
