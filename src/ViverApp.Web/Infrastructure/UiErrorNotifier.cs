namespace ViverApp.Web;

public sealed class UiErrorNotifier
{
    public const string UnexpectedMessage =
        "Ocorreu um erro interno não classificado. Contate o administrador do sistema.";

    public event Action<string>? ErrorRaised;

    public void Show(string? message) => ErrorRaised?.Invoke(Normalize(message));

    public static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return UnexpectedMessage;
        var value = RemoveTechnicalSuffix(message.Trim());
        if (value.Contains("One or more validation errors occurred", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Internal Server Error", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("An error occurred", StringComparison.OrdinalIgnoreCase)
            || value.Contains("TypeError", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Failed to fetch", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Failed to complete negotiation", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase))
            return UnexpectedMessage;
        return value;
    }

    private static string RemoveTechnicalSuffix(string value)
    {
        if (value.StartsWith("Error: ", StringComparison.OrdinalIgnoreCase))
            value = value[7..].TrimStart();

        var suffixes = new[] { "\r\nError:", "\nError:", " Error:", "\r\n   at ", "\n   at ", " at read (", " at async " };
        var firstSuffix = suffixes
            .Select(suffix => value.IndexOf(suffix, StringComparison.OrdinalIgnoreCase))
            .Where(index => index > 0)
            .DefaultIfEmpty(-1)
            .Min();

        return firstSuffix > 0 ? value[..firstSuffix].Trim() : value;
    }
}
