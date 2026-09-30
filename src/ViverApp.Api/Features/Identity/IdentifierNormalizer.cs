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
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 25 || !PhoneCharacters().IsMatch(trimmed)
            || trimmed.IndexOf('+', 1) >= 0)
            return null;
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        if (trimmed.StartsWith('+'))
        {
            if (!digits.StartsWith("55", StringComparison.Ordinal)) return null;
            digits = digits[2..];
        }
        else if (digits.Length is 12 or 13 && digits.StartsWith("55", StringComparison.Ordinal))
            digits = digits[2..];
        return NationalPhone().IsMatch(digits) ? $"+55{digits}" : null;
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

    [GeneratedRegex("^[+()0-9\\s.\\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneCharacters();

    [GeneratedRegex("^[1-9][0-9]{9,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex NationalPhone();
}
