namespace ViverApp.Security;

public sealed class HoneypotDetector
{
    public const string FieldName = "company_website";

    public bool IsTriggered(IEnumerable<string?> values)
    {
        return values.Any(value => !string.IsNullOrWhiteSpace(value));
    }
}
