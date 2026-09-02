namespace ViverApp.Api.Features.Identity;

public static class ViverAppRoles
{
    public const string Patient = "patient";
    public const string Doctor = "doctor";
    public const string Manager = "manager";
    public const string Administrator = "administrator";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Patient, Doctor, Manager, Administrator],
        StringComparer.Ordinal);
}
