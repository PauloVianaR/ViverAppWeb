namespace ViverApp.Web;

public sealed record WebMedicalRecordPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
public sealed record WebMedicalRecordPatientSummary(ulong PatientAccountId, string FullName, string? PreferredName,
    DateOnly? BirthDate, int? Age, string? TaxId, string? Email, string? Phone, string? Address,
    string AccountStatus, bool PortalAccessEnabled, bool IsPremium, int AppointmentCount, int CompletedCount,
    int NoShowCount, int CanceledCount, int DocumentCount, DateTime? LastAppointmentAtUtc,
    DateTime? NextAppointmentAtUtc, DateTime? LastClinicalUpdateAtUtc);
public sealed record WebMedicalRecordTimelineEvent(string EventType, string StatusCode, string Title, string? Description,
    DateTime OccurredAtUtc, ulong? AppointmentId, ulong? AppointmentNumber, string? ActorName, string? ActorRole, bool Restricted);
public sealed record WebMedicalRecordFinancialItem(ulong AppointmentId, ulong AppointmentNumber, DateTime AppointmentAtUtc,
    decimal Amount, string StatusCode, string MethodLabel, ulong? PaymentId, ulong? SupersedesPaymentId);
public sealed record WebMedicalRecordFinancialSummary(decimal Received, decimal Reversed, decimal Net,
    IReadOnlyList<WebMedicalRecordFinancialItem> Items);
public sealed record WebMedicalRecordAppointmentOption(ulong Id, ulong AppointmentNumber, DateTime StartsAtUtc,
    string StatusCode, string AppointmentTypeName);
public sealed record WebMedicalRecordContent(string? ChiefComplaint, string? PresentIllnessHistory, string? PersonalHistory,
    string? FamilyHistory, string? Allergies, string? Medications, string? RelevantHabits, string? PhysicalExamination,
    string? DiagnosticHypotheses, string? ConductAndGuidance, string? FollowUpPlan, string? ClinicalEvolution,
    string? AdditionalNotes, ushort? SystolicPressureMmhg, ushort? DiastolicPressureMmhg, ushort? HeartRateBpm,
    decimal? TemperatureCelsius, decimal? WeightKg, decimal? HeightCm)
{
    public static WebMedicalRecordContent Empty { get; } = new(null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null, null, null);
}
public sealed record WebMedicalRecordDraft(ulong Id, ulong AppointmentId, ulong AppointmentNumber, DateTime AppointmentAtUtc,
    ulong AuthorDoctorAccountId, string AuthorDoctorName, WebMedicalRecordContent Content, DateTime UpdatedAtUtc,
    DateTime ExpiresAtUtc, ulong RowVersion);
public sealed record WebMedicalRecordVersion(ulong Id, ulong EntryId, ulong AppointmentId, ulong AppointmentNumber,
    uint VersionNumber, ulong? SupersedesVersionId, string? CorrectionReason, ulong AuthorDoctorAccountId,
    string AuthorDoctorName, string LicenseLabel, WebMedicalRecordContent Content, DateTime FinalizedAtUtc, bool IsCurrent);
public sealed record WebMedicalRecordEntry(ulong Id, ulong AppointmentId, ulong AppointmentNumber, DateTime AppointmentAtUtc,
    ulong CurrentVersionId, uint CurrentVersionNumber, string AuthorRoleCode, string DoctorName, string LicenseLabel, DateTime FinalizedAtUtc,
    IReadOnlyList<WebMedicalRecordVersion>? Versions);
public sealed record WebMedicalRecordDocument(ulong Id, string FileName, string ContentType, ulong SizeBytes,
    string CategoryCode, ulong? AppointmentId, ulong? AppointmentNumber, ulong? MedicalRecordVersionId,
    string UploadedBy, DateTime CreatedAtUtc, ulong RowVersion);
public sealed record WebMedicalRecordAccessEvent(ulong Id, string ActorName, string ActorRole, string ScopeCode,
    string OutcomeCode, string? Purpose, DateTime OccurredAtUtc);
public sealed record WebMedicalRecordDraftWriteRequest(ulong RowVersion, WebMedicalRecordContent Content);
public sealed record WebMedicalRecordFinalizeRequest(ulong DraftRowVersion);
public sealed record WebMedicalRecordRectifyRequest(ulong CurrentVersionId, string CorrectionReason, WebMedicalRecordContent Content);
public sealed record WebMedicalRecordPdfRequest(DateOnly? From, DateOnly? To, bool IncludeTimeline,
    bool IncludeClinical, bool IncludeFinancial, string? Purpose);
