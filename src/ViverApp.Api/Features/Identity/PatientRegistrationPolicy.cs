using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;

namespace ViverApp.Api.Features.Identity;

public static class PatientRegistrationPolicy
{
    public const string AllowWithoutCpfKey = "patients.allow_registration_without_cpf";

    public static async Task<bool> AllowsWithoutCpfAsync(ViverAppDbContext database, CancellationToken ct)
    {
        var value = await database.ApplicationSettings.AsNoTracking()
            .Where(x => x.SettingKey == AllowWithoutCpfKey).Select(x => x.ValueJson).SingleOrDefaultAsync(ct);
        return string.Equals(value?.Trim(), "true", StringComparison.Ordinal);
    }

    public static async Task<bool> IsValidCpfAsync(ViverAppDbContext database, string roleCode, string? taxId, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(taxId)
            ? roleCode == ViverAppRoles.Patient && await AllowsWithoutCpfAsync(database, ct)
            : BrazilianDocumentValidator.IsValidCpf(taxId);
}
