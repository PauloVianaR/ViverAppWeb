using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ViverApp.Api.Features.Identity;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterRequest(
    [param: Required, StringLength(200, MinimumLength = 3)] string FullName,
    [param: EmailAddress, StringLength(254)] string? Email,
    [param: Required, StringLength(16)] string Phone,
    [param: Required, StringLength(128, MinimumLength = 12)] string Password,
    [param: Required, RegularExpression("^(email|sms)$")] string VerificationChannel,
    [param: Required, RegularExpression("^(patient|doctor|manager)$")] string RoleCode,
    [param: Required, RegularExpression("^[0-9]{11}$")] string TaxId,
    DateOnly BirthDate,
    bool TermsAccepted,
    RegistrationAddressRequest? Address,
    DoctorRegistrationRequest? Doctor,
    [param: StringLength(100)] string? Website = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegistrationAddressRequest(
    [param: Required, RegularExpression("^[0-9]{8}$")] string PostalCode,
    [param: Required, StringLength(200, MinimumLength = 2)] string Street,
    [param: Required, StringLength(20, MinimumLength = 1)] string Number,
    [param: StringLength(100)] string? Complement,
    [param: Required, StringLength(100, MinimumLength = 2)] string District,
    [param: Required, StringLength(100, MinimumLength = 2)] string City,
    [param: Required, RegularExpression("^[A-Z]{2}$")] string StateCode);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DoctorRegistrationRequest(
    [param: Required, RegularExpression("^(Dr\\.|Dra\\.)$")] string ProfessionalTitle,
    [param: Required, RegularExpression("^[A-Z]{2}$")] string LicenseStateCode,
    [param: Required, StringLength(30, MinimumLength = 1)] string LicenseNumber,
    [param: Range(0, 80)] ushort YearsExperience,
    [param: Range(typeof(uint), "1", "4294967295")] uint PrimarySpecialtyId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GoogleRegistrationRequest(
    [param: Required, StringLength(8192)] string OnboardingToken,
    [param: Required, StringLength(16)] string Phone,
    [param: Required, RegularExpression("^[0-9]{11}$")] string TaxId,
    DateOnly BirthDate,
    bool TermsAccepted,
    RegistrationAddressRequest? Address,
    DoctorRegistrationRequest? Doctor,
    [param: StringLength(100)] string? Website = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentifierChallengeRequest(
    [param: Required, StringLength(254)] string Identifier,
    [param: Required, RegularExpression("^(email|sms)$")] string Channel);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChallengeCodeRequest(
    Guid RequestId,
    [param: Required, StringLength(20, MinimumLength = 6)] string Code);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasswordResetRequest(
    Guid RequestId,
    [param: Required, StringLength(20, MinimumLength = 6)] string Code,
    [param: Required, StringLength(128, MinimumLength = 12)] string NewPassword);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasswordLoginRequest(
    [param: Required, StringLength(254)] string Identifier,
    [param: Required, StringLength(128)] string Password,
    [param: StringLength(20)] string? TotpCode,
    [param: StringLength(32)] string? RecoveryCode,
    bool RememberMe = false);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MfaCodeRequest(
    [param: StringLength(20)] string? TotpCode,
    [param: StringLength(32)] string? RecoveryCode);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasskeyCredentialRequest(
    [param: Required, StringLength(131072)] string CredentialJson,
    [param: StringLength(100)] string? DisplayName,
    bool RememberMe = false);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasskeyOptionsRequest([param: StringLength(254)] string? Identifier);

public sealed record ChallengeAcceptedResponse(Guid RequestId, string Message);

public sealed record AuthenticationResponse(
    bool Authenticated,
    string Role,
    bool RequiresMfa,
    bool RequiresMfaEnrollment);

public sealed record CurrentAccountResponse(
    string Id,
    string Role,
    string Status,
    string FullName,
    bool EmailVerified,
    bool PhoneVerified,
    bool MfaSatisfied,
    string Destination);

public sealed record RegistrationOption(uint Id, string Name);

public sealed record RegistrationOptionsResponse(
    string TermsVersion,
    string PrivacyVersion,
    IReadOnlyList<RegistrationOption> Specialties);

public sealed record RegistrationCompletedResponse(
    string Status,
    string Role,
    bool Authenticated,
    string Destination);

public sealed record SessionResponse(
    Guid Id,
    bool IsCurrent,
    string AuthenticationMethod,
    bool MfaSatisfied,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? LastSeenAtUtc);

public sealed record PasskeyResponse(
    string CredentialId,
    string DisplayName,
    DateTime CreatedAtUtc);
