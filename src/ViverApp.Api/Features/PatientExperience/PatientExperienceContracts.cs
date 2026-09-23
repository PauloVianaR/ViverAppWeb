using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientScheduling;

namespace ViverApp.Api.Features.PatientExperience;

public sealed record PatientClinicResponse(string Name, string Address, string? Phone, string? RouteUrl, string Timezone);
public sealed record PatientServiceResponse(uint Id, string Name, string? Description, string CategoryCode, string ModalityCode,
    ushort DurationMinutes, decimal BasePrice, decimal DiscountPercent, decimal PriceAmount, bool RequiresPayment);
public sealed record PatientPromotionResponse(string Title, string Description, string? Url);
public sealed record PatientHomeResponse(string FullName, bool IsPremium, PatientClinicResponse Clinic,
    PatientAppointmentResponse? NextAppointment, IReadOnlyList<PatientPromotionResponse> Promotions);
public sealed record PatientAppointmentResponse(AppointmentResponse Appointment, string CategoryCode, string Specialties,
    string PaymentStatus, string PaymentLocation, decimal BasePrice, decimal DiscountPercent, bool CanPay,
    bool CanCancel, bool CanReschedule, bool CanJoinOnline, bool HasReport, byte? Rating, bool CanChooseClinic);
public sealed record PatientPaymentItem(ulong AppointmentId, ulong? PaymentId, string Service, string DoctorName,
    DateTime StartsAtUtc, string StatusCode, decimal Amount, string? Method, string Location, DateTime? PaidAtUtc,
    bool CanChooseClinic, bool CanPay, string ModalityCode);
public sealed record PatientProfileAddressResponse(string? PostalCode, string? Street, string? Number, string? Complement,
    string? District, string? City, string? StateCode);
public sealed record PatientProfileResponse(string FullName, string? Email, string? Phone, bool EmailConfirmed,
    bool PhoneConfirmed, string? TaxId, DateOnly? BirthDate, PatientProfileAddressResponse? Address,
    bool EmailEnabled, bool SmsEnabled, ulong RowVersion);
public sealed record PatientPremiumResponse(bool IsPremium, ulong? MembershipId, string StatusCode,
    string? RejectionReason, DateTime? ReviewedAtUtc, DateTime? NextRequestAtUtc, bool CanRequest,
    decimal DiscountPercent, ulong RowVersion, Guid? ProofDocumentId, IReadOnlyList<PatientPremiumPlan> Plans);
public sealed record PatientPremiumPlan(uint Id, string Name, decimal DiscountPercent);
public sealed record PatientDocumentResponse(Guid Id, string Name, string ContentType, uint SizeBytes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatientProfileRequest(
    [param: Required, StringLength(200, MinimumLength = 3)] string FullName,
    [param: Required, RegularExpression("^[0-9]{11}$")] string TaxId, DateOnly BirthDate,
    [param: Required] RegistrationAddressRequest Address, bool EmailEnabled, bool SmsEnabled,
    [param: Range(1, long.MaxValue)] ulong RowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatientReviewRequest([param: Range(1, 5)] byte Rating, [param: StringLength(1000)] string? Comment);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatientPremiumCancelRequest([param: Range(1, long.MaxValue)] ulong RowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatientContactRequest([param: Required, RegularExpression("^(email|sms)$")] string Channel,
    [param: Required, StringLength(254)] string Destination);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatientContactConfirmRequest(Guid RequestId, [param: Required, RegularExpression("^[0-9]{6}$")] string Code);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeOwnPasswordRequest([param: Required, StringLength(128, MinimumLength = 8)] string NewPassword);

public sealed class PatientExperienceException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
