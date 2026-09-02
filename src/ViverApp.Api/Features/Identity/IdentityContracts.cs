using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ViverApp.Api.Features.Identity;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterRequest(
    [param: Required, StringLength(200, MinimumLength = 3)] string FullName,
    [param: EmailAddress, StringLength(254)] string? Email,
    [param: StringLength(16)] string? Phone,
    [param: Required, StringLength(128, MinimumLength = 12)] string Password,
    [param: Required, RegularExpression("^(email|sms)$")] string VerificationChannel);

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
    string FullName,
    bool EmailVerified,
    bool PhoneVerified,
    bool MfaSatisfied);

public sealed record SessionResponse(
    Guid Id,
    bool IsCurrent,
    string AuthenticationMethod,
    bool MfaSatisfied,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? LastSeenAtUtc);
