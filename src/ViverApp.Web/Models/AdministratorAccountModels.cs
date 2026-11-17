namespace ViverApp.Web;

public sealed record AdminAccountDetails(ulong Id, string RoleCode, string StatusCode, bool PortalAccessEnabled,
    bool PasswordSetupRequired, bool EmailVerified, bool PhoneVerified, ulong RowVersion, AdminAccountData Data);
public sealed record AdminAccountData(string FullName, string? Email, string? Phone, string? TaxId, DateOnly? BirthDate,
    AdminPatientData? Patient, AdminProfessionalData? Professional);
public sealed record AdminPatientData(string? PreferredName, string? AdministrativeNotes, AdminAccountAddress? Address);
public sealed record AdminAccountAddress(string? PostalCode, string? Street, string? Number, string? Complement,
    string? District, string? City, string? StateCode);
public sealed record AdminProfessionalData(AdminProfessionalRegistration Registration, string? Biography,
    ushort DefaultAppointmentDurationMinutes, IReadOnlyList<uint> SpecialtyIds, ulong? RowVersion);
public sealed record AdminProfessionalRegistration(string ProfessionalTitle, string LicenseStateCode,
    string LicenseNumber, ushort YearsExperience, uint PrimarySpecialtyId);
public sealed record AdminAccountSaved(AdminAccountDetails Account, bool WelcomeEmailQueued);
