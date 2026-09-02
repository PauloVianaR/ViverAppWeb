using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using ViverApp.Api.Infrastructure.Persistence;
using ViverApp.Security;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.MaxConcurrentConnections = 500;
    options.Limits.MaxConcurrentUpgradedConnections = 100;
    options.Limits.MaxRequestBodySize = 1_048_576;
    options.Limits.MaxRequestHeaderCount = 64;
    options.Limits.MaxRequestHeadersTotalSize = 32_768;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
});
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Detail = null;
        context.ProblemDetails.Instance = null;
        context.ProblemDetails.Extensions["correlationId"] =
            CorrelationIdMiddleware.GetCorrelationId(context.HttpContext);
    };
});
builder.Services
    .AddControllers(options =>
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(options => options.JsonSerializerOptions.MaxDepth = 32);
builder.Services.AddViverAppSecurityBaseline(
    builder.Configuration,
    builder.Environment,
    SecuritySurface.Api);
builder.Services.AddViverAppObservability(
    builder.Configuration,
    builder.Environment,
    "ViverApp.Api");
builder.Services.AddViverAppDatabase(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), ["live"])
    .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

app.UseViverAppSecurityBaseline(SecuritySurface.Api);
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseRequestTimeouts();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthChecks(
        "/health/live",
        new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
            ResponseWriter = MinimalHealthResponseWriter.WriteAsync,
        })
    .AllowAnonymous()
    .DisableRateLimiting()
    .DisableRequestTimeout();
app.MapHealthChecks(
        "/health/ready",
        new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = MinimalHealthResponseWriter.WriteAsync,
        })
    .AllowAnonymous()
    .DisableRateLimiting()
    .DisableRequestTimeout();
app.MapControllers().RequireCors(SecurityPolicyNames.WebClientCors);

app.Run();

public partial class Program;
