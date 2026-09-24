namespace ViverApp.Web;

public sealed record WebCurrentAccount(
    string Id,
    string Role,
    string Status,
    string FullName,
    bool EmailVerified,
    bool PhoneVerified,
    bool MfaSatisfied,
    string Destination);

public sealed record WebAuthenticationResult(
    bool Authenticated,
    string Role,
    bool RequiresMfa,
    bool RequiresMfaEnrollment);

public sealed record WebChallengeResult(Guid RequestId, string Message);

public sealed record WebRegistrationOption(uint Id, string Name);

public sealed record WebRegistrationOptions(
    string TermsVersion,
    string PrivacyVersion,
    IReadOnlyList<WebRegistrationOption> Specialties);

public sealed record WebRegistrationCompleted(
    string Status,
    string Role,
    bool Authenticated,
    string Destination);

public sealed record WebProfessional(
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
    IReadOnlyList<WebRegistrationOption> Specialties,
    uint? PrimarySpecialtyId,
    ulong AccountRowVersion,
    ulong? ProfileRowVersion);

public sealed record WebSession(Guid Id, bool IsCurrent, string AuthenticationMethod, bool MfaSatisfied, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, DateTime? LastSeenAtUtc);


public sealed record WebNotificationPreferences(
    bool ReminderEmailEnabled, bool ReminderSmsEnabled,
    bool PremiumUpdatesEnabled, ulong RowVersion);

public sealed record WebPostalCode(string Street, string? Complement, string District, string City, string StateCode);
