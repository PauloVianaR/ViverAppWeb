using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientExperience;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.PatientScheduling.Tests;

// Persiste exclusivamente dados identificados desta fixture no MySQL local real.
public sealed class PatientExperienceTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Delivery:Enabled"] = "false",
            ["PagBank:Enabled"] = "false",
            ["Storage:Private:Provider"] = "Database",
            ["Logging:LogLevel:Default"] = "None",
        }));
    });
    private const string Password = "Patient-Fixture#2026!";
    private ulong patient, other, doctor, appointment;
    private uint type, premiumPlan;
    private string email = "";
    private readonly string marker = Guid.NewGuid().ToString("N");

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        await db.Database.OpenConnectionAsync();
        Assert.Equal("viverappweb", db.Database.GetDbConnection().Database);
        Assert.StartsWith("8.0.41", db.Database.GetDbConnection().ServerVersion);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ViverAppUser>>();
        async Task<ulong> Account(string role, string suffix)
        {
            var address = $"experience-{suffix}-{marker}@example.test";
            var user = new ViverAppUser
            {
                FullName = $"Paciente Teste {suffix}",
                Email = address,
                UserName = address,
                RoleCode = role,
                StatusCode = "active",
                EmailConfirmed = true
            };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            if (suffix == "owner") email = address;
            return user.Id;
        }
        patient = await Account("patient", "owner"); other = await Account("patient", "other"); doctor = await Account("doctor", "doctor");
        var now = DateTime.UtcNow;
        db.DoctorProfiles.Add(new()
        {
            AccountId = doctor,
            LicenseStateCode = "SP",
            LicenseNumber = marker[..12],
            DefaultAppointmentDurationMinutes = 30,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        });
        var service = new AppointmentType
        {
            Name = $"Experiência {marker}",
            CategoryCode = "examination",
            ModalityCode = "both",
            DurationMinutes = 30,
            PriceAmount = 123.45m,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        };
        db.AppointmentTypes.Add(service); await db.SaveChangesAsync(); type = service.Id;
        var visit = new Appointment
        {
            AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63),
            PatientAccountId = patient,
            DoctorAccountId = doctor,
            AppointmentTypeId = type,
            CreatedByAccountId = patient,
            StatusCode = "pending",
            ModalityCode = "in_person",
            StartsAtUtc = now.AddDays(7),
            EndsAtUtc = now.AddDays(7).AddMinutes(30),
            PriceAmount = 123.45m,
            CurrencyCode = "BRL",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        };
        db.Appointments.Add(visit); await db.SaveChangesAsync(); appointment = visit.Id;
    }

    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        var ids = new[] { patient, other, doctor };
        await db.ContactChangeRequests.Where(x => ids.Contains(x.AccountId)).ExecuteDeleteAsync();
        await db.PatientPreferences.Where(x => ids.Contains(x.AccountId)).ExecuteDeleteAsync();
        await db.PremiumMemberships.Where(x => ids.Contains(x.AccountId)).ExecuteDeleteAsync();
        if (premiumPlan != 0) await db.PremiumPlans.Where(x => x.Id == premiumPlan).ExecuteDeleteAsync();
        await db.AppointmentDocuments.Where(x => x.AppointmentId == appointment).ExecuteDeleteAsync();
        await db.MedicalReports.Where(x => x.AppointmentId == appointment).ExecuteDeleteAsync();
        await db.PrivateDocuments.Where(x => ids.Contains(x.OwnerAccountId)).ExecuteDeleteAsync();
        await db.AppointmentReviews.Where(x => x.AppointmentId == appointment).ExecuteDeleteAsync();
        await db.Appointments.Where(x => x.Id == appointment).ExecuteUpdateAsync(update => update.SetProperty(x => x.CurrentPaymentId, (ulong?)null));
        await db.Payments.Where(x => x.AppointmentId == appointment).ExecuteDeleteAsync();
        await db.AppointmentStatusHistories.Where(x => x.AppointmentId == appointment).ExecuteDeleteAsync();
        await db.Appointments.Where(x => x.Id == appointment).ExecuteDeleteAsync();
        await db.DoctorProfiles.Where(x => x.AccountId == doctor).ExecuteDeleteAsync();
        await db.AppointmentTypes.Where(x => x.Id == type).ExecuteDeleteAsync();
        await db.OutboxMessages.Where(x => x.Recipient.Contains(marker)).ExecuteDeleteAsync();
        await db.Accounts.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Own_agenda_clinic_payment_review_and_inclusive_date_filters()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PatientExperienceService>();
        var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        var item = await service.AppointmentAsync(patient, appointment, default);
        Assert.True(item.CanChooseClinic);
        Assert.Equal(404, (await Assert.ThrowsAsync<PatientExperienceException>(() => service.AppointmentAsync(other, appointment, default))).StatusCode);
        var date = item.Appointment.LocalDate.ToDateTime(TimeOnly.MinValue);
        Assert.Single((await service.AgendaAsync(patient, 1, 12, "future", null, null, date, date, null, null, null, default)).Items);
        Assert.Single((await service.AgendaAsync(patient, 1, 12, "future", null, item.Appointment.AppointmentNumber, date, date, null, null, null, default)).Items);
        Assert.Empty((await service.AgendaAsync(patient, 1, 12, "future", null, item.Appointment.AppointmentNumber + 1, date, date, null, null, null, default)).Items);
        Assert.Empty((await service.AgendaAsync(other, 1, 12, "all", null, null, null, null, null, null, null, default)).Items);
        await service.ChooseClinicPaymentAsync(patient, appointment, default);
        Assert.Equal("clinic", (await service.AppointmentAsync(patient, appointment, default)).PaymentLocation);
        Assert.Empty(await db.Payments.Where(x => x.AppointmentId == appointment).ToArrayAsync());
        await Assert.ThrowsAsync<PatientExperienceException>(() => service.ReviewAsync(patient, appointment, new(5, null), default));
        await db.Appointments.Where(x => x.Id == appointment).ExecuteUpdateAsync(s => s.SetProperty(x => x.StatusCode, "completed").SetProperty(x => x.CompletedAtUtc, DateTime.UtcNow).SetProperty(x => x.CompletedByAccountId, doctor));
        db.ChangeTracker.Clear();
        await service.ReviewAsync(patient, appointment, new(5, "Atendimento cuidadoso"), default);
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<PatientExperienceException>(() => service.ReviewAsync(patient, appointment, new(1, null), default));
        Assert.Single(await db.AppointmentReviews.Where(x => x.AppointmentId == appointment).ToArrayAsync());
    }

    [Fact]
    public async Task Premium_document_is_encrypted_owned_and_cooldown_enforced()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ViverAppDbContext>();
        var service = services.GetRequiredService<PatientExperienceService>();
        var store = new PrivateDocumentStore(db, services.GetRequiredService<IDataProtectionProvider>(), new TestScanner(true), services.GetRequiredService<IWebHostEnvironment>());
        var content = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n%%EOF");
        var document = await store.PrepareAsync(patient, File(content), default);
        Assert.NotEqual(content, document.ProtectedContent);
        var plan = (await service.PremiumAsync(patient, default)).Plans.First();
        await service.RequestPremiumAsync(patient, plan.Id, document, default);
        Assert.Equal("pending", (await service.PremiumAsync(patient, default)).StatusCode);
        Assert.Equal(content, (await store.DownloadAsync(patient, document.Id, default)).Content);
        Assert.Equal(404, (await Assert.ThrowsAsync<PatientExperienceException>(() => store.DownloadAsync(other, document.Id, default))).StatusCode);
        var member = await db.PremiumMemberships.SingleAsync(x => x.AccountId == patient);
        member.StatusCode = "rejected"; member.ReviewedAtUtc = DateTime.UtcNow; member.RejectionReason = "Comprovante ilegível"; await db.SaveChangesAsync();
        var rejected = await service.PremiumAsync(patient, default);
        Assert.False(rejected.CanRequest); Assert.Equal("Comprovante ilegível", rejected.RejectionReason);
        member.ReviewedAtUtc = DateTime.UtcNow.AddDays(-4); await db.SaveChangesAsync();
        Assert.True((await service.PremiumAsync(patient, default)).CanRequest);
        var next = await store.PrepareAsync(patient, File(content), default);
        await service.RequestPremiumAsync(patient, plan.Id, next, default);
        var pending = await service.PremiumAsync(patient, default);
        await service.CancelPremiumAsync(patient, pending.MembershipId!.Value, pending.RowVersion, default);
        Assert.Equal("canceled", (await service.PremiumAsync(patient, default)).StatusCode);
        var blocked = new PrivateDocumentStore(db, services.GetRequiredService<IDataProtectionProvider>(), new TestScanner(false), services.GetRequiredService<IWebHostEnvironment>());
        Assert.Equal(422, (await Assert.ThrowsAsync<PatientExperienceException>(() => blocked.PrepareAsync(patient, File(content), default))).StatusCode);
    }

    [Fact]
    public async Task R2_document_uses_an_opaque_key_and_no_public_download_url()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var storage = new TestPrivateObjectStorage();
        var store = new PrivateDocumentStore(
            services.GetRequiredService<ViverAppDbContext>(),
            services.GetRequiredService<IDataProtectionProvider>(),
            new TestScanner(true),
            services.GetRequiredService<IWebHostEnvironment>(),
            storage,
            Options.Create(new PrivateStorageOptions { Provider = "R2" }));
        var content = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n%%EOF");
        var document = await store.PrepareAsync(patient, File(content), default);

        Assert.Equal("r2", document.StorageProviderCode);
        Assert.Null(document.ProtectedContent);
        Assert.NotNull(document.ObjectKey);
        Assert.DoesNotContain(document.OriginalFileName, document.ObjectKey, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(patient.ToString(), document.ObjectKey, StringComparison.Ordinal);
        Assert.StartsWith("private/development/", document.ObjectKey, StringComparison.Ordinal);
        Assert.Equal(content, storage.Content);
        Assert.Throws<NotSupportedException>(() => storage.CreateShortLivedDownloadUri(document.ObjectKey));
    }

    [Fact]
    public void R2_keys_are_unique_and_do_not_embed_identity_or_file_names()
    {
        var first = R2PrivateObjectStorage.CreateOpaqueKey("Production", new DateTime(2026, 9, 8));
        var second = R2PrivateObjectStorage.CreateOpaqueKey("Production", new DateTime(2026, 9, 8));
        Assert.StartsWith("private/production/2026/09/", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("patient", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pdf", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Real_cookie_api_enforces_csrf_ownership_and_no_profile_mass_assignment()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Origin", "https://localhost:7110");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/patient/experience/profile")).StatusCode);
        await Csrf(client);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login/password", new { identifier = email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/patient/experience/home")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/patient/experience/appointments/{ulong.MaxValue}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/patient/experience/profile", new { patientId = other, roleCode = "administrator" })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/patient/experience/appointments/{appointment}/pay-at-clinic", new { })).StatusCode);
    }

    [Fact]
    public async Task Profile_preferences_persist_and_stale_updates_are_rejected()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PatientExperienceService>();
        var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        var before = await service.ProfileAsync(patient, default);
        // CPF sintético válido, exclusivo desta execução; não corresponde a uma pessoa consultada.
        var digits = Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(0, 10)).ToList();
        for (var count = 9; count <= 10; count++) { var sum = digits.Select((digit, index) => digit * (count + 1 - index)).Sum(); var check = 11 - sum % 11; digits.Add(check >= 10 ? 0 : check); }
        var cpf = string.Concat(digits);
        var request = new PatientProfileRequest("Paciente Teste Atualizado", cpf, new(1990, 1, 1), new("30140071", "Rua Teste", "10", null, "Centro", "Belo Horizonte", "MG"), false, true, before.RowVersion);
        await service.UpdateProfileAsync(patient, request, default);
        db.ChangeTracker.Clear();
        var after = await service.ProfileAsync(patient, default);
        Assert.Equal(cpf, after.TaxId); Assert.False(after.EmailEnabled); Assert.True(after.SmsEnabled);
        Assert.Equal("MG", after.Address!.StateCode); Assert.Equal(before.RowVersion + 1, after.RowVersion);
        Assert.Equal(409, (await Assert.ThrowsAsync<PatientExperienceException>(() => service.UpdateProfileAsync(patient, request, default))).StatusCode);
        Assert.NotEqual(after.FullName, (await service.ProfileAsync(other, default)).FullName);
    }

    [Fact]
    public async Task New_contact_requires_otp_and_revokes_the_old_session_after_confirmation()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        await Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login/password", new { identifier = email, password = Password })).StatusCode);
        await Csrf(client);
        var destination = $"updated-{marker}@example.test";
        var requestResponse = await client.PostAsJsonAsync("/api/v1/patient/account/contact/request", new { channel = "email", destination });
        Assert.Equal(HttpStatusCode.Accepted, requestResponse.StatusCode);
        var requestId = (await requestResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        Assert.Equal(email, await db.Accounts.Where(x => x.Id == patient).Select(x => x.Email).SingleAsync());
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.Recipient == destination.ToUpperInvariant());
        var protectedBytes = Convert.FromBase64String(JsonDocument.Parse(message.PayloadJson).RootElement.GetProperty("protectedPayload").GetString()!);
        var plain = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("ViverApp.Identity.OutboxMessage.v1").Unprotect(protectedBytes);
        var code = JsonDocument.Parse(plain).RootElement.GetProperty("code").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/patient/account/contact/confirm", new { requestId, code })).StatusCode);
        Assert.Equal(destination.ToUpperInvariant(), await db.Accounts.Where(x => x.Id == patient).Select(x => x.Email).SingleAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/patient/experience/profile")).StatusCode);
        Assert.All(await db.AuthSessions.Where(x => x.AccountId == patient).ToArrayAsync(), x => Assert.NotNull(x.RevokedAtUtc));
    }

    [Fact]
    public async Task Development_scanner_accepts_the_repository_logo_and_rejects_corrupted_png()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "ViverApp.Web", "wwwroot", "images", "logo.png"));
        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        Assert.Equal("image/png", PrivateDocumentStore.ValidateContent("logo.png", "image/png", bytes));
        var corrupted = bytes.ToArray(); corrupted[29] ^= 1;
        Assert.Throws<PatientExperienceException>(() => PrivateDocumentStore.ValidateContent("logo.png", "image/png", corrupted));
        var store = services.GetRequiredService<PrivateDocumentStore>();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "logo.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
        var document = await store.PrepareAsync(patient, file, default);
        Assert.Equal("available", document.StatusCode);
        Assert.Equal(patient, document.OwnerAccountId);
    }

    [Fact]
    public async Task Active_premium_discount_is_calculated_from_database_not_browser_input()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PatientExperienceService>();
        var now = DateTime.UtcNow;
        var plan = new PremiumPlan { Name = $"Plano Teste {marker}", AppointmentDiscountPercent = 10, PriceAmount = 0, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.PremiumPlans.Add(plan); await db.SaveChangesAsync(); premiumPlan = plan.Id;
        db.PremiumMemberships.Add(new() { AccountId = patient, PremiumPlanId = plan.Id, StatusCode = "active", StartsAtUtc = now.AddDays(-1), EndsAtUtc = now.AddDays(1), CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 }); await db.SaveChangesAsync();
        var owner = await service.ServiceAsync(patient, type, default);
        Assert.Equal(10m, owner.DiscountPercent); Assert.Equal(111.10m, owner.PriceAmount);
        Assert.Equal(123.45m, (await service.ServiceAsync(other, type, default)).PriceAmount);
        Assert.True((await service.HomeAsync(patient, default)).IsPremium);
        await db.PremiumMemberships.Where(x => x.AccountId == patient).ExecuteUpdateAsync(s => s.SetProperty(x => x.EndsAtUtc, now.AddHours(-1)));
        Assert.Equal(0m, await service.DiscountAsync(patient, default));
    }

    [Fact]
    public async Task Clinical_document_requires_published_report_current_ownership_and_available_state()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ViverAppDbContext>();
        var store = new PrivateDocumentStore(db, services.GetRequiredService<IDataProtectionProvider>(), new TestScanner(true), services.GetRequiredService<IWebHostEnvironment>());
        var content = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n%%EOF");
        var document = await store.PrepareAsync(patient, File(content), default);
        db.PrivateDocuments.Add(document);
        var now = DateTime.UtcNow;
        db.AppointmentDocuments.Add(new() { AppointmentId = appointment, UploadedByAccountId = doctor, CategoryCode = "attachment", ObjectKey = document.Id.ToString("D"), OriginalFileName = document.OriginalFileName, ContentType = document.ContentType, SizeBytes = document.SizeBytes, Sha256 = document.Sha256, StatusCode = "available", CreatedAtUtc = now, AvailableAtUtc = now });
        var report = new MedicalReport { AppointmentId = appointment, AuthorDoctorAccountId = doctor, StatusCode = "draft", ClinicalSummary = "Conteúdo inteiramente sintético para teste.", CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 };
        db.MedicalReports.Add(report); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PatientExperienceException>(() => store.DownloadAsync(patient, document.Id, default));
        report.StatusCode = "published"; report.PublishedAtUtc = now; await db.SaveChangesAsync();
        Assert.Single((await store.ListAsync(patient, appointment, 1, 20, default)).Items);
        Assert.Empty((await store.ListAsync(patient, appointment, 2, 20, default)).Items);
        Assert.Equal(content, (await store.DownloadAsync(patient, document.Id, default)).Content);
        await Assert.ThrowsAsync<PatientExperienceException>(() => store.ListAsync(other, appointment, 1, 20, default));
        await Assert.ThrowsAsync<PatientExperienceException>(() => store.DownloadAsync(other, document.Id, default));
        document.StatusCode = "quarantined"; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PatientExperienceException>(() => store.DownloadAsync(patient, document.Id, default));
    }

    [Fact]
    public async Task Video_revalidates_ownership_window_payment_and_revoked_session()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<TeleconsultationAccess>();
        var session = Guid.NewGuid(); var now = DateTime.UtcNow;
        db.AuthSessions.Add(new()
        {
            Id = session.ToByteArray(),
            AccountId = patient,
            RefreshTokenHash = RandomNumberGenerator.GetBytes(32),
            AuthenticationMethod = "password",
            MfaSatisfied = true,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1)
        });
        await db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, patient.ToString()), new Claim(ClaimTypes.Role, "patient"), new Claim(ViverAppClaimTypes.SessionId, session.ToString()) }, "test"));
        await Assert.ThrowsAsync<HubException>(() => access.RequireAsync(principal, appointment, default));
        await db.Appointments.Where(x => x.Id == appointment).ExecuteUpdateAsync(s => s.SetProperty(x => x.ModalityCode, "online").SetProperty(x => x.StatusCode, "confirmed").SetProperty(x => x.StartsAtUtc, now).SetProperty(x => x.EndsAtUtc, now.AddMinutes(30)));
        await Assert.ThrowsAsync<HubException>(() => access.RequireAsync(principal, appointment, default));
        var paidPayment = new Payment { AppointmentId = appointment, ProviderReferenceAppointmentId = appointment, ProviderCode = "pagbank", StatusCode = "paid", Amount = 123.45m, CurrencyCode = "BRL", CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1 };
        db.Payments.Add(paidPayment); await db.SaveChangesAsync();
        await db.Appointments.Where(x => x.Id == appointment).ExecuteUpdateAsync(update => update.SetProperty(x => x.CurrentPaymentId, paidPayment.Id));
        Assert.Equal(appointment, (await access.RequireAsync(principal, appointment, default)).Id);
        await Assert.ThrowsAsync<HubException>(() => access.RequireAsync(principal, ulong.MaxValue, default));
        await db.AuthSessions.Where(x => x.AccountId == patient).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now));
        await Assert.ThrowsAsync<HubException>(() => access.RequireAsync(principal, appointment, default));
    }

    [Theory]
    [InlineData("../proof.pdf", "application/pdf", "%PDF-1.7\n%%EOF")]
    [InlineData("proof.png", "image/png", "%PDF-1.7\n%%EOF")]
    [InlineData("proof.pdf", "text/html", "%PDF-1.7\n%%EOF")]
    [InlineData("proof.pdf", "application/pdf", "%PDF-1.7\n/JavaScript (alert(1))\n%%EOF")]
    [InlineData("proof.pdf", "application/pdf", "%PDF-1.7\n%%EOF<script>bad</script>")]
    public void Upload_rejects_traversal_mime_and_active_content(string name, string mime, string body) =>
        Assert.Throws<PatientExperienceException>(() => PrivateDocumentStore.ValidateContent(name, mime, Encoding.ASCII.GetBytes(body)));

    private static FormFile File(byte[] bytes) => new(new MemoryStream(bytes), 0, bytes.Length, "file", "comprovante.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    private sealed class TestScanner(bool clean) : IDocumentMalwareScanner { public Task<bool> IsCleanAsync(byte[] content, CancellationToken ct) => Task.FromResult(clean); }
    private sealed class TestPrivateObjectStorage : IPrivateObjectStorage
    {
        public byte[]? Content { get; private set; }

        public Task<StoredPrivateObject> PutAsync(byte[] content, string contentType, byte[] sha256, CancellationToken ct)
        {
            Content = content.ToArray();
            return Task.FromResult(new StoredPrivateObject(
                R2PrivateObjectStorage.CreateOpaqueKey("Development", DateTime.UtcNow),
                "test-etag"));
        }

        public Task<byte[]> GetAsync(string key, int expectedBytes, CancellationToken ct) =>
            Task.FromResult(Content?.ToArray() ?? []);

        public Task<bool> ExistsAsync(string key, byte[] expectedSha256, CancellationToken ct) =>
            Task.FromResult(Content is not null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Content), expectedSha256));

        public Uri CreateShortLivedDownloadUri(string key) =>
            throw new NotSupportedException("A API mediada é obrigatória nestes testes.");
    }
    private static async Task Csrf(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/antiforgery");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("requestToken").GetString());
    }
}
