namespace ViverApp.Api.Features.Identity;

public static class BrazilianDocumentValidator
{
    public static bool IsValidCpf(string? value)
    {
        if (value is null || value.Length != 11 || value.Any(character => character is < '0' or > '9')
            || value.Distinct().Count() == 1)
        {
            return false;
        }

        return CheckDigit(value, 9) == value[9] - '0'
            && CheckDigit(value, 10) == value[10] - '0';
    }

    private static int CheckDigit(string value, int length)
    {
        var sum = 0;
        for (var index = 0; index < length; index++)
        {
            sum += (value[index] - '0') * (length + 1 - index);
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
