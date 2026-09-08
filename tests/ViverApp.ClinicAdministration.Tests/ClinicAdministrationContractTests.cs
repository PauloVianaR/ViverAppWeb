using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.ClinicAdministration;
using ViverApp.Api.Features.AdministratorExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.ClinicAdministration.Tests;

public sealed class ClinicAdministrationContractTests : IAsyncLifetime
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
    [InlineData("/api/v1/clinic")]
    [InlineData("/api/v1/catalog/specialties")]
    [InlineData("/api/v1/catalog/appointment-types")]
    [InlineData("/api/v1/professionals")]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/administrator/home")]
    [InlineData("/api/v1/administrator/analytics?from=2026-01-01&to=2026-01-31")]
    [InlineData("/api/v1/administrator/notifications")]
    public async Task MasterDataQueries_RequireAuthentication(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void AdministratorContracts_RejectUnknownMembersAndInvalidDecisions()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdministratorPremiumCancelRequest>("{\"reason\":\"motivo válido\",\"rowVersion\":1,\"role\":\"administrator\"}", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
        var decision = typeof(AdministratorAccountStatusRequest).GetConstructors().Single().GetParameters().Single(x => x.Name == "DecisionCode");
        Assert.Equal("^(blocked|reactivated)$", decision.GetCustomAttributes(typeof(RegularExpressionAttribute), true).Cast<RegularExpressionAttribute>().Single().Pattern);
    }

    [Fact]
    public void CriticalAdministrationControllers_ApplyStepUpFilter()
    {
        var protectedControllers = new[] { typeof(AdministratorExperienceController), typeof(UsersController), typeof(ProfessionalsController), typeof(ClinicConfigurationController), typeof(CatalogController) };
        foreach (var controller in protectedControllers)
        {
            Assert.Contains(controller.GetCustomAttributes(typeof(ServiceFilterAttribute), true).Cast<ServiceFilterAttribute>(),
                attribute => attribute.ServiceType == typeof(AdministratorStepUpFilter));
        }
    }

    [Fact]
    public async Task AdministratorExperience_ExecutesServerAggregationsAndProtectsCurrentAdministrator()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<AdministratorExperienceService>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var administratorEmail = $"phase14-{Guid.NewGuid():N}@example.test";
        var administrator = new Account
        {
            RoleCode = ViverAppRoles.Administrator,
            StatusCode = "active",
            FullName = "Administrador sintético da Fase 14",
            Email = administratorEmail,
            NormalizedEmail = administratorEmail.ToUpperInvariant(),
            EmailVerified = true,
            PreferredRecoveryChannel = "email",
            SecurityStamp = RandomNumberGenerator.GetBytes(32),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Accounts.Add(administrator);
        await database.SaveChangesAsync();

        var patient = new Account
        {
            RoleCode = ViverAppRoles.Patient,
            StatusCode = "active",
            FullName = "Paciente sintético do Analytics",
            Email = $"phase14-patient-{Guid.NewGuid():N}@example.test",
            EmailVerified = true,
            SecurityStamp = RandomNumberGenerator.GetBytes(32),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        patient.NormalizedEmail = patient.Email.ToUpperInvariant();
        var doctor = new Account
        {
            RoleCode = ViverAppRoles.Doctor,
            StatusCode = "active",
            FullName = "Médica sintética do Analytics",
            Email = $"phase14-doctor-{Guid.NewGuid():N}@example.test",
            EmailVerified = true,
            SecurityStamp = RandomNumberGenerator.GetBytes(32),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        doctor.NormalizedEmail = doctor.Email.ToUpperInvariant();
        database.Accounts.AddRange(patient, doctor);
        await database.SaveChangesAsync();
        database.DoctorProfiles.Add(new DoctorProfile
        {
            AccountId = doctor.Id,
            ProfessionalTitle = "Dra.",
            LicenseStateCode = "SP",
            LicenseNumber = "149999",
            DefaultAppointmentDurationMinutes = 30,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        });
        var appointmentType = new AppointmentType
        {
            Name = $"Consulta Analytics {Guid.NewGuid():N}",
            CategoryCode = "consultation",
            ModalityCode = "in_person",
            DurationMinutes = 30,
            PriceAmount = 180,
            IsActive = true,
            DisplayOrder = 999,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.AppointmentTypes.Add(appointmentType);
        await database.SaveChangesAsync();
        var appointment = new Appointment
        {
            PatientAccountId = patient.Id,
            DoctorAccountId = doctor.Id,
            AppointmentTypeId = appointmentType.Id,
            CreatedByAccountId = administrator.Id,
            StatusCode = "confirmed",
            ModalityCode = "in_person",
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(-1).AddMinutes(30),
            PriceAmount = 180,
            BasePriceAmount = 180,
            DiscountPercent = 0,
            PaymentLocationCode = "clinic",
            CurrencyCode = "BRL",
            CreatedAtUtc = now.AddDays(-2),
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Appointments.Add(appointment);
        await database.SaveChangesAsync();
        database.Payments.Add(new Payment
        {
            AppointmentId = appointment.Id,
            ProviderCode = "internal",
            StatusCode = "paid",
            Amount = 180,
            CurrencyCode = "BRL",
            IdempotencyKey = Guid.NewGuid(),
            MethodCode = "cash",
            PaidAtUtc = now.AddHours(-1),
            CreatedAtUtc = now.AddDays(-1),
            UpdatedAtUtc = now,
            RowVersion = 1,
        });
        await database.SaveChangesAsync();

        var home = await service.HomeAsync(administrator.Id, CancellationToken.None);
        var analytics = await service.AnalyticsAsync(DateOnly.FromDateTime(now.AddMonths(-1)), DateOnly.FromDateTime(now), CancellationToken.None);
        var settings = await service.SettingsAsync(CancellationToken.None);

        Assert.True(home.Counters.ActiveUsers >= 1);
        Assert.True(analytics.Appointments >= 1);
        Assert.Contains(analytics.RevenueByUserType, item => item.Regular >= 180);
        Assert.Contains(analytics.RevenueByMonth, item => item.Value >= 180 && item.Count >= 1);
        Assert.Contains(analytics.PaymentsByTypeEvolution, item => item.Cash >= 180);
        Assert.Contains(analytics.PaymentsByMethod, item => item.Label == "cash" && item.Value >= 180);
        Assert.Contains(analytics.PaymentsByLocationTrend, item => item.InClinic >= 1);
        Assert.Contains(analytics.PaymentsByLocation, item => item.Label == "clinic" && item.Count >= 1);
        Assert.Contains(analytics.AppointmentsByService, item => item.Label == appointmentType.Name && item.Count >= 1);
        Assert.Contains(analytics.AppointmentsByCategory, item => item.Label == "consultation" && item.Count >= 1);
        Assert.Contains(analytics.DoctorPerformance, item => item.Label == doctor.FullName && item.Count >= 1);
        Assert.NotEmpty(settings);
        var blocked = await Assert.ThrowsAsync<AdministratorRuleException>(() => service.SetAccountStatusAsync(
            administrator.Id,
            administrator.Id,
            new AdministratorAccountStatusRequest("blocked", "Tentativa protegida do teste", administrator.RowVersion),
            CancellationToken.None));
        Assert.Equal(409, blocked.StatusCode);
        await transaction.RollbackAsync();
    }

    [Fact]
    public void Contracts_RejectUnknownJsonMembers()
    {
        const string json = """
            {
              "name": "Cardiologia",
              "isActive": true,
              "rowVersion": 0,
              "entity": { "id": 99 }
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SpecialtyWriteRequest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }));
    }

    [Fact]
    public void ClinicContract_RejectsPartialAddress()
    {
        var request = new ClinicUpdateRequest(
            "Viver Clínica Ltda",
            "Viver Clínica",
            "12345678000190",
            "contato@example.com",
            "+5511999999999",
            "01001000",
            null,
            null,
            null,
            null,
            null,
            null,
            "America/Sao_Paulo",
            0);
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(request, new ValidationContext(request), results, true);

        Assert.False(valid);
        Assert.Contains(results, result => result.ErrorMessage == "O endereço deve ser informado por completo.");
    }

    [Fact]
    public async Task ManagementPolicy_AllowsOnlyManagerAndAdministratorRoles()
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await provider.GetPolicyAsync(ViverAppPolicies.Management);

        var roles = Assert.Single(policy!.Requirements.OfType<RolesAuthorizationRequirement>()).AllowedRoles;
        Assert.Equal(
            new[] { ViverAppRoles.Administrator, ViverAppRoles.Manager },
            roles.OrderBy(role => role, StringComparer.Ordinal));
    }

    [Fact]
    public void PublicResponses_DoNotExposeEfEntities()
    {
        var responseTypes = new[]
        {
            typeof(ClinicResponse),
            typeof(SpecialtyResponse),
            typeof(AppointmentTypeResponse),
            typeof(HolidayResponse),
            typeof(ProfessionalResponse),
            typeof(UserResponse),
        };

        Assert.All(responseTypes, responseType =>
            Assert.DoesNotContain(responseType.GetProperties(), property =>
                property.PropertyType.FullName?.Contains("Infrastructure.Persistence.Generated", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task SpecialtyCrud_UsesRealMysqlConcurrencyAndAudit()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ClinicAdministrationContractTests).Assembly, optional: false)
            .Build();
        await using var database = CreateContext(configuration);
        await database.Database.OpenConnectionAsync();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var actor = new Account
        {
            RoleCode = ViverAppRoles.Administrator,
            StatusCode = "active",
            FullName = "Administrador de teste",
            Email = $"admin-{Guid.NewGuid():N}@example.test",
            NormalizedEmail = $"ADMIN-{Guid.NewGuid():N}@EXAMPLE.TEST",
            EmailVerified = true,
            PhoneVerified = false,
            SecurityStamp = RandomNumberGenerator.GetBytes(32),
            FailedLoginCount = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Accounts.Add(actor);
        await database.SaveChangesAsync();
        var httpContext = new DefaultHttpContext
        {
            User = CreateAdministratorPrincipal(actor.Id),
        };
        var audit = new IdentityAuditWriter(
            database,
            new HttpContextAccessor { HttpContext = httpContext },
            IdentitySecurityOptions.Load(configuration));
        var controller = new CatalogController(database, audit)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
        var uniqueName = $"Especialidade {Guid.NewGuid():N}";

        var createdAction = await controller.CreateSpecialty(
            new SpecialtyWriteRequest(uniqueName, true),
            CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(createdAction.Result);
        var createdBody = Assert.IsType<SpecialtyResponse>(created.Value);
        Assert.Equal(1UL, createdBody.RowVersion);

        var updatedAction = await controller.UpdateSpecialty(
            createdBody.Id,
            new SpecialtyWriteRequest(uniqueName + " atualizada", true, createdBody.RowVersion),
            CancellationToken.None);
        var updated = Assert.IsType<OkObjectResult>(updatedAction.Result);
        var updatedBody = Assert.IsType<SpecialtyResponse>(updated.Value);
        Assert.Equal(2UL, updatedBody.RowVersion);

        var deleted = await controller.DeactivateSpecialty(
            createdBody.Id,
            updatedBody.RowVersion,
            CancellationToken.None);
        Assert.IsType<NoContentResult>(deleted);
        Assert.Contains(
            await database.AuditEvents.AsNoTracking()
                .Where(item => item.EntityType == "specialty" && item.EntityId == createdBody.Id.ToString())
                .Select(item => item.EventCode)
                .ToListAsync(),
            eventCode => eventCode == "catalog.specialty.deactivated");

        await transaction.RollbackAsync();
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    private static ViverAppDbContext CreateContext(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("LocalConnection não configurada para os testes.");
        var builder = new MySqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.Database, "viverappweb", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("O teste recusou um database diferente de viverappweb.");
        }

        return new ViverAppDbContext(new DbContextOptionsBuilder<ViverAppDbContext>()
            .UseMySQL(builder.ConnectionString)
            .Options);
    }

    private static ClaimsPrincipal CreateAdministratorPrincipal(ulong accountId)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, accountId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Role, ViverAppRoles.Administrator),
                new Claim(ViverAppClaimTypes.MfaSatisfied, bool.TrueString),
            ],
            "Test");
        return new ClaimsPrincipal(identity);
    }
}
