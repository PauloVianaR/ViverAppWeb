using System.Globalization;
using System.Text;

namespace ViverApp.Web;

public sealed record WebBookingWorkspaceSpecialty(uint Id, string Name);
public sealed record WebBookingWorkspaceOptions(uint? DefaultSpecialtyId, IReadOnlyList<WebBookingWorkspaceSpecialty> Specialties);
public sealed record WebBookingWorkspaceMetadata(string BillingFormCode, uint? SpecialtyId, ulong RowVersion, string? SpecialtyName = null);

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
