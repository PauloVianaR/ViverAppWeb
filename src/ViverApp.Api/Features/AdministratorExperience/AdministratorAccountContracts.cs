using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.ManagerExperience;

namespace ViverApp.Api.Features.AdministratorExperience;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdministratorAccountData(
    [param: Required, StringLength(200, MinimumLength = 3)] string FullName,
    [param: EmailAddress, StringLength(254)] string? Email,
    [param: StringLength(25)] string? Phone,
    [param: RegularExpression("^[0-9]{11}$")] string? TaxId,
    DateOnly? BirthDate,
    AdministratorPatientData? Patient = null,
    AdministratorProfessionalData? Professional = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdministratorPatientData([param: StringLength(120)] string? PreferredName,
    [param: StringLength(2000)] string? AdministrativeNotes, ManagerPatientAddressRequest? Address);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdministratorProfessionalData([param: Required] DoctorRegistrationRequest Registration,
    [param: StringLength(4000)] string? Biography = null,
    [param: Range(5, 240)] ushort DefaultAppointmentDurationMinutes = 10,
    IReadOnlyList<uint>? SpecialtyIds = null, ulong? RowVersion = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdministratorAccountCreateRequest(
    [param: Required, RegularExpression("^(patient|doctor|psychologist|manager|administrator)$")] string RoleCode,
    [param: Required] AdministratorAccountData Data);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdministratorAccountUpdateRequest([param: Required] AdministratorAccountData Data,
    [param: Range(1, long.MaxValue)] ulong RowVersion);

public sealed record AdministratorAccountResponse(ulong Id, string RoleCode, string StatusCode,
    bool PortalAccessEnabled, bool PasswordSetupRequired, bool EmailVerified, bool PhoneVerified,
    ulong RowVersion, AdministratorAccountData Data);
public sealed record AdministratorAccountSavedResponse(AdministratorAccountResponse Account, bool WelcomeEmailQueued);
