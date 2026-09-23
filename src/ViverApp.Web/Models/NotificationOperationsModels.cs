namespace ViverApp.Web.Models;

public sealed record WebNotificationQueueSummary(
    int Pending, int Processing, int Sent, int Suppressed, int DeadLetter,
    double? OldestPendingMinutes, int ScheduledPending, int ScheduledDeadLetter);

public sealed record WebNotificationDeadLetter(
    ulong Id, string Channel, string TemplateKey, ushort TemplateVersion,
    ushort Attempts, string? ErrorCode, DateTime CreatedAtUtc);
