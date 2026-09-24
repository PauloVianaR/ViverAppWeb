using System.Diagnostics.Metrics;

namespace ViverApp.Api.Features.Notifications;

internal static class NotificationTelemetry
{
    private static readonly Meter Meter = new("ViverApp.Notifications", "1.0.0");
    private static readonly Counter<long> Sent = Meter.CreateCounter<long>("viverapp.notifications.sent");
    private static readonly Counter<long> Failed = Meter.CreateCounter<long>("viverapp.notifications.failed");
    private static readonly Counter<long> Suppressed = Meter.CreateCounter<long>("viverapp.notifications.suppressed");
    private static readonly Histogram<double> DeliveryLatency = Meter.CreateHistogram<double>(
        "viverapp.notifications.delivery_latency", "s");

    public static void RecordSent(string channel, DateTime createdAtUtc, DateTime nowUtc)
    {
        Sent.Add(1, new KeyValuePair<string, object?>("channel", channel));
        DeliveryLatency.Record(Math.Max(0, (nowUtc - createdAtUtc).TotalSeconds),
            new KeyValuePair<string, object?>("channel", channel));
    }

    public static void RecordFailed(string channel) =>
        Failed.Add(1, new KeyValuePair<string, object?>("channel", channel));

    public static void RecordSuppressed(string channel) =>
        Suppressed.Add(1, new KeyValuePair<string, object?>("channel", channel));
}
