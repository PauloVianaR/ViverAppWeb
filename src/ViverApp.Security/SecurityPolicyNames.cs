namespace ViverApp.Security;

public static class SecurityPolicyNames
{
    public const string WebClientCors = "web-client";
    public const string PublicFormRateLimit = "public-form";
    public const string SensitiveRateLimit = "sensitive";
    public const string WriteRateLimit = "write";
    public const string UploadRateLimit = "patient-upload";
    public const string SlotRateLimit = "patient-slots";
    public const string CheckoutRateLimit = "patient-checkout";
    public const string VideoRateLimit = "patient-video";
    public const string LongRunningRequestTimeout = "long-running";
}
