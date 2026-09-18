using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ViverApp.Api.Features.MedicalRecords;

public sealed record MedicalRecordPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record MedicalRecordPatientSummary(
    ulong PatientAccountId,
    string FullName,
    string? PreferredName,
    DateOnly? BirthDate,
    int? Age,
    string? TaxId,
    string? Email,
    string? Phone,
    string? Address,
    string AccountStatus,
    bool PortalAccessEnabled,
    bool IsPremium,
    int AppointmentCount,
    int CompletedCount,
    int NoShowCount,
    int CanceledCount,
    int DocumentCount,
    DateTime? LastAppointmentAtUtc,
    DateTime? NextAppointmentAtUtc,
    DateTime? LastClinicalUpdateAtUtc);

public sealed record MedicalRecordTimelineEvent(
    string EventType,
    string StatusCode,
    string Title,
    string? Description,
    DateTime OccurredAtUtc,
    ulong? AppointmentId,
    ulong? AppointmentNumber,
    string? ActorName,
    string? ActorRole,
    bool Restricted);

public sealed record MedicalRecordFinancialItem(
    ulong AppointmentId,
    ulong AppointmentNumber,
    DateTime AppointmentAtUtc,
    decimal Amount,
    string StatusCode,
    string MethodLabel,
    ulong? PaymentId,
    ulong? SupersedesPaymentId);

public sealed record MedicalRecordFinancialSummary(
    decimal Received,
    decimal Reversed,
    decimal Net,
    IReadOnlyList<MedicalRecordFinancialItem> Items);

public sealed record MedicalRecordAppointmentOption(
    ulong Id,
    ulong AppointmentNumber,
    DateTime StartsAtUtc,
    string StatusCode,
    string AppointmentTypeName);

public sealed record MedicalRecordContent(
    string? ChiefComplaint,
    string? PresentIllnessHistory,
    string? PersonalHistory,
    string? FamilyHistory,
    string? Allergies,
    string? Medications,
    string? RelevantHabits,
    string? PhysicalExamination,
    string? DiagnosticHypotheses,
    string? ConductAndGuidance,
    string? FollowUpPlan,
    string? ClinicalEvolution,
    string? AdditionalNotes,
    ushort? SystolicPressureMmhg,
    ushort? DiastolicPressureMmhg,
    ushort? HeartRateBpm,
    decimal? TemperatureCelsius,
    decimal? WeightKg,
    decimal? HeightCm);

public sealed record MedicalRecordDraftResponse(
    ulong Id,
    ulong AppointmentId,
    ulong AppointmentNumber,
    DateTime AppointmentAtUtc,
    ulong AuthorDoctorAccountId,
    string AuthorDoctorName,
    MedicalRecordContent Content,
    DateTime UpdatedAtUtc,
    DateTime ExpiresAtUtc,
    ulong RowVersion);

public sealed record MedicalRecordVersionResponse(
    ulong Id,
    ulong EntryId,
    ulong AppointmentId,
    ulong AppointmentNumber,
    uint VersionNumber,
    ulong? SupersedesVersionId,
    string? CorrectionReason,
    ulong AuthorDoctorAccountId,
    string AuthorDoctorName,
    string LicenseLabel,
    MedicalRecordContent Content,
    DateTime FinalizedAtUtc,
    bool IsCurrent);

public sealed record MedicalRecordEntryResponse(
    ulong Id,
    ulong AppointmentId,
    ulong AppointmentNumber,
    DateTime AppointmentAtUtc,
    ulong CurrentVersionId,
    uint CurrentVersionNumber,
    string AuthorRoleCode,
    string DoctorName,
    string LicenseLabel,
    DateTime FinalizedAtUtc,
    IReadOnlyList<MedicalRecordVersionResponse>? Versions);

public sealed record MedicalRecordDocumentResponse(
    ulong Id,
    string FileName,
    string ContentType,
    ulong SizeBytes,
    string CategoryCode,
    ulong? AppointmentId,
    ulong? AppointmentNumber,
    ulong? MedicalRecordVersionId,
    string UploadedBy,
    DateTime CreatedAtUtc,
    ulong RowVersion);

public sealed record MedicalRecordAccessEventResponse(
    ulong Id,
    string ActorName,
    string ActorRole,
    string ScopeCode,
    string OutcomeCode,
    string? Purpose,
    DateTime OccurredAtUtc);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MedicalRecordDraftWriteRequest(
    [param: Range(0, long.MaxValue)] ulong RowVersion,
    MedicalRecordContent Content);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MedicalRecordFinalizeRequest(
    [param: Range(1, long.MaxValue)] ulong DraftRowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MedicalRecordRectifyRequest(
    [param: Range(1, long.MaxValue)] ulong CurrentVersionId,
    [param: Required, StringLength(1000, MinimumLength = 5)] string CorrectionReason,
    MedicalRecordContent Content);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MedicalRecordPdfRequest(
    DateOnly? From,
    DateOnly? To,
    bool IncludeTimeline,
    bool IncludeClinical,
    bool IncludeFinancial,
    [param: StringLength(500)] string? Purpose);

internal sealed class MedicalRecordRuleException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
