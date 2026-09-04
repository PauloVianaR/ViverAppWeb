using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.Payments;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.Payments.Tests;

public sealed class PaymentIntegrationTests
{
    [Fact]
    public async Task Checkout_uses_database_amount_and_webhooks_are_idempotent_and_ordered()
    {
        var configuration = LoadConfiguration();
        await DeleteStaleFixturesAsync(configuration);
        var fixture = await CreateFixtureAsync(configuration);
        try
        {
            var client = new RecordingPagBankClient(fixture.UtcNow.UtcDateTime);
            await using (var database = CreateContext(configuration))
            {
                var service = CreateService(database, client, fixture.UtcNow);
                var result = await service.CreateCheckoutAsync(
                    fixture.PatientId,
                    fixture.AppointmentId,
                    $"payment-{Guid.NewGuid():N}",
                    CancellationToken.None);

                Assert.False(result.Replayed);
                Assert.Equal(12_345, client.LastCheckout!.UnitAmount);
                Assert.Equal(123.45m, result.Payment.Amount);
            }

            var paidPayload = Encoding.UTF8.GetBytes(
                $"{{\"id\":\"CHAR_{fixture.Marker}\",\"reference_id\":\"appointment-{fixture.AppointmentId}\",\"status\":\"PAID\",\"created_at\":\"2026-09-03T12:01:00Z\",\"amount\":{{\"value\":12345,\"summary\":{{\"total\":12345,\"paid\":12345,\"refunded\":0}}}}}}");
            await using (var database = CreateContext(configuration))
            {
                var service = CreateService(database, client, fixture.UtcNow.AddMinutes(2));
                var first = await service.ProcessWebhookAsync(paidPayload, Sign(paidPayload), CancellationToken.None);
                var replay = await service.ProcessWebhookAsync(paidPayload, Sign(paidPayload), CancellationToken.None);
                Assert.Equal("applied", first.ResultCode);
                Assert.True(replay.Replayed);
            }

            var oldWaitingPayload = Encoding.UTF8.GetBytes(
                $"{{\"id\":\"CHAR_OLD{fixture.Marker}\",\"reference_id\":\"appointment-{fixture.AppointmentId}\",\"status\":\"WAITING\",\"created_at\":\"2026-09-03T11:55:00Z\",\"amount\":{{\"value\":12345,\"summary\":{{\"total\":12345,\"paid\":0,\"refunded\":0}}}}}}");
            await using (var database = CreateContext(configuration))
            {
                var service = CreateService(database, client, fixture.UtcNow.AddMinutes(3));
                var stale = await service.ProcessWebhookAsync(oldWaitingPayload, Sign(oldWaitingPayload), CancellationToken.None);
                Assert.Equal("terminal_paid", stale.ResultCode);
            }

            await using (var verification = CreateContext(configuration))
            {
                var payment = await verification.Payments.AsNoTracking().SingleAsync(item => item.AppointmentId == fixture.AppointmentId);
                var appointment = await verification.Appointments.AsNoTracking().SingleAsync(item => item.Id == fixture.AppointmentId);
                Assert.Equal("paid", payment.StatusCode);
                Assert.Equal("confirmed", appointment.StatusCode);
                Assert.Equal(2, await verification.PaymentWebhookReceipts.CountAsync(item => item.ProviderResourceId!.Contains(fixture.Marker)));
                Assert.Equal(3, await verification.PaymentEvents.CountAsync(item => item.PaymentId == payment.Id));
                Assert.Equal(1, await verification.AppointmentStatusHistories.CountAsync(
                    item => item.AppointmentId == appointment.Id && item.ToStatusCode == "confirmed" && item.ActorAccountId == null));
            }
        }
        finally
        {
            await DeleteFixtureAsync(configuration, fixture);
        }
    }

    [Fact]
    public async Task Forged_webhook_does_not_create_financial_records()
    {
        var configuration = LoadConfiguration();
        await using var database = CreateContext(configuration);
        var before = await database.PaymentWebhookReceipts.CountAsync();
        var service = CreateService(database, new RecordingPagBankClient(DateTime.UtcNow), DateTimeOffset.UtcNow);
        var payload = Encoding.UTF8.GetBytes("{\"id\":\"CHAR_FORGED\",\"status\":\"PAID\"}");

        var exception = await Assert.ThrowsAsync<PaymentRuleException>(() =>
            service.ProcessWebhookAsync(payload, new string('0', 64), CancellationToken.None));

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal(before, await database.PaymentWebhookReceipts.CountAsync());
    }

    private static PaymentService CreateService(
        ViverAppDbContext database,
        IPagBankClient client,
        DateTimeOffset now) =>
        new(database, client, Options(), new NullAuditWriter(), new FixedTimeProvider(now));

    private static PagBankOptions Options() => new()
    {
        Enabled = true,
        Environment = "Sandbox",
        ProductionEnabled = false,
        RefundsEnabled = false,
        ApiBaseUrl = new Uri("https://sandbox.api.pagseguro.com/"),
        WebPublicBaseUrl = new Uri("https://example.test/"),
        ApiPublicBaseUrl = new Uri("https://api.example.test/"),
        Token = "integration-test-token",
        CheckoutLifetimeMinutes = 120,
        ReconciliationIntervalMinutes = 5,
        ReconciliationBatchSize = 20,
    };

    private static string Sign(byte[] payload) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"integration-test-token-{Encoding.UTF8.GetString(payload)}"))).ToLowerInvariant();

    private static async Task<Fixture> CreateFixtureAsync(IConfiguration configuration)
    {
        await using var database = CreateContext(configuration);
        var utcNow = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var now = utcNow.UtcDateTime;
        var marker = Guid.NewGuid().ToString("N")[..12];
        var patient = NewAccount(ViverAppRoles.Patient, $"Pagamento Paciente {marker}", $"payment-patient-{marker}@example.test", now);
        var doctor = NewAccount(ViverAppRoles.Doctor, $"Pagamento Médico {marker}", $"payment-doctor-{marker}@example.test", now);
        database.Accounts.AddRange(patient, doctor);
        await database.SaveChangesAsync();
        database.DoctorProfiles.Add(new DoctorProfile
        {
            AccountId = doctor.Id,
            LicenseStateCode = "SP",
            LicenseNumber = marker,
            DefaultAppointmentDurationMinutes = 30,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        });
        var type = new AppointmentType
        {
            Name = $"Consulta PagBank {marker}",
            Description = "Fixture exclusiva da fase 9.",
            ModalityCode = "online",
            DurationMinutes = 30,
            PriceAmount = 123.45m,
            IsActive = true,
            DisplayOrder = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.AppointmentTypes.Add(type);
        await database.SaveChangesAsync();
        var appointment = new Appointment
        {
            PatientAccountId = patient.Id,
            DoctorAccountId = doctor.Id,
            AppointmentTypeId = type.Id,
            CreatedByAccountId = patient.Id,
            StatusCode = "pending",
            ModalityCode = "online",
            StartsAtUtc = now.AddDays(7),
            EndsAtUtc = now.AddDays(7).AddMinutes(30),
            PriceAmount = 123.45m,
            CurrencyCode = "BRL",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Appointments.Add(appointment);
        await database.SaveChangesAsync();
        return new(marker, patient.Id, doctor.Id, type.Id, appointment.Id, utcNow);
    }

    private static Account NewAccount(string role, string name, string email, DateTime now) => new()
    {
        RoleCode = role,
        StatusCode = "active",
        FullName = name,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailVerified = true,
        PhoneVerified = false,
        SecurityStamp = RandomNumberGenerator.GetBytes(32),
        FailedLoginCount = 0,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        RowVersion = 1,
    };

    private static async Task DeleteFixtureAsync(IConfiguration configuration, Fixture fixture)
    {
        await using var database = CreateContext(configuration);
        var paymentIds = await database.Payments.Where(item => item.AppointmentId == fixture.AppointmentId).Select(item => item.Id).ToArrayAsync();
        var receiptIds = await database.PaymentEvents.Where(item => paymentIds.Contains(item.PaymentId) && item.WebhookReceiptId != null)
            .Select(item => item.WebhookReceiptId!.Value).ToArrayAsync();
        await database.PaymentEvents.Where(item => paymentIds.Contains(item.PaymentId)).ExecuteDeleteAsync();
        await database.PaymentWebhookReceipts.Where(item => receiptIds.Contains(item.Id)).ExecuteDeleteAsync();
        await database.Payments.Where(item => paymentIds.Contains(item.Id)).ExecuteDeleteAsync();
        await database.IdempotencyRecords.Where(item => item.ScopeCode == $"payment.checkout:{fixture.PatientId}").ExecuteDeleteAsync();
        await database.AppointmentStatusHistories.Where(item => item.AppointmentId == fixture.AppointmentId).ExecuteDeleteAsync();
        await database.Appointments.Where(item => item.Id == fixture.AppointmentId).ExecuteDeleteAsync();
        await database.DoctorProfiles.Where(item => item.AccountId == fixture.DoctorId).ExecuteDeleteAsync();
        await database.AppointmentTypes.Where(item => item.Id == fixture.AppointmentTypeId).ExecuteDeleteAsync();
        await database.Accounts.Where(item => item.Id == fixture.PatientId || item.Id == fixture.DoctorId).ExecuteDeleteAsync();
    }

    private static async Task DeleteStaleFixturesAsync(IConfiguration configuration)
    {
        await using var database = CreateContext(configuration);
        var accountIds = await database.Accounts
            .Where(item => item.Email != null && item.Email.StartsWith("payment-") && item.Email.EndsWith("@example.test"))
            .Select(item => item.Id).ToArrayAsync();
        if (accountIds.Length == 0)
        {
            return;
        }

        var appointments = await database.Appointments
            .Where(item => accountIds.Contains(item.PatientAccountId) || accountIds.Contains(item.DoctorAccountId))
            .Select(item => new { item.Id, item.PatientAccountId }).ToArrayAsync();
        var appointmentIds = appointments.Select(item => item.Id).ToArray();
        var paymentIds = await database.Payments.Where(item => appointmentIds.Contains(item.AppointmentId)).Select(item => item.Id).ToArrayAsync();
        var receiptIds = await database.PaymentEvents.Where(item => paymentIds.Contains(item.PaymentId) && item.WebhookReceiptId != null)
            .Select(item => item.WebhookReceiptId!.Value).ToArrayAsync();
        await database.PaymentEvents.Where(item => paymentIds.Contains(item.PaymentId)).ExecuteDeleteAsync();
        await database.PaymentWebhookReceipts.Where(item => receiptIds.Contains(item.Id)).ExecuteDeleteAsync();
        await database.Payments.Where(item => paymentIds.Contains(item.Id)).ExecuteDeleteAsync();
        var scopes = appointments.Select(item => $"payment.checkout:{item.PatientAccountId}").ToArray();
        await database.IdempotencyRecords.Where(item => scopes.Contains(item.ScopeCode)).ExecuteDeleteAsync();
        await database.AppointmentStatusHistories.Where(item => appointmentIds.Contains(item.AppointmentId)).ExecuteDeleteAsync();
        await database.Appointments.Where(item => appointmentIds.Contains(item.Id)).ExecuteDeleteAsync();
        await database.DoctorProfiles.Where(item => accountIds.Contains(item.AccountId)).ExecuteDeleteAsync();
        await database.AppointmentTypes.Where(item => item.Description == "Fixture exclusiva da fase 9.").ExecuteDeleteAsync();
        await database.Accounts.Where(item => accountIds.Contains(item.Id)).ExecuteDeleteAsync();
    }

    private static IConfiguration LoadConfiguration() => new ConfigurationBuilder()
        .AddUserSecrets(typeof(PaymentIntegrationTests).Assembly, optional: false)
        .Build();

    private static ViverAppDbContext CreateContext(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("LocalConnection não configurada para os testes.");
        var builder = new MySqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.Database, "viverappweb", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("O teste recusou um database diferente de viverappweb.");
        }

        return new ViverAppDbContext(new DbContextOptionsBuilder<ViverAppDbContext>().UseMySQL(builder.ConnectionString).Options);
    }

    private sealed record Fixture(string Marker, ulong PatientId, ulong DoctorId, uint AppointmentTypeId, ulong AppointmentId, DateTimeOffset UtcNow);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class NullAuditWriter : IPaymentAuditWriter
    {
        public Task WriteAsync(string eventCode, ulong? actorAccountId, ulong paymentId, IReadOnlyDictionary<string, string>? safeData, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingPagBankClient(DateTime now) : IPagBankClient
    {
        public PagBankCheckoutCommand? LastCheckout { get; private set; }

        public Task<PagBankResource> CreateCheckoutAsync(PagBankCheckoutCommand command, string idempotencyKey, CancellationToken cancellationToken)
        {
            LastCheckout = command;
            return Task.FromResult(new PagBankResource(
                $"CHEC_{Guid.NewGuid():N}", command.ReferenceId, "ACTIVE",
                new Uri("https://pagamento.pagseguro.uol.com.br/pagamento?code=test"), now,
                command.ExpirationDate.UtcDateTime, command.UnitAmount, 0, "checkout"));
        }

        public Task<PagBankResource> GetCheckoutAsync(string checkoutId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<PagBankResource> InactivateCheckoutAsync(string checkoutId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<PagBankResource> RefundChargeAsync(string chargeId, long amountCents, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
