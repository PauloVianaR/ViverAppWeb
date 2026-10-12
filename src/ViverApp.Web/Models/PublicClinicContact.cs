namespace ViverApp.Web;

public sealed record PublicClinicContact(string LegalName, string DisplayName, string? Email,
    string? PhoneE164, string? Street, string? Number, string? Complement, string? District,
    string? City, string? StateCode, string? PostalCode)
{
    public string Address => string.Join(", ", new[] { Street, Number, Complement, District, City, StateCode, PostalCode }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}
