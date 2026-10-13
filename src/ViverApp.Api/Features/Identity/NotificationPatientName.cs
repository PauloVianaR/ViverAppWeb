namespace ViverApp.Api.Features.Identity;

public static class NotificationPatientName
{
    public static string FirstTwo(string? name) => string.Join(" ",
        (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(2));
}
