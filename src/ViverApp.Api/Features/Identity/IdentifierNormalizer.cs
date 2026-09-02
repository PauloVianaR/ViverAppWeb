using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace ViverApp.Api.Features.Identity;

public static partial class IdentifierNormalizer
{
    public static string? NormalizeEmail(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 254)
        {
            return null;
        }

        try
        {
            var address = new MailAddress(trimmed);
            return string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase)
                ? address.Address.ToUpperInvariant()
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string? NormalizePhone(string? value)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed) && E164().IsMatch(trimmed)
            ? trimmed
            : null;
    }

    public static string? NormalizeIdentifier(string? value)
    {
        return NormalizePhone(value) ?? NormalizeEmail(value);
    }

    public static string NormalizeCode(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray())
            .ToUpper(CultureInfo.InvariantCulture);
    }

    [GeneratedRegex("^\\+55[1-9][0-9]{9,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164();
}
