using System.Diagnostics.Metrics;

namespace ViverApp.Api.Infrastructure.Observability;

internal static class OperationalTelemetry
{
    private static readonly Meter Meter = new("ViverApp.Operations", "1.0.0");
    private static readonly Counter<long> CheckoutAttempts = Meter.CreateCounter<long>("viverapp.checkout.provider.attempts");
    private static readonly Histogram<double> CheckoutDuration = Meter.CreateHistogram<double>(
        "viverapp.checkout.provider.duration", "ms");
    private static readonly Counter<long> AnalyticsExports = Meter.CreateCounter<long>("viverapp.analytics.exports");
    private static readonly Histogram<double> AnalyticsDuration = Meter.CreateHistogram<double>(
        "viverapp.analytics.export.duration", "ms");

    internal static void RecordCheckout(bool succeeded, double milliseconds)
    {
        var outcome = new KeyValuePair<string, object?>("outcome", succeeded ? "success" : "failure");
        CheckoutAttempts.Add(1, outcome);
        CheckoutDuration.Record(milliseconds, outcome);
    }

    internal static void RecordAnalyticsExport(bool succeeded, double milliseconds)
    {
        var outcome = new KeyValuePair<string, object?>("outcome", succeeded ? "success" : "failure");
        AnalyticsExports.Add(1, outcome);
        AnalyticsDuration.Record(milliseconds, outcome);
    }
}
