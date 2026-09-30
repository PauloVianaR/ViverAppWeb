using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ViverApp.Api.Features.ClinicAdministration;

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClinicUpdateRequest(
    [param: Required, StringLength(200, MinimumLength = 2)] string LegalName,
    [param: Required, StringLength(120, MinimumLength = 2)] string DisplayName,
    [param: RegularExpression("^[0-9]{14}$")] string? TaxId,
    [param: EmailAddress, StringLength(254)] string? Email,
    [param: RegularExpression("^\\+55[1-9][0-9]{9,10}$")] string? PhoneE164,
    [param: RegularExpression("^[0-9]{8}$")] string? PostalCode,
    [param: StringLength(200)] string? Street,
    [param: StringLength(20)] string? Number,
    [param: StringLength(100)] string? Complement,
    [param: StringLength(100)] string? District,
    [param: StringLength(100)] string? City,
    [param: RegularExpression("^[A-Z]{2}$")] string? StateCode,
    [param: Required, StringLength(64)] string TimezoneName,
    [param: Range(0, long.MaxValue)] ulong RowVersion) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var address = new[] { PostalCode, Street, Number, District, City, StateCode };
        var populated = address.Count(value => !string.IsNullOrWhiteSpace(value));
        if (populated != 0 && populated != address.Length)
        {
            yield return new ValidationResult(
                "O endereço deve ser informado por completo.",
                [nameof(PostalCode), nameof(Street), nameof(Number), nameof(District), nameof(City), nameof(StateCode)]);
        }
    }
}

public sealed record ClinicResponse(
    string LegalName,
    string DisplayName,
    string? TaxId,
    string? Email,
    string? PhoneE164,
    string? PostalCode,
    string? Street,
    string? Number,
    string? Complement,
    string? District,
    string? City,
    string? StateCode,
    string TimezoneName,
    ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SpecialtyWriteRequest(
    [param: Required, StringLength(120, MinimumLength = 2)] string Name,
    bool IsActive,
    [param: Range(0, long.MaxValue)] ulong RowVersion = 0);

public sealed record SpecialtyResponse(uint Id, string Name, bool IsActive, ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppointmentTypeWriteRequest(
    [param: Required, StringLength(120, MinimumLength = 2)] string Name,
    [param: StringLength(500)] string? Description,
    [param: Required, RegularExpression("^(in_person|online|both)$")] string ModalityCode,
    [param: Range(5, 480)] ushort DurationMinutes,
    [param: Range(typeof(decimal), "0", "99999999999.99",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal PriceAmount,
    bool IsActive,
    ushort DisplayOrder,
    [param: Range(0, long.MaxValue)] ulong RowVersion = 0,
    [param: Required, RegularExpression("^(consultation|examination|surgery|procedure)$")] string CategoryCode = "consultation",
    bool RequiresPayment = true,
    [param: MaxLength(500)] IReadOnlyList<ulong>? ProfessionalAccountIds = null);

public sealed record AppointmentTypeResponse(
    uint Id,
    string Name,
    string? Description,
    string CategoryCode,
    string ModalityCode,
    ushort DurationMinutes,
    decimal PriceAmount,
    bool RequiresPayment,
    bool IsActive,
    ushort DisplayOrder,
    ulong RowVersion,
    bool CanDelete = false);

public sealed record AppointmentTypeProfessionalResponse(
    ulong ProfessionalAccountId,
    string FullName,
    string RoleCode,
    string LicenseLabel,
    bool Linked);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppointmentTypeProfessionalsUpdateRequest(
    [param: MaxLength(500)] IReadOnlyList<ulong> ProfessionalAccountIds);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WeeklyHourWriteRequest(
    [param: Range(0, 6)] byte DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    bool IsActive,
    [param: Range(0, long.MaxValue)] ulong RowVersion = 0);

public sealed record WeeklyHourResponse(
    ulong Id,
    byte DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    bool IsActive,
    ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfessionalWeeklyHourWriteRequest(
    [param: Range(0, 6)] byte DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    bool IsActive,
    [param: Range(0, long.MaxValue)] ulong RowVersion = 0,
    [param: RegularExpression("^(in_person|online|both)$")] string ModalityCode = "both");

public sealed record ProfessionalWeeklyHourResponse(
    ulong Id,
    byte DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    bool IsActive,
    ulong RowVersion,
    string ModalityCode);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HolidayWriteRequest(
    DateOnly HolidayDate,
    [param: Required, StringLength(120, MinimumLength = 2)] string Name,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    [param: Range(0, long.MaxValue)] ulong RowVersion = 0,
    bool IsAnnual = false);

public sealed record HolidayResponse(
    uint Id,
    DateOnly HolidayDate,
    string Name,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    ulong RowVersion,
    bool IsAnnual);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UserUpdateRequest(
    [param: Required, StringLength(200, MinimumLength = 2)] string FullName,
    [param: Range(1, long.MaxValue)] ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatientStatusRequest(
    [param: Required, RegularExpression("^(blocked|reactivated)$")] string DecisionCode,
    [param: StringLength(500)] string? Reason,
    [param: Range(1, long.MaxValue)] ulong RowVersion);

public sealed record UserResponse(
    ulong Id,
    string FullName,
    string RoleCode,
    string StatusCode,
    string? Email,
    bool EmailVerified,
    string? PhoneE164,
    bool PhoneVerified,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc,
    ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfessionalCreateRequest(
    [param: Required, StringLength(200, MinimumLength = 2)] string FullName,
    [param: EmailAddress, StringLength(254)] string? Email,
    [param: RegularExpression("^\\+55[1-9][0-9]{9,10}$")] string? PhoneE164,
    [param: Required, RegularExpression("^(doctor|psychologist|manager)$")] string RoleCode,
    [param: RegularExpression("^[A-Z]{2}$")] string? LicenseStateCode,
    [param: StringLength(30, MinimumLength = 1)] string? LicenseNumber,
    [param: StringLength(4000)] string? Biography,
    [param: Range(5, 480)] ushort? DefaultAppointmentDurationMinutes,
    IReadOnlyList<uint>? SpecialtyIds,
    uint? PrimarySpecialtyId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfessionalUpdateRequest(
    [param: Required, StringLength(200, MinimumLength = 2)] string FullName,
    [param: StringLength(4000)] string? Biography,
    [param: Range(5, 480)] ushort? DefaultAppointmentDurationMinutes,
    IReadOnlyList<uint>? SpecialtyIds,
    uint? PrimarySpecialtyId,
    [param: Range(1, long.MaxValue)] ulong AccountRowVersion,
    [param: Range(0, long.MaxValue)] ulong? ProfileRowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfessionalReviewRequest(
    [param: Required, RegularExpression("^(approved|rejected|blocked|reactivated)$")] string DecisionCode,
    [param: StringLength(500)] string? Reason,
    [param: Range(1, long.MaxValue)] ulong AccountRowVersion);

public sealed record ProfessionalResponse(
    ulong AccountId,
    string FullName,
    string RoleCode,
    string StatusCode,
    string? Email,
    string? PhoneE164,
    string? TaxId,
    DateOnly? BirthDate,
    string? ProfessionalTitle,
    string? LicenseStateCode,
    string? LicenseNumber,
    string? Biography,
    ushort? YearsExperience,
    ushort? DefaultAppointmentDurationMinutes,
    IReadOnlyList<SpecialtyResponse> Specialties,
    uint? PrimarySpecialtyId,
    ulong AccountRowVersion,
    ulong? ProfileRowVersion);
