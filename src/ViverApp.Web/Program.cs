using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ViverApp.Security;
using ViverApp.Web;
using ViverApp.Web.Components;

var builder = WebApplication.CreateBuilder(args);
var allowInsecureLocalHttp = builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue("Security:AllowInsecureLocalHttp", false);
if (allowInsecureLocalHttp)
{
    builder.Configuration["Kestrel:Certificates:Default:Path"] = null;
    builder.Configuration["Kestrel:Certificates:Default:Password"] = null;
}

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

builder.Services.AddViverAppSecurityBaseline(
    builder.Configuration,
    builder.Environment,
    SecuritySurface.Web);
builder.Services.AddViverAppObservability(
    builder.Configuration,
    builder.Environment,
    "ViverApp.Web");
builder.Services.AddSingleton(WebBackendOptions.Load(builder.Configuration, builder.Environment));
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), ["live", "ready"]);
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseViverAppSecurityBaseline(SecuritySurface.Web);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (!allowInsecureLocalHttp) app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapHealthChecks(
        "/health/live",
        new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
            ResponseWriter = MinimalHealthResponseWriter.WriteAsync,
        })
    .DisableRateLimiting();
app.MapHealthChecks(
        "/health/ready",
        new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = MinimalHealthResponseWriter.WriteAsync,
        })
    .DisableRateLimiting();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
