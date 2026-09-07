using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.DoctorExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.ManagerExperience;
using Xunit;

namespace ViverApp.ClinicalOperations.Tests;

public sealed class ClinicalOperationsApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Delivery:Enabled"] = "false",
                }));
        });

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Theory]
    [InlineData("/api/v1/clinical/context")]
    [InlineData("/api/v1/clinical/appointments?from=2026-01-01&to=2026-01-02")]
    [InlineData("/api/v1/clinical/patients")]
    public async Task ClinicalEndpoints_RequireAuthentication(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ClinicalStaffPolicy_AllowsOnlyDoctorManagerAndAdministrator()
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await provider.GetPolicyAsync(ViverAppPolicies.ClinicalStaff);
        var roles = Assert.Single(policy!.Requirements.OfType<RolesAuthorizationRequirement>()).AllowedRoles;
        Assert.Equal([ViverAppRoles.Doctor, ViverAppRoles.Manager, ViverAppRoles.Administrator], roles);
    }

    [Fact]
    public void MedicalReportContract_RejectsShortOrOversizedClinicalContent()
    {
        var shortRequest = new MedicalReportWriteRequest(0, "muito curto", null);
        var shortResults = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(shortRequest, new ValidationContext(shortRequest), shortResults, true));

        var oversizedRequest = new MedicalReportWriteRequest(0, new string('A', 20), new string('B', 8001));
        var oversizedResults = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(oversizedRequest, new ValidationContext(oversizedRequest), oversizedResults, true));
    }

    [Fact]
    public async Task WebClientCors_AllowsAntiforgeryAndIdempotencyHeaders()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/patient/appointments");
        request.Headers.Add("Origin", "https://localhost:7110");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "idempotency-key,x-csrf-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var allowedHeaders = string.Join(',', response.Headers.GetValues("Access-Control-Allow-Headers"));
        Assert.Contains("idempotency-key", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("x-csrf-token", allowedHeaders, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/api/v1/doctor/home")]
    [InlineData("/api/v1/doctor/profile")]
    [InlineData("/api/v1/doctor/patients")]
    [InlineData("/api/v1/doctor/agenda?from=2026-01-01&to=2026-01-02")]
    [InlineData("/api/v1/doctor/appointments/1")]
    [InlineData("/api/v1/doctor/documents/1")]
    public async Task DoctorEndpoints_RequireAuthentication(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void DoctorContracts_RejectInvalidAvailabilityAndRectification()
    {
        var exception = new DoctorAvailabilityExceptionRequest(DateOnly.FromDateTime(DateTime.Today), "online", true, null, null, 0);
        var exceptionResults = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(exception, new ValidationContext(exception), exceptionResults, true));

        var rectification = new DoctorReportRectificationRequest(1, "curto", null, "x");
        var rectificationResults = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(rectification, new ValidationContext(rectification), rectificationResults, true));
    }

    [Theory]
    [InlineData("/api/v1/manager/home")]
    [InlineData("/api/v1/manager/profile")]
    [InlineData("/api/v1/manager/patients")]
    [InlineData("/api/v1/manager/agenda?from=2026-01-01&to=2026-01-02")]
    [InlineData("/api/v1/manager/premium")]
    public async Task ManagerEndpoints_RequireAuthentication(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void ManagerContracts_RejectForgedCardAndWeakPatientData()
    {
        var card = new ManagerPaymentConfirmRequest("credit_card", DateTime.UtcNow, "12ab", null, 0);
        var cardResults = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(card, new ValidationContext(card), cardResults, true));

        var patient = new ManagerPatientCreateRequest("A", "invalid", null, null);
        var patientResults = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(patient, new ValidationContext(patient), patientResults, true));
    }
}
