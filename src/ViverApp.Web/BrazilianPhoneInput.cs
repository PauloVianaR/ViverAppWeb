namespace ViverApp.Web;

public static class BrazilianPhoneInput
{
    public static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("55", StringComparison.Ordinal)
            && (value.TrimStart().StartsWith('+') || digits.Length > 11))
            digits = digits[2..];
        if (digits.Length > 11) digits = digits[..11];
        if (digits.Length == 0) return string.Empty;
        if (digits.Length <= 2) return $"({digits}";
        if (digits.Length <= 7) return $"({digits[..2]}) {digits[2..]}";
        if (digits.Length == 10 && digits[2] != '9')
            return $"({digits[..2]}) {digits[2..6]}-{digits[6..]}";
        return $"({digits[..2]}) {digits[2..7]}-{digits[7..]}";
    }
}
