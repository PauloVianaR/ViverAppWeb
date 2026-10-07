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
    bool SupportsOnline = true,
    string LicenseTypeCode = "CRM");

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

public sealed record AppointmentServiceResponse(uint AppointmentTypeId, string Name, string CategoryCode,
    ushort DurationMinutes, decimal BasePriceAmount, bool RequiresPayment);

public sealed record AppointmentResponse(
    ulong Id,
    ulong AppointmentNumber,
    ulong ProfessionalAccountId,
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
    bool RequiresPayment,
    ulong RowVersion)
{
    public IReadOnlyList<AppointmentServiceResponse> Services { get; init; } = [];
    public decimal BasePriceAmount { get; init; }
    public decimal PremiumDiscountPercent { get; init; }
    public string? PointDiscountKindCode { get; init; }
    public decimal? PointDiscountValue { get; init; }
    public decimal PointDiscountAmount { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppointmentCreateRequest(
    [param: Range(1, long.MaxValue)] ulong ProfessionalAccountId,
    [param: Range(1, int.MaxValue)] uint AppointmentTypeId,
    [param: Required, RegularExpression("^(in_person|online)$")] string ModalityCode,
    DateOnly LocalDate,
    TimeOnly LocalStartsAt,
    [param: StringLength(1000)] string? PatientNotes,
    IReadOnlyList<uint>? AdditionalAppointmentTypeIds = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DoctorAppointmentCreateRequest(
    [param: Range(1, long.MaxValue)] ulong PatientAccountId,
    [param: Range(1, int.MaxValue)] uint AppointmentTypeId,
    [param: Required, RegularExpression("^(in_person|online)$")] string ModalityCode,
    DateOnly LocalDate,
    TimeOnly LocalStartsAt,
    [param: StringLength(1000)] string? PatientNotes,
    IReadOnlyList<uint>? AdditionalAppointmentTypeIds = null,
    [param: Range(5, 720)] int? DurationMinutes = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppointmentPointDiscountRequest(
    [param: Required, RegularExpression("^(percent|amount)$")] string KindCode,
    [param: Range(typeof(decimal), "0.01", "99999999",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal Value,
    [param: Range(1, long.MaxValue)] ulong RowVersion);

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
