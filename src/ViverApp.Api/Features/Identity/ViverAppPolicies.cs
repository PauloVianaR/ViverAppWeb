namespace ViverApp.Api.Features.Identity;

public static class ViverAppPolicies
{
    public const string Patient = "role:patient";
    public const string Doctor = "role:doctor";
    public const string Manager = "role:manager";
    public const string Administrator = "role:administrator";
    public const string ClinicalStaff = "role:clinical-staff";
    public const string MfaEnrollment = "identity:mfa-enrollment";
    public const string MfaSatisfied = "identity:mfa-satisfied";
}
