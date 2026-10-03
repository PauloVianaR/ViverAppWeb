using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.CashManagement;
using ViverApp.Api.Features.AdministratorExperience;
using ViverApp.Api.Features.DoctorExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.ManagerExperience;
using ViverApp.Api.Features.MedicalRecords;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Features.Payments;
using ViverApp.Api.Features.PatientExperience;
using ViverApp.Api.Features.Calendar;
using ViverApp.Api.Features.Notifications;
using ViverApp.Api.Infrastructure.Persistence;
using ViverApp.Security;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.GetValue("Homologation:Enabled", false) && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("A homologação local só pode ser executada em Development.");
if (!builder.Environment.IsDevelopment())
    WindowsDocumentMalwareScanner.AssertProductionReady(builder.Configuration, builder.Environment.ContentRootPath);
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
    options.Limits.MaxRequestBodySize = 10_600_000;
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
builder.Services.AddExceptionHandler<IdentitySmsUnavailableHandler>();
builder.Services
    .AddControllersWithViews(options =>
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
builder.Services.AddViverAppPrivateStorage(builder.Configuration, builder.Environment);
builder.Services.AddViverAppIdentity(builder.Configuration, builder.Environment);
builder.Services.AddViverAppNotifications(builder.Configuration);
builder.Services.AddViverAppPayments(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient("PostalCodeLookup", client =>
{
    client.BaseAddress = new Uri("https://viacep.com.br/");
    client.Timeout = TimeSpan.FromSeconds(4);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ViverAppWeb/1.0");
});
builder.Services.AddScoped<IPatientSchedulingAuditWriter, PatientSchedulingAuditWriter>();
builder.Services.AddScoped<PatientSchedulingService>();
builder.Services.AddScoped<CalendarService>();
builder.Services.AddScoped<IClinicalOperationsAuditWriter, ClinicalOperationsAuditWriter>();
builder.Services.AddScoped<ClinicalOperationsService>();
builder.Services.AddScoped<MedicalRecordService>();
builder.Services.AddScoped<MedicalRecordExceptionFilter>();
builder.Services.AddSingleton<ClinicalPdfRenderer>();
builder.Services.AddScoped<CashManagementService>();
builder.Services.AddScoped<CashRuleExceptionFilter>();
builder.Services.AddScoped<ViverApp.Api.Features.ArrivalExperience.ArrivalExperienceService>();
builder.Services.AddScoped<DoctorExperienceService>();
builder.Services.AddScoped<DoctorExperienceExceptionFilter>();
builder.Services.AddScoped<ManagerExperienceService>();
builder.Services.AddScoped<ManagerExperienceExceptionFilter>();
builder.Services.AddScoped<AdministratorExperienceService>();
builder.Services.AddScoped<AdministratorAnalyticsExportService>();
builder.Services.AddHostedService<AdministratorAnalyticsExportWorker>();
builder.Services.AddScoped<AdministratorExceptionFilter>();
builder.Services.AddScoped<AdministratorStepUpFilter>();
builder.Services.AddScoped<PatientExperienceService>();
builder.Services.AddScoped<PrivateDocumentStore>();
builder.Services.AddScoped<IDocumentMalwareScanner, WindowsDocumentMalwareScanner>();
builder.Services.AddScoped<PatientExperienceExceptionFilter>();
builder.Services.AddScoped<RecentAuthentication>();
builder.Services.AddScoped<TeleconsultationAccess>();
builder.Services.AddScoped<TeleconsultationGuestService>();
builder.Services.AddScoped<TeleconsultationPresence>();
builder.Services.AddSingleton<VideoInvocationLimiter>();
var signalR = builder.Services.AddSignalR(options => { options.EnableDetailedErrors = false; options.MaximumReceiveMessageSize = 65536; options.MaximumParallelInvocationsPerClient = 1; });
var redisConnection = builder.Configuration["SignalR:RedisConnectionString"];
if (builder.Configuration.GetValue("SignalR:DistributedDeployment", false) && string.IsNullOrWhiteSpace(redisConnection))
    throw new InvalidOperationException("SignalR:RedisConnectionString é obrigatório em implantação distribuída.");
if (!string.IsNullOrWhiteSpace(redisConnection))
    signalR.AddStackExchangeRedis(redisConnection, options => options.Configuration.ChannelPrefix = RedisChannel.Literal("ViverAppWeb"));
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

if (!allowInsecureLocalHttp) app.UseHttpsRedirection();
app.UseRouting();
app.UseCors();
app.UseRequestTimeouts();
app.UseAuthentication();
app.UseMiddleware<MaintenanceModeMiddleware>();
app.UseRateLimiter();
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
app.MapHub<TeleconsultationHub>("/hubs/teleconsultation").RequireCors(SecurityPolicyNames.WebClientCors).RequireRateLimiting(SecurityPolicyNames.VideoRateLimit).DisableRequestTimeout();
app.MapHub<ViverApp.Api.Features.ArrivalExperience.ProfessionalNotificationsHub>("/hubs/doctor-notifications").RequireCors(SecurityPolicyNames.WebClientCors).RequireRateLimiting(SecurityPolicyNames.VideoRateLimit).DisableRequestTimeout();

app.Run();

public partial class Program;
