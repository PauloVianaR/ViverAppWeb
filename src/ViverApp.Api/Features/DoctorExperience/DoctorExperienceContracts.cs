using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ViverApp.Api.Features.PatientScheduling;

namespace ViverApp.Api.Features.DoctorExperience;

public sealed record DoctorHomeResponse(DoctorProfileResponse Profile, DoctorHomeCounters Counters,
    IReadOnlyList<DoctorAppointmentResponse> Today);
public sealed record DoctorHomeCounters(int Today, int Week, int Online, int InPerson);
public sealed record DoctorSpecialtyResponse(uint Id, string Name, bool IsPrimary);
public sealed record DoctorProfileResponse(ulong AccountId, string FullName, string? Email, string? Phone,
    string? TaxId, string ProfessionalTitle, string LicenseStateCode, string LicenseNumber, string? Biography,
    ushort YearsExperience, ushort DefaultAppointmentDurationMinutes, IReadOnlyList<DoctorSpecialtyResponse> Specialties,
    bool EmailEnabled, bool SmsEnabled, bool OnlineEnabled, ushort MaxOnlineDaily, ushort MaxInPersonDaily,
    double? AverageRating, int ReviewCount, ulong AccountRowVersion, ulong ProfileRowVersion, ulong PreferenceRowVersion);
public sealed record DoctorServiceResponse(uint Id, string Name, string? Description, string CategoryCode,
    string ModalityCode, ushort DurationMinutes, decimal PriceAmount, bool IsActive, bool Offered, ulong RowVersion);
public sealed record DoctorAppointmentResponse(ulong Id, ulong PatientAccountId, string PatientName, int? PatientAge,
    uint AppointmentTypeId, string Service, string CategoryCode, string StatusCode, string ModalityCode, DateTime StartsAtUtc, DateTime EndsAtUtc,
    decimal PriceAmount, decimal DiscountPercent, string PaymentStatus, string PaymentLocation, string? PatientNotes,
    string? CancellationReason, ulong? RescheduledFromAppointmentId, ulong? RescheduledToAppointmentId,
    byte? Rating, string? ReviewComment, bool CanJoinOnline, bool CanCancel, bool CanReschedule, bool CanComplete,
    ulong RowVersion);
public sealed record DoctorAgendaResponse(DoctorAgendaCounters Counters, SchedulingPage<DoctorAppointmentResponse> Page);
public sealed record DoctorAgendaCounters(int Total, int Online, int InPerson, int Rescheduled);
public sealed record DoctorPatientResponse(ulong AccountId, string FullName, string? PreferredName, string? Email,
    string? Phone, DateOnly? BirthDate, string StatusCode, bool IsPremium, int AppointmentCount,
    DateTime? LastAppointmentAtUtc, DateTime? NextAppointmentAtUtc, ulong RowVersion);
public sealed record DoctorPatientCounters(int Total, int Premium, int Active, int Blocked);
public sealed record DoctorPatientsResponse(DoctorPatientCounters Counters, SchedulingPage<DoctorPatientResponse> Page);
public sealed record DoctorReportVersionResponse(uint VersionNumber, string ClinicalSummary, string? Recommendations,
    string? ChangeReason, DateTime CreatedAtUtc, ulong AuthorDoctorAccountId);
public sealed record DoctorDocumentResponse(ulong Id, string Name, string ContentType, ulong SizeBytes,
    DateTime CreatedAtUtc, ulong RowVersion);
public sealed record DoctorAppointmentDetailResponse(DoctorAppointmentResponse Appointment,
    IReadOnlyList<DoctorReportVersionResponse> ReportVersions, IReadOnlyList<DoctorDocumentResponse> Documents);
public sealed record DoctorWeeklyHourResponse(ulong Id, byte DayOfWeek, TimeOnly StartsAt, TimeOnly EndsAt,
    DateOnly? ValidFrom, DateOnly? ValidUntil, bool IsActive, ulong RowVersion, string ModalityCode);
public sealed record DoctorAvailabilityExceptionResponse(ulong Id, DateOnly Date, string ModalityCode,
    bool IsAvailable, TimeOnly? StartsAt, TimeOnly? EndsAt, ulong RowVersion);
public sealed record DoctorAvailabilityResponse(bool OnlineEnabled, ushort MaxOnlineDaily, ushort MaxInPersonDaily,
    ulong PreferenceRowVersion, IReadOnlyList<DoctorWeeklyHourResponse> WeeklyHours,
    IReadOnlyList<DoctorAvailabilityExceptionResponse> Exceptions);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DoctorProfileUpdateRequest([param: Required, StringLength(200, MinimumLength = 3)] string FullName,
    [param: Required, StringLength(30)] string ProfessionalTitle, [param: StringLength(4000)] string? Biography,
    [param: Range(0, 80)] ushort YearsExperience, [param: Range(10, 480)] ushort DefaultAppointmentDurationMinutes,
    [param: MinLength(1), MaxLength(20)] IReadOnlyList<uint> SpecialtyIds, [param: Range(1, int.MaxValue)] uint PrimarySpecialtyId,
    bool EmailEnabled, bool SmsEnabled, ulong AccountRowVersion, ulong ProfileRowVersion, ulong PreferenceRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DoctorServicesUpdateRequest([param: MaxLength(200)] IReadOnlyList<uint> AppointmentTypeIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DoctorAvailabilitySettingsRequest(bool OnlineEnabled, [param: Range(0, 100)] ushort MaxOnlineDaily,
    [param: Range(0, 100)] ushort MaxInPersonDaily, [param: Range(1, long.MaxValue)] ulong RowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DoctorAvailabilityExceptionRequest(DateOnly Date,
    [param: Required, RegularExpression("^(in_person|online|both)$")] string ModalityCode, bool IsAvailable,
    TimeOnly? StartsAt, TimeOnly? EndsAt, [param: Range(0, long.MaxValue)] ulong RowVersion) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IsAvailable && (!StartsAt.HasValue || !EndsAt.HasValue || EndsAt <= StartsAt)
            || !IsAvailable && (StartsAt.HasValue || EndsAt.HasValue))
            yield return new ValidationResult("Informe uma faixa válida ou bloqueie o dia inteiro.", [nameof(StartsAt), nameof(EndsAt)]);
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DoctorPatientLinkRequest([param: Required, StringLength(254)] string Identifier);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DoctorPatientInviteRequest([param: Required, StringLength(200, MinimumLength = 3)] string FullName,
    [param: EmailAddress, StringLength(254)] string? Email, [param: StringLength(20)] string? PhoneE164,
    DateOnly? BirthDate);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DoctorPatientUpdateRequest([param: StringLength(200)] string? PreferredName,
    [param: Range(1, long.MaxValue)] ulong RowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DoctorReportRectificationRequest([param: Range(1, long.MaxValue)] ulong ReportRowVersion,
    [param: Required, StringLength(12000, MinimumLength = 20)] string ClinicalSummary,
    [param: StringLength(8000)] string? Recommendations,
    [param: Required, StringLength(1000, MinimumLength = 5)] string Reason) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((ClinicalSummary?.Trim().Length ?? 0) is < 20 or > 12000)
            yield return new ValidationResult("O resumo clínico deve ter entre 20 e 12.000 caracteres.", [nameof(ClinicalSummary)]);
        if (Recommendations?.Trim().Length > 8000)
            yield return new ValidationResult("As recomendações devem ter no máximo 8.000 caracteres.", [nameof(Recommendations)]);
        if ((Reason?.Trim().Length ?? 0) is < 5 or > 1000)
            yield return new ValidationResult("O motivo deve ter entre 5 e 1.000 caracteres.", [nameof(Reason)]);
    }
}
