using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ViverApp.Api.Features.PatientScheduling;

public sealed record SchedulingPage<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record BookingSpecialtyResponse(uint Id, string Name, bool IsPrimary);

public sealed record BookingProfessionalResponse(
    ulong AccountId,
    string FullName,
    string? Biography,
    string LicenseStateCode,
    string LicenseNumber,
    ushort DefaultAppointmentDurationMinutes,
    IReadOnlyList<BookingSpecialtyResponse> Specialties,
    ushort YearsExperience = 0,
    double? AverageRating = null,
    int ReviewCount = 0,
    bool SupportsOnline = true);

public sealed record AvailableSlotResponse(
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string TimezoneName);

public sealed record AppointmentRescheduleHistoryResponse(
    uint SequenceNumber,
    DateTime PreviousStartsAtUtc,
    DateTime PreviousEndsAtUtc,
    DateTime NewStartsAtUtc,
    DateTime NewEndsAtUtc,
    string? Reason,
    DateTime OccurredAtUtc);

public sealed record AppointmentResponse(
    ulong Id,
    ulong AppointmentNumber,
    ulong DoctorAccountId,
    string DoctorName,
    uint AppointmentTypeId,
    string AppointmentTypeName,
    string StatusCode,
    string ModalityCode,
    DateOnly LocalDate,
    TimeOnly LocalStartsAt,
    TimeOnly LocalEndsAt,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string TimezoneName,
    decimal PriceAmount,
    string CurrencyCode,
    string? PatientNotes,
    string? CancellationReason,
    ulong? RescheduledFromAppointmentId,
    ulong? RescheduledToAppointmentId,
    DateTime? ArrivedAtUtc,
    DateOnly? ArrivalBusinessDate,
    uint? ArrivalQueueNumber,
    IReadOnlyList<AppointmentRescheduleHistoryResponse> RescheduleHistory,
    ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppointmentCreateRequest(
    [param: Range(1, long.MaxValue)] ulong DoctorAccountId,
    [param: Range(1, int.MaxValue)] uint AppointmentTypeId,
    [param: Required, RegularExpression("^(in_person|online)$")] string ModalityCode,
    DateOnly LocalDate,
    TimeOnly LocalStartsAt,
    [param: StringLength(1000)] string? PatientNotes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DoctorAppointmentCreateRequest(
    [param: Range(1, long.MaxValue)] ulong PatientAccountId,
    [param: Range(1, int.MaxValue)] uint AppointmentTypeId,
    [param: Required, RegularExpression("^(in_person|online)$")] string ModalityCode,
    DateOnly LocalDate,
    TimeOnly LocalStartsAt,
    [param: StringLength(1000)] string? PatientNotes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppointmentCancelRequest(
    [param: Required, StringLength(500, MinimumLength = 5)] string Reason,
    [param: Range(1, long.MaxValue)] ulong RowVersion) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Reason) || Reason.Trim().Length < 5)
        {
            yield return new ValidationResult(
                "O motivo deve conter pelo menos 5 caracteres úteis.",
                [nameof(Reason)]);
        }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppointmentRescheduleRequest(
    DateOnly LocalDate,
    TimeOnly LocalStartsAt,
    [param: StringLength(500)] string? Reason,
    [param: Range(1, long.MaxValue)] ulong RowVersion);

internal sealed class SchedulingRuleException(int statusCode, string title) : Exception(title)
{
    public int StatusCode { get; } = statusCode;
}
