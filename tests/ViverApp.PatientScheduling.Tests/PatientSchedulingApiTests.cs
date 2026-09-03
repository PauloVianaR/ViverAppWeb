using System.Net;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientScheduling;
using Xunit;

namespace ViverApp.PatientScheduling.Tests;

public sealed class PatientSchedulingApiTests : IAsyncLifetime
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
    [InlineData("/api/v1/patient/booking/professionals")]
    [InlineData("/api/v1/patient/appointments")]
    public async Task PatientSchedulingEndpoints_RequireAuthentication(string path)
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
    public async Task PatientPolicy_AllowsOnlyPatientRole()
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await provider.GetPolicyAsync(ViverAppPolicies.Patient);

        var roles = Assert.Single(policy!.Requirements.OfType<RolesAuthorizationRequirement>()).AllowedRoles;
        Assert.Equal([ViverAppRoles.Patient], roles);
    }

    [Fact]
    public void PublicContracts_DoNotExposeDatabaseEntities()
    {
        var responseTypes = new[]
        {
            typeof(BookingProfessionalResponse),
            typeof(AvailableSlotResponse),
            typeof(AppointmentResponse),
        };

        Assert.All(responseTypes, responseType =>
            Assert.DoesNotContain(responseType.GetProperties(), property =>
                property.PropertyType.FullName?.Contains("Infrastructure.Persistence.Generated", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void Commands_RejectUnknownJsonMembersAndShortCancellationReason()
    {
        const string json = """
            {
              "doctorAccountId": 1,
              "appointmentTypeId": 1,
              "modalityCode": "online",
              "localDate": "2026-09-10",
              "localStartsAt": "10:00:00",
              "patientNotes": null,
              "priceAmount": 1
            }
            """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppointmentCreateRequest>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));

        var invalid = new AppointmentCancelRequest("não", 1);
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(invalid, new ValidationContext(invalid), results, true));
    }
}
