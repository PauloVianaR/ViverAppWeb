using System.Globalization;
using System.Text;

namespace ViverApp.Web;

public sealed record WebBookingWorkspaceSpecialty(uint Id, string Name);
public sealed record WebBookingWorkspaceOptions(uint? DefaultSpecialtyId, IReadOnlyList<WebBookingWorkspaceSpecialty> Specialties);
public sealed record WebBookingWorkspaceMetadata(string BillingFormCode, uint? SpecialtyId, ulong RowVersion, string? SpecialtyName = null,
    string StatusCode = "", DateTime StartsAtUtc = default, DateTime EndsAtUtc = default);

public static class BookingWorkspaceLabels
{
    public static string Specialty(WebBookingWorkspaceMetadata? metadata, IReadOnlyList<WebSpecialty>? catalog)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.SpecialtyName)) return metadata.SpecialtyName.Trim();
        var name = catalog?.FirstOrDefault(x => x.Id == metadata?.SpecialtyId)?.Name;
        return string.IsNullOrWhiteSpace(name) ? "Não informada" : name;
    }
}

public static class PatientSearchKey
{
    public static string Normalize(string value)
    {
        var key = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
                key.Append(char.ToUpperInvariant(character));
        return key.ToString();
    }
}
