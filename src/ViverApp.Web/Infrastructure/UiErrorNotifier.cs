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
        var value = message.Trim();
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
}
