using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ViverApp.Api.Features.AdministratorExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.ClinicAdministration.Tests;

public sealed class AdministratorAccountIntegrationTests
{
    private static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Authentication:Delivery:Enabled"] = "false", ["Storage:Private:Provider"] = "Database", ["Authentication:WebReturnUrl"] = "https://localhost:7110/acesso" }));
        builder.ConfigureTestServices(s =>
        {
            foreach (var hosted in s.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType?.Assembly == typeof(Program).Assembly).ToArray()) s.Remove(hosted);
        });
    });

    private static Account Actor(string role = "administrator")
    {
        var email = $"qa35-{Guid.NewGuid():N}@example.test";
        return new Account
        {
            FullName = "QA35 Administrador sintético",
            RoleCode = role,
            StatusCode = "active",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailVerified = true,
            PortalAccessEnabled = true,
            SecurityStamp = RandomNumberGenerator.GetBytes(32),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = 1
        };
    }

    private static string Cpf()
    {
        var digits = Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(0, 10)).ToList();
        for (var length = 9; length <= 10; length++)
        {
            var sum = digits.Select((d, index) => d * (length + 1 - index)).Sum() % 11;
            digits.Add(sum < 2 ? 0 : 11 - sum);
        }
        return string.Concat(digits);
    }

    private static async Task<AdministratorAccountCreateRequest> Request(ViverAppDbContext db, string role)
    {
        var spec = await db.Specialties.Where(s => s.IsActive).Select(s => s.Id).FirstAsync();
        var id = Guid.NewGuid().ToString("N");
        return new(role, new("QA35 Pessoa " + id, "qa35-" + id + "@example.test", "319" + RandomNumberGenerator.GetInt32(10000000, 99999999),
            Cpf(), new DateOnly(1990, 5, 10),
            role == "patient" ? new("Nome preferido", "Observação sintética", new("39900000", null, null, null, null, "Almenara", "MG")) : null,
            role is "doctor" or "psychologist" ? new(new(role == "doctor" ? "Dra." : "Psicóloga", "MG", "QA35" + id[..10], 3, spec)) : null));
    }

    [Fact]
    public async Task PasswordResetCodeIsBoundToTheCurrentContactEvenWhenAnotherEditorChangedIt()
    {
        await using var factory = Factory(); await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>(); await using var tx = await db.Database.BeginTransactionAsync();
        var account = Actor("patient"); db.Accounts.Add(account); await db.SaveChangesAsync();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ViverAppUser>>().FindByIdAsync(account.Id.ToString());
        var challenges = scope.ServiceProvider.GetRequiredService<IdentityChallengeService>();
        var requestId = await challenges.CreateAsync(user!, "password_reset", "email", account.NormalizedEmail!, default);
        var message = await db.OutboxMessages.SingleAsync(m => m.AccountId == account.Id);
        using var envelope = JsonDocument.Parse(message.PayloadJson);
        var decrypted = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("ViverApp.Identity.OutboxMessage.v1")
            .Unprotect(Convert.FromBase64String(envelope.RootElement.GetProperty("protectedPayload").GetString()!));
        using var payload = JsonDocument.Parse(decrypted);
        var code = payload.RootElement.GetProperty("code").GetString()!;
        await db.Accounts.Where(a => a.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.NormalizedEmail, "ALTERADO-" + account.NormalizedEmail));
        Assert.False((await challenges.VerifyAsync(requestId, "password_reset", code, default)).Succeeded);
        await tx.RollbackAsync();
    }

    [Theory]
    [InlineData("patient")]
    [InlineData("doctor")]
    [InlineData("psychologist")]
    [InlineData("manager")]
    [InlineData("administrator")]
    public async Task CreatesActiveWithoutPasswordAndEditsEachRoleWithoutChangingSecurityOrApproval(string role)
    {
        await using var factory = Factory(); await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        await db.Database.OpenConnectionAsync(); Assert.StartsWith("8.0.41", db.Database.GetDbConnection().ServerVersion);
        Assert.Equal("viverappweb", db.Database.GetDbConnection().Database);
        await using var tx = await db.Database.BeginTransactionAsync();
        var actor = Actor(); db.Accounts.Add(actor); await db.SaveChangesAsync();
        await db.ApplicationSettings.Where(s => s.SettingKey == AdministratorAccountService.WelcomeEmailKey).ExecuteUpdateAsync(s => s.SetProperty(x => x.ValueJson, "true"));
        var service = scope.ServiceProvider.GetRequiredService<AdministratorAccountService>();
        var request = await Request(db, role);
        var created = await service.CreateAsync(actor.Id, request, default);
        Assert.Equal("active", created.Account.StatusCode); Assert.Equal(role, created.Account.RoleCode); Assert.True(created.WelcomeEmailQueued);
        var account = await db.Accounts.SingleAsync(a => a.Id == created.Account.Id);
        Assert.Null(account.PasswordHash); Assert.True(account.PasswordSetupRequired); Assert.True(account.AdminEmailVerificationPending); Assert.False(account.EmailVerified);
        Assert.True(account.PortalAccessEnabled); Assert.Null(await db.AccountConsents.SingleOrDefaultAsync(c => c.AccountId == account.Id));
        var message = await db.OutboxMessages.SingleAsync(m => m.AccountId == account.Id);
        Assert.Equal("identity.administrator_registration", message.TemplateKey); Assert.Equal("email", message.ChannelCode);
        Assert.DoesNotContain(request.Data.FullName, message.PayloadJson);
        if (role is "doctor" or "psychologist")
        {
            Assert.Equal((ushort)10, account.ProfessionalProfile!.DefaultAppointmentDurationMinutes);
            Assert.Equal(role == "doctor" ? "CRM" : "CRP", account.ProfessionalProfile.LicenseTypeCode);
            Assert.Single(account.ProfessionalProfile.ProfessionalSpecialties);
            Assert.True(await db.ProfessionalPreferences.AnyAsync(p => p.ProfessionalAccountId == account.Id));
        }
        if (role is "doctor" or "psychologist" or "manager") Assert.True(await db.ProfessionalReviews.AnyAsync(p => p.ProfessionalAccountId == account.Id && p.DecisionCode == "approved"));
        if (role == "manager") Assert.True(await db.ManagerPreferences.AnyAsync(p => p.ManagerAccountId == account.Id));
        var editedData = created.Account.Data with { FullName = "QA35 Nome atualizado" };
        var updated = await service.UpdateAsync(actor.Id, account.Id, new(editedData, created.Account.RowVersion), default);
        Assert.Equal(editedData.FullName, updated.Data.FullName); Assert.Equal(role, updated.RoleCode); Assert.Equal("active", updated.StatusCode);
        Assert.Null(account.PasswordHash); Assert.Equal(2UL, updated.RowVersion);
        Assert.Single(await db.OutboxMessages.Where(m => m.AccountId == account.Id).ToArrayAsync());
        Assert.Equal(409, (await Assert.ThrowsAsync<AdministratorRuleException>(() => service.UpdateAsync(actor.Id, account.Id, new(editedData, 1), default))).StatusCode);
        Assert.True(await db.AuditEvents.AnyAsync(a => a.EventCode == "administrator.user.registration_updated" && a.ActorAccountId == actor.Id));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ValidatesRequiredFieldsRoleBoundariesDuplicatesAndDisablingWelcome()
    {
        await using var factory = Factory(); await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>(); await using var tx = await db.Database.BeginTransactionAsync();
        var admin = Actor(); var manager = Actor("manager"); db.Accounts.AddRange(admin, manager); await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<AdministratorAccountService>();
        var request = await Request(db, "doctor");
        Assert.Equal(403, (await Assert.ThrowsAsync<AdministratorRuleException>(() => service.CreateAsync(manager.Id, request, default))).StatusCode);
        foreach (var invalid in new[] { request with { Data = request.Data with { Professional = null } }, request with { Data = request.Data with { TaxId = null } }, request with { Data = request.Data with { Phone = null } }, request with { Data = request.Data with { BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-17) } } })
            Assert.Equal(400, (await Assert.ThrowsAsync<AdministratorRuleException>(() => service.CreateAsync(admin.Id, invalid, default))).StatusCode);
        var settings = scope.ServiceProvider.GetRequiredService<AdministratorExperienceService>();
        var setting = (await settings.SettingsAsync(default)).Single(s => s.Key == AdministratorAccountService.WelcomeEmailKey);
        Assert.Equal(400, (await Assert.ThrowsAsync<AdministratorRuleException>(() => settings.UpdateSettingAsync(admin.Id, setting.Key, new("\"false\"", setting.RowVersion), default))).StatusCode);
        await settings.UpdateSettingAsync(admin.Id, setting.Key, new("false", setting.RowVersion), default);
        var created = await service.CreateAsync(admin.Id, request, default);
        Assert.False(created.WelcomeEmailQueued); Assert.False(await db.OutboxMessages.AnyAsync(m => m.AccountId == created.Account.Id));
        Assert.Equal(409, (await Assert.ThrowsAsync<AdministratorRuleException>(() => service.CreateAsync(admin.Id, request, default))).StatusCode);
        var patient = await Request(db, "patient");
        var createdPatient = await service.CreateAsync(admin.Id, patient, default);
        Assert.Equal(409, (await Assert.ThrowsAsync<AdministratorRuleException>(() => service.UpdateAsync(admin.Id, createdPatient.Account.Id,
            new(createdPatient.Account.Data with { Email = request.Data.Email }, createdPatient.Account.RowVersion), default))).StatusCode);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ChangedContactsInvalidateSessionsAndCodesAndPreserveExistingPasswordRoleAndStatus()
    {
        await using var factory = Factory(); await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>(); await using var tx = await db.Database.BeginTransactionAsync();
        var admin = Actor(); db.Accounts.Add(admin); await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<AdministratorAccountService>();
        var target = await service.CreateAsync(admin.Id, await Request(db, "manager"), default);
        var account = await db.Accounts.SingleAsync(a => a.Id == target.Account.Id);
        account.PasswordHash = "existing-adaptive-hash"; account.PasswordSetupRequired = false; account.EmailVerified = true; account.AdminEmailVerificationPending = false; account.StatusCode = "blocked";
        db.AuthSessions.Add(new AuthSession { Id = Guid.NewGuid().ToByteArray(), AccountId = account.Id, RefreshTokenHash = RandomNumberGenerator.GetBytes(32), AuthenticationMethod = "password", MfaSatisfied = true, CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ViverAppUser>>().FindByIdAsync(account.Id.ToString());
        var challenge = await scope.ServiceProvider.GetRequiredService<IdentityChallengeService>().CreateAsync(user!, "password_reset", "email", account.NormalizedEmail!, default);
        var before = account.SecurityStamp.ToArray();
        var updated = await service.UpdateAsync(admin.Id, account.Id, new(target.Account.Data with { Email = "qa35-changed-" + Guid.NewGuid().ToString("N") + "@example.test" }, account.RowVersion), default);
        Assert.Equal("blocked", updated.StatusCode); Assert.Equal("manager", updated.RoleCode); Assert.Equal("existing-adaptive-hash", account.PasswordHash);
        Assert.False(account.EmailVerified); Assert.True(account.AdminEmailVerificationPending); Assert.NotEqual(before, account.SecurityStamp);
        Assert.False(await db.AuthSessions.AnyAsync(s => s.AccountId == account.Id && s.RevokedAtUtc == null));
        Assert.False(await db.AccountChallenges.AnyAsync(c => c.AccountId == account.Id && c.ConsumedAtUtc == null));
        var stale = await scope.ServiceProvider.GetRequiredService<IdentityChallengeService>().VerifyAsync(challenge, "password_reset", "000000", default);
        Assert.False(stale.Succeeded);
        account.PhoneVerified = true; await db.SaveChangesAsync();
        var smsOnly = await service.UpdateAsync(admin.Id, account.Id, new(updated.Data with { Email = null }, updated.RowVersion), default);
        Assert.Null(smsOnly.Data.Email); Assert.True(account.PhoneVerified); Assert.False(account.AdminEmailVerificationPending);
        Assert.Equal("sms", account.PreferredRecoveryChannel); Assert.Equal("existing-adaptive-hash", account.PasswordHash);
        await tx.RollbackAsync();
    }

    [Theory]
    [InlineData("psychologist")]
    [InlineData("administrator")]
    public async Task FirstPasswordUsesExistingRecoveryCodeAndDoesNotRequireAnotherApproval(string role)
    {
        await using var factory = Factory(); using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        var actor = Actor(); db.Accounts.Add(actor); await db.SaveChangesAsync(); ulong targetId = 0;
        try
        {
            var request = await Request(db, role);
            var created = await scope.ServiceProvider.GetRequiredService<AdministratorAccountService>().CreateAsync(actor.Id, request, default); targetId = created.Account.Id;
            using var csrf = await client.GetAsync("/api/v1/auth/antiforgery"); var csrfJson = await csrf.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrfJson.GetProperty("requestToken").GetString());
            await db.Accounts.Where(a => a.Id == targetId).ExecuteUpdateAsync(s => s.SetProperty(a => a.AdminEmailVerificationPending, false));
            using var unverified = await client.PostAsJsonAsync("/api/v1/auth/password/recovery/request", new { identifier = request.Data.Email, channel = "email" });
            Assert.Equal(HttpStatusCode.Accepted, unverified.StatusCode);
            Assert.False(await db.OutboxMessages.AnyAsync(m => m.AccountId == targetId && m.TemplateKey == "identity.password_reset"));
            await db.Accounts.Where(a => a.Id == targetId).ExecuteUpdateAsync(s => s.SetProperty(a => a.AdminEmailVerificationPending, true));
            using var sms = await client.PostAsJsonAsync("/api/v1/auth/password/recovery/request", new { identifier = request.Data.Phone, channel = "sms" });
            Assert.Equal(HttpStatusCode.Accepted, sms.StatusCode);
            Assert.False(await db.OutboxMessages.AnyAsync(m => m.AccountId == targetId && m.TemplateKey == "identity.password_reset"));
            using var requested = await client.PostAsJsonAsync("/api/v1/auth/password/recovery/request", new { identifier = request.Data.Email, channel = "email" });
            Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
            var accepted = await requested.Content.ReadFromJsonAsync<JsonElement>(); var requestId = accepted.GetProperty("requestId").GetGuid();
            var message = await db.OutboxMessages.AsNoTracking().Where(m => m.AccountId == targetId && m.TemplateKey == "identity.password_reset").SingleAsync();
            using var envelope = JsonDocument.Parse(message.PayloadJson);
            var payload = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("ViverApp.Identity.OutboxMessage.v1")
                .Unprotect(Convert.FromBase64String(envelope.RootElement.GetProperty("protectedPayload").GetString()!));
            using var content = JsonDocument.Parse(payload); var code = content.RootElement.GetProperty("code").GetString();
            using var wrong = await client.PostAsJsonAsync("/api/v1/auth/password/recovery/complete", new { requestId, code = "INVALIDO", newPassword = "Nova-QA35!2026" });
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            using var completed = await client.PostAsJsonAsync("/api/v1/auth/password/recovery/complete", new { requestId, code, newPassword = "Nova-QA35!2026" });
            Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
            db.ChangeTracker.Clear(); var user = await scope.ServiceProvider.GetRequiredService<UserManager<ViverAppUser>>().FindByIdAsync(targetId.ToString());
            Assert.NotNull(user); Assert.True(user.EmailConfirmed); Assert.False(user.PasswordSetupRequired); Assert.False(user.AdminEmailVerificationPending); Assert.Equal("active", user.StatusCode);
            Assert.True(await scope.ServiceProvider.GetRequiredService<UserManager<ViverAppUser>>().CheckPasswordAsync(user, "Nova-QA35!2026"));
            using var replay = await client.PostAsJsonAsync("/api/v1/auth/password/recovery/complete", new { requestId, code, newPassword = "Outra-QA35!2026" });
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login/password", new { identifier = request.Data.Email, password = "Nova-QA35!2026" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var authentication = await login.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(role == "administrator", authentication.GetProperty("requiresMfaEnrollment").GetBoolean());
            using var forbidden = await client.GetAsync($"/api/v1/administrator/accounts/{targetId}");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.ProfessionalReviews.Where(r => r.ReviewerAccountId == actor.Id).ExecuteDeleteAsync();
            if (targetId > 0) await db.OutboxMessages.Where(m => m.AccountId == targetId).ExecuteDeleteAsync();
            if (targetId > 0) await db.Accounts.Where(a => a.Id == targetId).ExecuteDeleteAsync();
            await db.Accounts.Where(a => a.Id == actor.Id).ExecuteDeleteAsync();
        }
    }
}
