using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ViverApp.Api.Features.ClinicalOperations;

public sealed record ClinicalPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record ClinicalContextResponse(
    ulong AccountId,
    string RoleCode,
    string FullName,
    IReadOnlyList<ClinicalDoctorOptionResponse> Doctors);

public sealed record ClinicalDoctorOptionResponse(ulong AccountId, string FullName, string LicenseLabel);

public sealed record ClinicalReportResponse(
    ulong Id,
    string StatusCode,
    string? ClinicalSummary,
    string? Recommendations,
    DateTime UpdatedAtUtc,
    DateTime? PublishedAtUtc,
    ulong RowVersion,
    bool ContentVisible);

public sealed record ClinicalAppointmentResponse(
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
    ClinicalReportResponse? MedicalReport,
    bool CanComplete,
    bool CanMarkNoShow);

public sealed record ClinicalPatientResponse(
    ulong AccountId,
    string FullName,
    string? PreferredName,
    string? Email,
    string? Phone,
    DateOnly? BirthDate,
    int AppointmentCount,
    DateTime? LastAppointmentAtUtc,
    DateTime? NextAppointmentAtUtc);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MedicalReportWriteRequest(
    [param: Range(0, long.MaxValue)] ulong RowVersion,
    [param: Required, StringLength(12000, MinimumLength = 20)] string ClinicalSummary,
    [param: StringLength(8000)] string? Recommendations) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((ClinicalSummary?.Trim().Length ?? 0) is < 20 or > 12000)
        {
            yield return new ValidationResult(
                "O resumo clínico deve ter entre 20 e 12.000 caracteres.",
                [nameof(ClinicalSummary)]);
        }

        if (Recommendations?.Trim().Length > 8000)
        {
            yield return new ValidationResult(
                "As recomendações devem ter no máximo 8.000 caracteres.",
                [nameof(Recommendations)]);
        }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteAppointmentRequest(
    [param: Range(1, long.MaxValue)] ulong AppointmentRowVersion,
    [param: Range(0, long.MaxValue)] ulong ReportRowVersion,
    [param: StringLength(12000)] string? ClinicalSummary,
    [param: StringLength(8000)] string? Recommendations) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var summary = ClinicalSummary?.Trim();
        if (summary is { Length: > 0 and < 20 })
            yield return new ValidationResult(
                "Quando informado, o resumo clínico deve ter pelo menos 20 caracteres.",
                [nameof(ClinicalSummary)]);
        if (string.IsNullOrEmpty(summary) && !string.IsNullOrWhiteSpace(Recommendations))
            yield return new ValidationResult(
                "Informe o resumo clínico antes das recomendações, ou conclua sem laudo.",
                [nameof(ClinicalSummary), nameof(Recommendations)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordNoShowRequest([param: Range(1, long.MaxValue)] ulong AppointmentRowVersion);

public sealed record PatientMedicalReportResponse(
    ulong AppointmentId,
    string DoctorName,
    string AppointmentTypeName,
    DateTime AppointmentAtUtc,
    string ClinicalSummary,
    string? Recommendations,
    DateTime PublishedAtUtc);

internal sealed class ClinicalRuleException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
