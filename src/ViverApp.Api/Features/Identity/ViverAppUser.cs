namespace ViverApp.Api.Features.Identity;

public sealed class ViverAppUser
{
    public ulong Id { get; set; }

    public string? UserName { get; set; }

    public string? NormalizedUserName { get; set; }

    public string RoleCode { get; set; } = ViverAppRoles.Patient;

    public string StatusCode { get; set; } = "pending_confirmation";

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    public bool EmailConfirmed { get; set; }

    public string? PhoneNumber { get; set; }

    public bool PhoneNumberConfirmed { get; set; }

    public string? TaxId { get; set; }

    public DateTime? BirthDate { get; set; }

    public string? PreferredRecoveryChannel { get; set; }

    public string? PasswordHash { get; set; }

    public string SecurityStamp { get; set; } = string.Empty;

    public int AccessFailedCount { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public bool LockoutEnabled { get; set; } = true;

    public bool TwoFactorEnabled { get; set; }

    public ulong RowVersion { get; set; }
}
