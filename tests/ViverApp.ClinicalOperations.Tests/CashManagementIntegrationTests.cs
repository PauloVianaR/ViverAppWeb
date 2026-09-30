using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
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
    public void ManualMovementAmountRangeUsesInvariantLimitsUnderBrazilianCulture()
    {
        var amount = typeof(CashManualMovementRequest).GetConstructors().Single().GetParameters()
            .Single(parameter => parameter.Name == nameof(CashManualMovementRequest.Amount));
        var range = amount.GetCustomAttribute<RangeAttribute>()!;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Assert.True(range.IsValid(200m));
            Assert.False(range.IsValid(0m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public async Task ReversalReplacementClosingAndPrintPreserveTheFinancialChain()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<CashManagementIntegrationTests>().Build();
        await using var database = CreateContext(configuration);
        await using var transaction = await database.Database.BeginTransactionAsync();
        var now = new DateTime(2099, 6, 17, 15, 30, 0, DateTimeKind.Utc);
        var manager = Account($"manager-{Guid.NewGuid():N}@phase17.example.test", ViverAppRoles.Manager, "Gestora Caixa");
        var administrator = Account($"admin-{Guid.NewGuid():N}@phase17.example.test", ViverAppRoles.Administrator, "Administradora Caixa");
        var doctor = Account($"doctor-{Guid.NewGuid():N}@phase17.example.test", ViverAppRoles.Doctor, "Dra. Caixa");
        var patient = Account($"patient-{Guid.NewGuid():N}@phase17.example.test", ViverAppRoles.Patient, "Paciente Caixa");
        database.Accounts.AddRange(manager, administrator, doctor, patient);
        await database.SaveChangesAsync();
        database.ProfessionalProfiles.Add(new ProfessionalProfile
        {
            AccountId = doctor.Id,
            ProfessionalTitle = "Dra.",
            LicenseStateCode = "MG",
            LicenseTypeCode = "CRM",
            LicenseNumber = "170017",
            DefaultAppointmentDurationMinutes = 30,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        });
        var appointmentType = new AppointmentType
        {
            Name = $"__phase17_{Guid.NewGuid():N}",
            CategoryCode = "consultation",
            ModalityCode = "in_person",
            DurationMinutes = 30,
            PriceAmount = 180m,
            RequiresPayment = true,
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
            AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63),
            PatientAccountId = patient.Id,
            ProfessionalAccountId = doctor.Id,
            AppointmentTypeId = appointmentType.Id,
            CreatedByAccountId = manager.Id,
            StatusCode = "confirmed",
            ModalityCode = "in_person",
            StartsAtUtc = now.AddDays(1),
            EndsAtUtc = now.AddDays(1).AddMinutes(30),
            PriceAmount = 180m,
            BasePriceAmount = 180m,
            RequiresPayment = true,
            CurrencyCode = "BRL",
            PaymentLocationCode = "clinic",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Appointments.Add(appointment);
        await database.SaveChangesAsync();
        var original = new Payment
        {
            AppointmentId = appointment.Id,
            AppointmentRequiresPayment = true,
            ProviderReferenceAppointmentId = appointment.Id,
            ProviderCode = "internal",
            StatusCode = "paid",
            Amount = 180m,
            CurrencyCode = "BRL",
            IdempotencyKey = Guid.NewGuid(),
            MethodCode = "credit_card",
            CardLastFour = "4242",
            AuthorizationReference = "AUTH-PHASE17",
            ConfirmedByAccountId = manager.Id,
            PaidAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Payments.Add(original);
        await database.SaveChangesAsync();
        appointment.CurrentPaymentId = original.Id;

        var options = new PagBankOptions
        {
            Enabled = false,
            Environment = "Sandbox",
            ProductionEnabled = false,
            RefundsEnabled = false,
            ApiBaseUrl = new Uri("https://sandbox.api.pagseguro.com/"),
            WebPublicBaseUrl = new Uri("https://example.test/"),
            ApiPublicBaseUrl = new Uri("https://api.example.test/"),
            Token = string.Empty,
        };
        var clock = new FixedClock(now);
        var httpContext = new DefaultHttpContext { TraceIdentifier = Guid.NewGuid().ToString("N") };
        httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        var audit = new DelegatingAuditWriter(new IdentityAuditWriter(database,
            new HttpContextAccessor { HttpContext = httpContext },
            IdentitySecurityOptions.Load(configuration, allowInsecureLoopbackHttp: true)));
        var cash = new CashManagementService(database, new NoOpPagBank(), options, audit, clock);
        await cash.RecordPaymentReceivedAsync(original, manager.Id, now, CancellationToken.None);
        await database.SaveChangesAsync();
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now,
            TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")));

        var before = await cash.DayAsync(date, ViverAppRoles.Manager, null, null, appointment.AppointmentNumber, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(180m, before.Summary.NetTotal);
        Assert.Single(before.Page.Items);
        Assert.Equal("4242", before.Page.Items[0].CardLastFour);
        Assert.Equal("AUTH-PHASE17", before.Page.Items[0].AuthorizationReference);

        var reversed = await cash.ReverseAsync(manager.Id, Guid.NewGuid().ToString("N"), original.Id,
            new PaymentReversalRequest(original.RowVersion, "Pagamento lançado na forma incorreta"), CancellationToken.None);
        Assert.Equal("reversed", reversed.PaymentStatusCode);
        Assert.True(reversed.CanCreateReplacementPayment);
        Assert.Equal("pending", appointment.StatusCode);
        Assert.Null(appointment.ArrivedAtUtc);
        Assert.Contains(await database.AppointmentStatusHistories.Where(item => item.AppointmentId == appointment.Id).ToArrayAsync(),
            item => item.FromStatusCode == "confirmed" && item.ToStatusCode == "pending"
                && item.Reason!.Contains("Pagamento cancelado", StringComparison.Ordinal));

        var managerService = new ManagerExperienceService(database, null!, null!, audit, clock, cash);
        var replacement = await managerService.ConfirmPaymentAsync(manager.Id, appointment.Id, Guid.NewGuid().ToString("N"),
            new ManagerPaymentConfirmRequest("pix", now, null, null, appointment.RowVersion), CancellationToken.None);
        Assert.NotEqual(original.Id, replacement.Id);
        Assert.Equal(original.Id, await database.Payments.Where(item => item.Id == replacement.Id)
            .Select(item => item.SupersedesPaymentId).SingleAsync());
        Assert.Equal(2, await database.Payments.CountAsync(item => item.AppointmentId == appointment.Id));
        Assert.Equal(1, await database.Payments.CountAsync(item => item.AppointmentId == appointment.Id && item.ActiveAppointmentId != null));

        var withdrawal = await cash.AddManualAsync(manager.Id, ViverAppRoles.Manager, Guid.NewGuid().ToString("N"),
            new CashManualMovementRequest("withdrawal", "outflow", "cash", 200m, null,
                "Sangria operacional"), CancellationToken.None);
        Assert.Equal("withdrawal", withdrawal.TypeCode);
        Assert.Equal("outflow", withdrawal.DirectionCode);
        Assert.Equal(200m, withdrawal.Amount);
        Assert.Equal("Sangria manual", withdrawal.Description);
        Assert.Equal("Sangria operacional", withdrawal.Reason);

        var day = await cash.DayAsync(date, ViverAppRoles.Manager, null, null, appointment.AppointmentNumber, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(360m, day.Summary.GrossEntries);
        Assert.Equal(180m, day.Summary.PaymentReversals);
        Assert.Equal(180m, day.Summary.NetTotal);
        Assert.Equal(3, day.Summary.MovementCount);
        Assert.Contains(day.Page.Items, item => item.TypeCode == "payment_reversal" && item.RelatedMovementId.HasValue);

        var unfilteredDay = await cash.DayAsync(date, ViverAppRoles.Manager, null, null, null, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(200m, unfilteredDay.Summary.Withdrawals);
        Assert.Equal(-20m, unfilteredDay.Summary.NetTotal);
        Assert.Equal(4, unfilteredDay.Summary.MovementCount);
        Assert.Contains(unfilteredDay.Page.Items, item => item.Id == withdrawal.Id && item.TypeCode == "withdrawal");
        Assert.Equal(unfilteredDay.Page.Items.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id), unfilteredDay.Page.Items);
        Assert.Equal(unfilteredDay.Page.Items.Max(item => item.Id), unfilteredDay.LastMovementId);

        var filterOptions = await cash.FilterOptionsAsync(CancellationToken.None);
        Assert.Contains(filterOptions.Professionals, item => item.AccountId == doctor.Id && item.RoleCode == ViverAppRoles.Doctor);
        Assert.Contains(filterOptions.Responsibles, item => item.AccountId == manager.Id);
        var professionalDay = await cash.DayAsync(date, ViverAppRoles.Manager, null, null, null, null, null, null, null,
            1, 25, CancellationToken.None, professionalAccountId: doctor.Id);
        Assert.Equal(3, professionalDay.Page.Items.Count);
        Assert.DoesNotContain(professionalDay.Page.Items, item => item.Id == withdrawal.Id);
        Assert.Equal(180m, professionalDay.Summary.NetTotal);
        var otherProfessionalDay = await cash.DayAsync(date, ViverAppRoles.Manager, null, null, null, null, null, null, null,
            1, 25, CancellationToken.None, professionalAccountId: administrator.Id);
        Assert.Empty(otherProfessionalDay.Page.Items);
        var responsibleDay = await cash.DayAsync(date, ViverAppRoles.Manager, null, null, null, null, null, null, null,
            1, 25, CancellationToken.None, responsibleAccountId: manager.Id);
        Assert.Contains(responsibleDay.Page.Items, item => item.Id == withdrawal.Id);
        var otherResponsibleDay = await cash.DayAsync(date, ViverAppRoles.Manager, null, null, null, null, null, null, null,
            1, 25, CancellationToken.None, responsibleAccountId: administrator.Id);
        Assert.Empty(otherResponsibleDay.Page.Items);

        var cardFiltered = await cash.DayAsync(date, ViverAppRoles.Manager, null, null, null, null, null, "4242", "PHASE17", 1, 25, CancellationToken.None);
        Assert.Equal(2, cardFiltered.Page.Items.Count);
        Assert.All(cardFiltered.Page.Items, item => Assert.Equal(original.Id, item.PaymentId));

        var fullPrint = await cash.PrintAsync(manager.Id, date, null, null, appointment.AppointmentNumber, null, null, null, null, false, CancellationToken.None);
        var totalsPrint = await cash.PrintAsync(manager.Id, date, null, null, appointment.AppointmentNumber, null, null, null, null, true, CancellationToken.None);
        Assert.Equal(3, fullPrint.Movements.Count);
        Assert.Empty(totalsPrint.Movements);
        Assert.Equal(fullPrint.Summary.NetTotal, totalsPrint.Summary.NetTotal);
        var professionalPrint = await cash.PrintAsync(manager.Id, date, null, null, null, null, null, null, null,
            false, CancellationToken.None, professionalAccountId: doctor.Id);
        Assert.Equal(3, professionalPrint.Movements.Count);
        Assert.Equal(180m, professionalPrint.Summary.NetTotal);
        Assert.Contains("Profissional: Dra. Caixa", professionalPrint.FilterDescription);

        var closure = await cash.CloseAsync(manager.Id, date, new CashCloseRequest(unfilteredDay.LastMovementId), CancellationToken.None);
        Assert.Equal(-20m, closure.Snapshot.NetTotal);
        Assert.Equal(200m, closure.Snapshot.Withdrawals);
        var denied = await Assert.ThrowsAsync<CashRuleException>(() => cash.AddManualAsync(manager.Id, ViverAppRoles.Manager,
            Guid.NewGuid().ToString("N"), new CashManualMovementRequest("supply", "entry", "cash", 25m, null,
                "Abertura complementar após fechamento"), CancellationToken.None));
        Assert.Equal(409, denied.StatusCode);
        var administratorDenied = await Assert.ThrowsAsync<CashRuleException>(() => cash.AddManualAsync(administrator.Id, ViverAppRoles.Administrator,
            Guid.NewGuid().ToString("N"), new CashManualMovementRequest("supply", "entry", "cash", 25m, null,
                "Abertura complementar após fechamento"), CancellationToken.None));
        Assert.Equal(409, administratorDenied.StatusCode);
        var reopening = await cash.ReopenAsync(administrator.Id, ViverAppRoles.Administrator, date,
            new CashReopenRequest("Correção operacional autorizada"), CancellationToken.None);
        Assert.Equal(closure.Id, reopening.CashClosureId);
        var postClose = await cash.AddManualAsync(administrator.Id, ViverAppRoles.Administrator, Guid.NewGuid().ToString("N"),
            new CashManualMovementRequest("supply", "entry", "cash", 25m, null,
                "Abertura complementar após fechamento"), CancellationToken.None);
        Assert.False(postClose.AfterClosure);

        clock.AdvanceTo(now.AddDays(1));
        appointment.StatusCode = "no_show";
        appointment.NoShowRecordedByAccountId = manager.Id;
        appointment.NoShowRecordedAtUtc = clock.GetUtcNow().UtcDateTime;
        appointment.RowVersion++;
        database.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            ActorAccountId = manager.Id,
            FromStatusCode = "confirmed",
            ToStatusCode = "no_show",
            StartsAtUtc = appointment.StartsAtUtc,
            EndsAtUtc = appointment.EndsAtUtc,
            OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
        });
        await database.SaveChangesAsync();
        var statusHistoryCountBeforeRefund = await database.AppointmentStatusHistories
            .CountAsync(item => item.AppointmentId == appointment.Id);
        var noShowPayment = await database.Payments.SingleAsync(item => item.Id == replacement.Id);
        var noShowReversal = await cash.ReverseAsync(manager.Id, Guid.NewGuid().ToString("N"), noShowPayment.Id,
            new PaymentReversalRequest(noShowPayment.RowVersion, "Estorno autorizado após ausência do paciente"), CancellationToken.None);
        Assert.Equal("reversed", noShowReversal.PaymentStatusCode);
        Assert.False(noShowReversal.CanCreateReplacementPayment);
        Assert.Equal("no_show", appointment.StatusCode);
        Assert.Equal(statusHistoryCountBeforeRefund,
            await database.AppointmentStatusHistories.CountAsync(item => item.AppointmentId == appointment.Id));
        var refundDate = date.AddDays(1);
        var refundDay = await cash.DayAsync(refundDate, ViverAppRoles.Manager, null, null,
            appointment.AppointmentNumber, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(-180m, refundDay.Summary.NetTotal);
        var refundMovement = Assert.Single(refundDay.Page.Items);
        Assert.Equal("payment_reversal", refundMovement.TypeCode);
        Assert.Equal("outflow", refundMovement.DirectionCode);
        Assert.Equal(noShowPayment.Id, refundMovement.PaymentId);
        Assert.NotNull(refundMovement.RelatedMovementId);
        var originalDayAfterRefund = await cash.DayAsync(date, ViverAppRoles.Manager, null, null,
            appointment.AppointmentNumber, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(3, originalDayAfterRefund.Summary.MovementCount);

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
        RoleCode = role,
        StatusCode = "active",
        FullName = name,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailVerified = true,
        SecurityStamp = RandomNumberGenerator.GetBytes(32),
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = 1,
    };

    private sealed class DelegatingAuditWriter(IdentityAuditWriter writer) : IClinicalOperationsAuditWriter
    {
        public Task WriteAsync(string eventCode, ulong actorAccountId, string entityType, string entityId,
            IReadOnlyDictionary<string, string>? safeData, CancellationToken cancellationToken) =>
            writer.WriteAsync(eventCode, actorAccountId, entityType, entityId, safeData, cancellationToken);
    }

    private sealed class FixedClock(DateTime value) : TimeProvider
    {
        public void AdvanceTo(DateTime utc) => value = utc;
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
