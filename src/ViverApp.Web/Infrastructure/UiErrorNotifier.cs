namespace ViverApp.Web;

public sealed class UiErrorNotifier
{
    public const string UnexpectedMessage =
        "Ocorreu um erro interno não classificado. Contate o administrador do sistema.";

    public event Action<string>? ErrorRaised;
    public event Action<UiNotification>? NotificationRaised;
    private string? lastMessage;
    private bool lastError;
    private DateTime lastRaised;

    public void Show(string? message) => Notify(Normalize(message), true);

    public void Notify(string? message, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var text = Normalize(message);
        var now = DateTime.UtcNow;
        // A mesma falha pode vir do tratamento da operação e do aviso da página.
        if (text == lastMessage && isError == lastError && now - lastRaised < TimeSpan.FromSeconds(1)) return;
        lastMessage = text; lastError = isError; lastRaised = now;
        if (isError) ErrorRaised?.Invoke(text);
        NotificationRaised?.Invoke(new(text, isError));
    }

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
            || value.Equals("Not Found", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Bad Request", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Forbidden", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Unauthorized", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Service Unavailable", StringComparison.OrdinalIgnoreCase)
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

public sealed record UiNotification(string Message, bool IsError);
