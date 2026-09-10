using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.CashManagement;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.ManagerExperience;
using ViverApp.Api.Features.Payments;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.ClinicalOperations.Tests;

public sealed class CashManagementIntegrationTests
{
    [Fact]
    public async Task ReversalReplacementClosingAndPrintPreserveTheFinancialChain()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<CashManagementIntegrationTests>().Build();
        await using var database = CreateContext(configuration);
        await using var transaction = await database.Database.BeginTransactionAsync();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        var manager = Account($"manager-{Guid.NewGuid():N}@phase17.example.test", ViverAppRoles.Manager, "Gestora Caixa");
        var administrator = Account($"admin-{Guid.NewGuid():N}@phase17.example.test", ViverAppRoles.Administrator, "Administradora Caixa");
        var doctor = Account($"doctor-{Guid.NewGuid():N}@phase17.example.test", ViverAppRoles.Doctor, "Dra. Caixa");
        var patient = Account($"patient-{Guid.NewGuid():N}@phase17.example.test", ViverAppRoles.Patient, "Paciente Caixa");
        database.Accounts.AddRange(manager, administrator, doctor, patient);
        await database.SaveChangesAsync();
        database.DoctorProfiles.Add(new DoctorProfile
        {
            AccountId = doctor.Id, ProfessionalTitle = "Dra.", LicenseStateCode = "MG", LicenseNumber = "170017",
            DefaultAppointmentDurationMinutes = 30, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1,
        });
        var appointmentType = new AppointmentType
        {
            Name = $"__phase17_{Guid.NewGuid():N}", CategoryCode = "consultation", ModalityCode = "in_person",
            DurationMinutes = 30, PriceAmount = 180m, IsActive = true, DisplayOrder = 999,
            CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1,
        };
        database.AppointmentTypes.Add(appointmentType);
        await database.SaveChangesAsync();
        var appointment = new Appointment
        {
            AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63),
            PatientAccountId = patient.Id, DoctorAccountId = doctor.Id, AppointmentTypeId = appointmentType.Id,
            CreatedByAccountId = manager.Id, StatusCode = "confirmed", ModalityCode = "in_person",
            StartsAtUtc = now.AddDays(1), EndsAtUtc = now.AddDays(1).AddMinutes(30), PriceAmount = 180m,
            BasePriceAmount = 180m, CurrencyCode = "BRL", PaymentLocationCode = "clinic",
            CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1,
        };
        database.Appointments.Add(appointment);
        await database.SaveChangesAsync();
        var original = new Payment
        {
            AppointmentId = appointment.Id, ProviderReferenceAppointmentId = appointment.Id, ProviderCode = "internal",
            StatusCode = "paid", Amount = 180m, CurrencyCode = "BRL", IdempotencyKey = Guid.NewGuid(), MethodCode = "cash",
            ConfirmedByAccountId = manager.Id, PaidAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1,
        };
        database.Payments.Add(original);
        await database.SaveChangesAsync();
        appointment.CurrentPaymentId = original.Id;

        var options = new PagBankOptions
        {
            Enabled = false, Environment = "Sandbox", ProductionEnabled = false, RefundsEnabled = false,
            ApiBaseUrl = new Uri("https://sandbox.api.pagseguro.com/"), WebPublicBaseUrl = new Uri("https://example.test/"),
            ApiPublicBaseUrl = new Uri("https://api.example.test/"), Token = string.Empty,
        };
        var clock = new FixedClock(now);
        var audit = new NoOpAuditWriter();
        var cash = new CashManagementService(database, new NoOpPagBank(), options, audit, clock);
        await cash.RecordPaymentReceivedAsync(original, manager.Id, now, CancellationToken.None);
        await database.SaveChangesAsync();
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now,
            TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")));

        var before = await cash.DayAsync(date, null, null, appointment.AppointmentNumber, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(180m, before.Summary.NetTotal);
        Assert.Single(before.Page.Items);

        var reversed = await cash.ReverseAsync(manager.Id, Guid.NewGuid().ToString("N"), original.Id,
            new PaymentReversalRequest(original.RowVersion, "Pagamento lançado na forma incorreta"), CancellationToken.None);
        Assert.Equal("reversed", reversed.PaymentStatusCode);
        Assert.True(reversed.CanCreateReplacementPayment);

        var managerService = new ManagerExperienceService(database, null!, null!, audit, clock, cash);
        var replacement = await managerService.ConfirmPaymentAsync(manager.Id, appointment.Id, Guid.NewGuid().ToString("N"),
            new ManagerPaymentConfirmRequest("pix", now, null, null, appointment.RowVersion), CancellationToken.None);
        Assert.NotEqual(original.Id, replacement.Id);
        Assert.Equal(original.Id, await database.Payments.Where(item => item.Id == replacement.Id)
            .Select(item => item.SupersedesPaymentId).SingleAsync());
        Assert.Equal(2, await database.Payments.CountAsync(item => item.AppointmentId == appointment.Id));
        Assert.Equal(1, await database.Payments.CountAsync(item => item.AppointmentId == appointment.Id && item.ActiveAppointmentId != null));

        var day = await cash.DayAsync(date, null, null, appointment.AppointmentNumber, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(360m, day.Summary.GrossEntries);
        Assert.Equal(180m, day.Summary.PaymentReversals);
        Assert.Equal(180m, day.Summary.NetTotal);
        Assert.Equal(3, day.Summary.MovementCount);
        Assert.Contains(day.Page.Items, item => item.TypeCode == "payment_reversal" && item.RelatedMovementId.HasValue);

        var fullPrint = await cash.PrintAsync(manager.Id, date, null, null, appointment.AppointmentNumber, null, null, false, CancellationToken.None);
        var totalsPrint = await cash.PrintAsync(manager.Id, date, null, null, appointment.AppointmentNumber, null, null, true, CancellationToken.None);
        Assert.Equal(3, fullPrint.Movements.Count);
        Assert.Empty(totalsPrint.Movements);
        Assert.Equal(fullPrint.Summary.NetTotal, totalsPrint.Summary.NetTotal);

        var lastMovement = day.Page.Items.Max(item => item.Id);
        var closure = await cash.CloseAsync(manager.Id, date, new CashCloseRequest(lastMovement), CancellationToken.None);
        Assert.Equal(180m, closure.Snapshot.NetTotal);
        var denied = await Assert.ThrowsAsync<CashRuleException>(() => cash.AddManualAsync(manager.Id, ViverAppRoles.Manager,
            Guid.NewGuid().ToString("N"), new CashManualMovementRequest("supply", "entry", "cash", 25m, null,
                "Troco adicional", "Abertura complementar após fechamento"), CancellationToken.None));
        Assert.Equal(403, denied.StatusCode);
        var postClose = await cash.AddManualAsync(administrator.Id, ViverAppRoles.Administrator, Guid.NewGuid().ToString("N"),
            new CashManualMovementRequest("supply", "entry", "cash", 25m, null, "Troco adicional",
                "Abertura complementar após fechamento"), CancellationToken.None);
        Assert.True(postClose.AfterClosure);

        await Assert.ThrowsAsync<MySqlException>(() => database.CashMovements
            .Where(item => item.Id == postClose.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Amount, 30m)));
        await transaction.RollbackAsync();
    }

    private static ViverAppDbContext CreateContext(IConfiguration configuration)
    {
        var value = configuration.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("LocalConnection ausente.");
        var builder = new MySqlConnectionStringBuilder(value);
        if (builder.Database != "viverappweb") throw new InvalidOperationException("Database recusado.");
        return new(new DbContextOptionsBuilder<ViverAppDbContext>().UseMySQL(builder.ConnectionString).Options);
    }

    private static Account Account(string email, string role, string name) => new()
    {
        RoleCode = role, StatusCode = "active", FullName = name, Email = email, NormalizedEmail = email.ToUpperInvariant(),
        EmailVerified = true, SecurityStamp = RandomNumberGenerator.GetBytes(32), CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow, RowVersion = 1,
    };

    private sealed class NoOpAuditWriter : IClinicalOperationsAuditWriter
    {
        public Task WriteAsync(string eventCode, ulong actorAccountId, string entityType, string entityId,
            IReadOnlyDictionary<string, string>? safeData, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value, TimeSpan.Zero);
    }

    private sealed class NoOpPagBank : IPagBankClient
    {
        public Task<PagBankResource> CreateCheckoutAsync(PagBankCheckoutCommand command, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagBankResource> GetCheckoutAsync(string checkoutId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagBankResource> InactivateCheckoutAsync(string checkoutId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagBankResource> RefundChargeAsync(string chargeId, long amountCents, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
