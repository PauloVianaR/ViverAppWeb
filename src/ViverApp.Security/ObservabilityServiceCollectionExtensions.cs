using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ViverApp.Security;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddViverAppObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName)
    {
        var options = configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        var endpoint = options.ValidateAndGetEndpoint(environment.IsDevelopment());

        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName));

        telemetry.WithTracing(tracing =>
        {
            tracing
                .SetSampler(new ParentBasedSampler(
                    new TraceIdRatioBasedSampler(options.TraceSamplingRatio)))
                .AddAspNetCoreInstrumentation(instrumentation =>
                    instrumentation.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation();
            if (endpoint is not null)
            {
                tracing.AddOtlpExporter(exporter => exporter.Endpoint = endpoint);
            }
        });

        telemetry.WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation();
            if (endpoint is not null)
            {
                metrics.AddOtlpExporter(exporter => exporter.Endpoint = endpoint);
            }
        });

        return services;
    }
}
