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
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.ClinicalOperations.Tests;

public sealed class CashManagementIntegrationTests
{
    [Theory]
    [InlineData("manager")]
    [InlineData("administrator")]
    public async Task CorrectiveAdjustmentsNeedReasonButNotCompensatedMovementAndHaveSignedTotals(string role)
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<CashManagementIntegrationTests>().Build();
        await using var database = CreateContext(configuration);
        await using var transaction = await database.Database.BeginTransactionAsync();
        var now = new DateTime(2099, 7, 19, 15, 0, 0, DateTimeKind.Utc);
        var actor = Account($"adjustment-{Guid.NewGuid():N}@phase32.example.test", role, "QA Ajuste Corretivo");
        database.Accounts.Add(actor); await database.SaveChangesAsync();
        var cash = new CashManagementService(database, new NoOpPagBank(), new PagBankOptions
        {
            ApiBaseUrl = new("https://sandbox.example.test"),
            WebPublicBaseUrl = new("https://example.test"),
            ApiPublicBaseUrl = new("https://api.example.test"),
            Token = "",
        }, new NullFinancialAudit(), new FixedClock(now));
        var date = new DateOnly(2099, 7, 19);
        Task<CashDayResponse> Day() => cash.DayAsync(date, role, null, null, null, null, null, null, null, 1, 100, default);
        var before = await Day();
        var key = Guid.NewGuid().ToString("N");
        var request = new CashManualMovementRequest("adjustment", "entry", "cash", 35m, null, "Correção sintética positiva");
        var entry = await cash.AddManualAsync(actor.Id, role, key, request, default);
        Assert.Equal(entry.Id, (await cash.AddManualAsync(actor.Id, role, key, request, default)).Id);
        var outflow = await cash.AddManualAsync(actor.Id, role, Guid.NewGuid().ToString("N"),
            new("adjustment", "outflow", "pix", 12m, null, "Correção sintética negativa"), default);
        Assert.Null(entry.RelatedMovementId); Assert.Null(outflow.RelatedMovementId);
        var after = await Day();
        Assert.Equal(before.Summary.CorrectiveAdjustmentsNet + 23, after.Summary.CorrectiveAdjustmentsNet);
        Assert.Equal(before.Summary.AdjustmentsNet + 23, after.Summary.AdjustmentsNet);
        Assert.Equal(before.Summary.NetTotal + 23, after.Summary.NetTotal);
        Assert.Equal(before.CumulativeSummary!.CorrectiveAdjustmentsNet + 23, after.CumulativeSummary!.CorrectiveAdjustmentsNet);
        Assert.Equal(before.CumulativeSummary.NetTotal + 23, after.CumulativeSummary.NetTotal);
        Assert.Equal(before.Summary.MovementCount + 2, after.Summary.MovementCount);
        foreach (var totalsOnly in new[] { false, true })
        {
            var print = await cash.PrintAsync(actor.Id, date, null, null, null, null, null, null, null, totalsOnly, default);
            Assert.Equal(after.Summary.CorrectiveAdjustmentsNet, print.Summary.CorrectiveAdjustmentsNet);
            Assert.Equal(after.Summary.NetTotal, print.Summary.NetTotal);
        }
        var invalidReason = await Assert.ThrowsAsync<CashRuleException>(() => cash.AddManualAsync(actor.Id, role,
            Guid.NewGuid().ToString("N"), request with { Reason = "" }, default));
        Assert.Equal(400, invalidReason.StatusCode);
        var invalidLink = await Assert.ThrowsAsync<CashRuleException>(() => cash.AddManualAsync(actor.Id, role,
            Guid.NewGuid().ToString("N"), request with { TypeCode = "supply", RelatedMovementId = entry.Id }, default));
        Assert.Equal(400, invalidLink.StatusCode);
        await transaction.RollbackAsync();
    }

    [Fact]
    public void CashPrintIncludesEverySupportedMethodWithoutInventingMovements()
    {
        var summary = new CashSummaryResponse(100, 0, 0, 0, 0, 100, 1, [new("pix", 100, 0, 100, 1)]);
        var print = CashManagementService.WithAllMethods(summary);
        Assert.Equal(new[] { "cash", "pix", "debit_card", "credit_card", "pagbank_online", "other" }, print.ByMethod.Select(m => m.MethodCode));
        Assert.Equal(100, print.NetTotal); Assert.Equal(1, print.MovementCount);
        Assert.All(print.ByMethod.Where(m => m.MethodCode != "pix"), m => { Assert.Equal(0, m.Net); Assert.Equal(0, m.Entries); Assert.Equal(0, m.Outflows); Assert.Equal(0, m.MovementCount); });
        Assert.Equal(6, CashManagementService.WithAllMethods(new(0, 0, 0, 0, 0, 0, 0, [])).ByMethod.Count);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("confirmed")]
    [InlineData("arrived")]
    [InlineData("in_progress")]
    [InlineData("completed")]
    [InlineData("canceled")]
    [InlineData("no_show")]
    public async Task DeferredSplitSettlementPreservesClinicalStateAndUsesTheActualCashDay(string status)
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<CashManagementIntegrationTests>().Build();
        await using var database = CreateContext(configuration);
        await using var transaction = await database.Database.BeginTransactionAsync();
        var now = new DateTime(2099, 7, 18, 15, 0, 0, DateTimeKind.Utc);
        var actor = Account($"manager-{Guid.NewGuid():N}@phase32.example.test", "manager", "QA Gestão Posterior");
        var patient = Account($"patient-{Guid.NewGuid():N}@phase32.example.test", "patient", "QA Paciente Posterior");
        var doctor = Account($"doctor-{Guid.NewGuid():N}@phase32.example.test", "doctor", "QA Médico Posterior");
        database.Accounts.AddRange(actor, patient, doctor); await database.SaveChangesAsync();
        database.ProfessionalProfiles.Add(new()
        {
            AccountId = doctor.Id,
            LicenseStateCode = "MG",
            LicenseTypeCode = "CRM",
            LicenseNumber = Guid.NewGuid().ToString("N")[..12],
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        });
        var type = new AppointmentType
        {
            Name = $"__phase32_{Guid.NewGuid():N}",
            CategoryCode = "consultation",
            ModalityCode = "both",
            DurationMinutes = 10,
            RequiresPayment = true,
            PriceAmount = 100,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        };
        database.AppointmentTypes.Add(type); await database.SaveChangesAsync();
        var appointment = new Appointment
        {
            AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63),
            PatientAccountId = patient.Id,
            ProfessionalAccountId = doctor.Id,
            CreatedByAccountId = actor.Id,
            AppointmentTypeId = type.Id,
            StatusCode = status,
            BillingFormCode = "private",
            ModalityCode = "online",
            PaymentLocationCode = "clinic",
            PaymentDeferred = true,
            RequiresPayment = true,
            BasePriceAmount = 100,
            DiscountPercent = 20,
            PointDiscountKindCode = "percent",
            PointDiscountValue = 10,
            PointDiscountAmount = 8,
            PointDiscountByAccountId = actor.Id,
            PointDiscountAtUtc = now,
            PriceAmount = 72,
            CurrencyCode = "BRL",
            StartsAtUtc = now.AddDays(-2),
            EndsAtUtc = now.AddDays(-2).AddMinutes(10),
            PatientNotes = "Observação mantida",
            CreatedAtUtc = now.AddDays(-3),
            UpdatedAtUtc = now,
            RowVersion = 1
        };
        if (status == "completed") { appointment.CompletedAtUtc = now; appointment.CompletedByAccountId = actor.Id; }
        if (status == "no_show") { appointment.NoShowRecordedAtUtc = now; appointment.NoShowRecordedByAccountId = actor.Id; }
        if (status == "canceled") { appointment.CanceledAtUtc = now; appointment.CanceledByAccountId = actor.Id; appointment.CancellationReason = "Cancelamento sintético"; }
        database.Appointments.Add(appointment); await database.SaveChangesAsync();
        var audit = new NullFinancialAudit(); var clock = new FixedClock(now);
        var cash = new CashManagementService(database, new NoOpPagBank(), new PagBankOptions
        {
            ApiBaseUrl = new("https://sandbox.example.test"),
            WebPublicBaseUrl = new("https://example.test"),
            ApiPublicBaseUrl = new("https://api.example.test"),
            Token = "",
        }, audit, clock);
        var manager = new ManagerExperienceService(database, null!, null!, audit, clock, cash);
        Assert.True(AppointmentPaymentPolicy.CanReceiveManually(appointment));
        var own = database.Appointments.Where(a => a.Id == appointment.Id);
        Assert.Equal(status != "canceled", await own.Where(AppointmentPaymentPolicy.Pending).AnyAsync());
        Assert.Equal(status != "canceled", await AppointmentPaymentPolicy.FilterPending(own, true).AnyAsync());
        Assert.False(await AppointmentPaymentPolicy.FilterPending(own, false).AnyAsync());
        Assert.True(await AppointmentPaymentPolicy.FilterPending(own, null).AnyAsync());
        var key = Guid.NewGuid().ToString("N");
        var request = new ManagerPaymentConfirmRequest("cash", now.AddDays(-1), null, null, 1,
            [new("cash", 22, null, null), new("pix", 50, null, null)]);
        var paid = await manager.ConfirmPaymentAsync(actor.Id, appointment.Id, key, request, default);
        var replay = await manager.ConfirmPaymentAsync(actor.Id, appointment.Id, key, request, default);
        Assert.Equal(paid.Id, replay.Id);
        var saved = await database.Appointments.AsNoTracking().Include(a => a.CurrentPayment).SingleAsync(a => a.Id == appointment.Id);
        Assert.Equal(status == "pending" ? "confirmed" : status, saved.StatusCode);
        Assert.Equal(72, saved.PriceAmount); Assert.Equal("Observação mantida", saved.PatientNotes);
        Assert.Equal(2UL, saved.RowVersion);
        Assert.False(AppointmentPaymentPolicy.DeferredOutstanding(saved));
        Assert.False(AppointmentPaymentPolicy.CanReceiveManually(saved));
        var movements = await database.CashMovements.Where(m => m.AppointmentId == appointment.Id).ToArrayAsync();
        Assert.Equal(2, movements.Length); Assert.Equal(72, movements.Sum(m => m.Amount));
        Assert.All(movements, m => Assert.Equal(new DateTime(2099, 7, 18), m.OperationalDate));
        var print = await cash.PrintAsync(actor.Id, new(2099, 7, 18), null, null, appointment.AppointmentNumber,
            null, null, null, null, false, default);
        Assert.Equal(6, print.Summary.ByMethod.Count); Assert.Equal(22, print.Summary.ByMethod.Single(m => m.MethodCode == "cash").Net);
        Assert.Equal(50, print.Summary.ByMethod.Single(m => m.MethodCode == "pix").Net);
        var totalsPrint = await cash.PrintAsync(actor.Id, new(2099, 7, 18), null, null, appointment.AppointmentNumber,
            null, null, null, null, true, default);
        Assert.Empty(totalsPrint.Movements); Assert.Equal(6, totalsPrint.Summary.ByMethod.Count);
        var stale = await Assert.ThrowsAsync<ManagerRuleException>(() => manager.ConfirmPaymentAsync(actor.Id, appointment.Id,
            Guid.NewGuid().ToString("N"), request, default));
        Assert.Equal(409, stale.StatusCode);
        if (saved.StatusCode is "confirmed" or "arrived" or "no_show")
        {
            await cash.ReverseAsync(actor.Id, Guid.NewGuid().ToString("N"), paid.Id,
                new(paid.RowVersion, "Reversão exclusivamente sintética"), default);
            var reversed = await database.Appointments.AsNoTracking().Include(a => a.CurrentPayment).SingleAsync(a => a.Id == appointment.Id);
            Assert.Equal(saved.StatusCode, reversed.StatusCode);
            Assert.True(AppointmentPaymentPolicy.DeferredOutstanding(reversed));
            Assert.True(AppointmentPaymentPolicy.CanReceiveManually(reversed));
            Assert.Equal(3UL, reversed.RowVersion);
        }
        await transaction.RollbackAsync();
    }

    private sealed class NullFinancialAudit : IClinicalOperationsAuditWriter
    {
        public Task WriteAsync(string eventCode, ulong actorAccountId, string entityType, string entityId,
            IReadOnlyDictionary<string, string>? safeData, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task AppointmentPrintIncludesAllFilteredRowsAcrossPagesAndEnforcesOwnership()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<CashManagementIntegrationTests>().Build();
        await using var database = CreateContext(configuration);
        await using var transaction = await database.Database.BeginTransactionAsync();
        var now = new DateTime(2099, 7, 18, 15, 0, 0, DateTimeKind.Utc);
        var marker = Guid.NewGuid().ToString("N");
        var accounts = new[] { Account($"manager-{marker}@phase32.example.test", "manager", "QA Relatório " + marker),
            Account($"admin-{marker}@phase32.example.test", "administrator", "QA Admin " + marker),
            Account($"patient-{marker}@phase32.example.test", "patient", "QA Paciente " + marker),
            Account($"other-{marker}@phase32.example.test", "patient", "QA Outro " + marker),
            Account($"doctor-{marker}@phase32.example.test", "doctor", "QA Médico " + marker),
            Account($"psychologist-{marker}@phase32.example.test", "psychologist", "QA Psicólogo " + marker) };
        database.Accounts.AddRange(accounts); await database.SaveChangesAsync();
        foreach (var professional in accounts.Skip(4)) database.ProfessionalProfiles.Add(new()
        {
            AccountId = professional.Id,
            LicenseStateCode = "MG",
            LicenseTypeCode = professional.RoleCode == "doctor" ? "CRM" : "CRP",
            LicenseNumber = Guid.NewGuid().ToString("N")[..12],
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        });
        var type = new AppointmentType
        {
            Name = "__phase32_report_" + marker,
            CategoryCode = "examination",
            ModalityCode = "in_person",
            DurationMinutes = 10,
            PriceAmount = 100,
            RequiresPayment = true,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        };
        database.AppointmentTypes.Add(type); await database.SaveChangesAsync();
        for (var i = 0; i < 25; i++) database.Appointments.Add(new()
        {
            AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63),
            PatientAccountId = accounts[i < 20 ? 2 : 3].Id,
            ProfessionalAccountId = accounts[i < 22 ? 4 : 5].Id,
            CreatedByAccountId = accounts[0].Id,
            AppointmentTypeId = type.Id,
            SpecialtyId = type.SpecialtyId,
            StatusCode = "confirmed",
            PaymentDeferred = true,
            RequiresPayment = true,
            BillingFormCode = "private",
            ModalityCode = "in_person",
            PaymentLocationCode = "clinic",
            StartsAtUtc = now.AddMinutes(i * 10),
            EndsAtUtc = now.AddMinutes(i * 10 + 10),
            BasePriceAmount = 100,
            PriceAmount = 80,
            DiscountPercent = 20,
            CurrencyCode = "BRL",
            PatientNotes = "<script>não executar</script>",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        });
        await database.SaveChangesAsync();
        var clock = new FixedClock(now); var audit = new NullFinancialAudit();
        var reports = new AppointmentPrintService(database, clock, audit);
        var filter = new AppointmentPrintQuery { Search = marker, From = new(2099, 7, 18), To = new(2099, 7, 18), PendingPayment = true };
        var manager = new ManagerExperienceService(database, null!, null!, audit, clock);
        var page = await manager.AgendaAsync(filter.From, filter.To, null, null, null, null, null, null, null, null,
            marker, "date_asc", 1, 12, default, pendingPayment: true);
        Assert.Equal(25, page.Page.TotalCount); Assert.Equal(12, page.Page.Items.Count);
        var full = await reports.PrintAsync(accounts[0].Id, filter, default);
        Assert.Equal(25, full.Rows.Count);
        Assert.Equal(page.Sources.Total.Order(), full.Rows.Select(r => r.Number).Order());
        Assert.All(full.Rows, r => { Assert.Equal(100, r.BasePriceAmount); Assert.Equal(80, r.PriceAmount); Assert.Equal("Particular", r.BillingFormCode == "private" ? "Particular" : "Convênio"); });
        Assert.Equal(25, (await reports.PrintAsync(accounts[1].Id, filter, default)).Rows.Count);
        Assert.Equal(20, (await reports.PrintAsync(accounts[2].Id, filter, default)).Rows.Count);
        Assert.Equal(22, (await reports.PrintAsync(accounts[4].Id, filter, default)).Rows.Count);
        Assert.Equal(3, (await reports.PrintAsync(accounts[5].Id, filter, default)).Rows.Count);
        filter.DoctorAccountId = accounts[4].Id;
        Assert.Empty((await reports.PrintAsync(accounts[5].Id, filter, default)).Rows);
        filter.DoctorAccountId = null; filter.PendingPayment = false;
        Assert.Empty((await reports.PrintAsync(accounts[0].Id, filter, default)).Rows);
        filter.PendingPayment = true; filter.StartTime = new(12, 0); filter.EndTime = new(12, 30);
        var timed = await reports.PrintAsync(accounts[0].Id, filter, default);
        Assert.Equal(3, timed.Rows.Count);
        filter.EndTime = new(11, 0);
        await Assert.ThrowsAsync<ViverApp.Api.Features.AdministratorExperience.AdministratorRuleException>(() => reports.PrintAsync(accounts[0].Id, filter, default));
        // A canceled debt stays collectible, but must disappear from the pending-payment recut and daily count.
        var canceled = await database.Appointments.Where(a => a.AppointmentTypeId == type.Id).FirstAsync();
        var notices = new PaymentPendingNoticeService(database, clock);
        var beforeNotice = await notices.CheckAsync(accounts[0].Id, default);
        canceled.StatusCode = "canceled"; canceled.CanceledAtUtc = now; canceled.CanceledByAccountId = accounts[0].Id;
        canceled.CancellationReason = "Cancelamento sintético para validar filtro";
        await database.SaveChangesAsync();
        filter.StartTime = null; filter.EndTime = null;
        Assert.Equal(24, (await reports.PrintAsync(accounts[0].Id, filter, default)).Rows.Count);
        var afterNotice = await notices.CheckAsync(accounts[1].Id, default);
        Assert.Equal(beforeNotice.Notice.PendingCount - 1, afterNotice.Notice.PendingCount);
        Assert.True(AppointmentPaymentPolicy.CanReceiveManually(canceled));
        filter.PendingPayment = null;
        Assert.Equal(25, (await reports.PrintAsync(accounts[0].Id, filter, default)).Rows.Count);
        filter.Payment = "pending";
        Assert.Equal(25, (await reports.PrintAsync(accounts[0].Id, filter, default)).Rows.Count);
        filter.PendingPayment = true;
        Assert.Equal(24, (await reports.PrintAsync(accounts[0].Id, filter, default)).Rows.Count);
        var pendingPage = await manager.AgendaAsync(filter.From, filter.To, null, null, null, null, null, "pending", null,
            null, marker, "date_asc", 1, 12, default, pendingPayment: true);
        Assert.Equal(24, pendingPage.Page.TotalCount);
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("administrator")]
    public async Task DailyPaymentNoticeUsesClinicDayAndIsPerAccount(string role)
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<CashManagementIntegrationTests>().Build();
        await using var database = CreateContext(configuration);
        await using var transaction = await database.Database.BeginTransactionAsync();
        var actor = Account($"notice-{Guid.NewGuid():N}@phase32.example.test", role, "QA Aviso Diário");
        var other = Account($"other-notice-{Guid.NewGuid():N}@phase32.example.test", role, "QA Outro Aviso");
        var patient = Account($"patient-notice-{Guid.NewGuid():N}@phase32.example.test", "patient", "QA Paciente Aviso");
        database.Accounts.AddRange(actor, other, patient); await database.SaveChangesAsync();
        var clock = new FixedClock(new DateTime(2099, 7, 18, 2, 55, 0, DateTimeKind.Utc));
        var service = new PaymentPendingNoticeService(database, clock);
        var first = await service.CheckAsync(actor.Id, default);
        Assert.True(first.IsNew); Assert.Equal(new DateOnly(2099, 7, 17), first.Notice.OperationalDate);
        var again = await service.CheckAsync(actor.Id, default);
        Assert.False(again.IsNew); Assert.Equal(first.Notice.Id, again.Notice.Id);
        Assert.True((await service.CheckAsync(other.Id, default)).IsNew);
        Assert.Equal(0, await service.ReadAsync(other.Id, first.Notice.Id, default));
        Assert.Equal(1, await service.ReadAsync(actor.Id, first.Notice.Id, default));
        Assert.True((await service.CheckAsync(actor.Id, default)).Notice.IsRead);
        clock.AdvanceTo(new DateTime(2099, 7, 18, 3, 5, 0, DateTimeKind.Utc));
        var tomorrow = await service.CheckAsync(actor.Id, default);
        Assert.True(tomorrow.IsNew); Assert.Equal(new DateOnly(2099, 7, 18), tomorrow.Notice.OperationalDate);
        await Assert.ThrowsAsync<ViverApp.Api.Features.AdministratorExperience.AdministratorRuleException>(() => service.CheckAsync(patient.Id, default));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ConcurrentDailyEntriesCreateExactlyOneNotice()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<CashManagementIntegrationTests>().Build();
        ulong actorId = 0;
        try
        {
            await using (var database = CreateContext(configuration))
            {
                var actor = Account($"notice-race-{Guid.NewGuid():N}@phase32.example.test", "manager", "QA Aviso Concorrente");
                database.Accounts.Add(actor); await database.SaveChangesAsync(); actorId = actor.Id;
            }
            async Task<PaymentPendingDailyCheckResponse> Check()
            {
                await using var database = CreateContext(configuration);
                return await new PaymentPendingNoticeService(database, new FixedClock(new DateTime(2099, 7, 18, 15, 0, 0, DateTimeKind.Utc))).CheckAsync(actorId, default);
            }
            var checks = await Task.WhenAll(Check(), Check());
            Assert.Single(checks, c => c.IsNew); Assert.Equal(checks[0].Notice.Id, checks[1].Notice.Id);
        }
        finally
        {
            if (actorId != 0) { await using var database = CreateContext(configuration); await database.Accounts.Where(a => a.Id == actorId).ExecuteDeleteAsync(); }
        }
    }

    [Fact]
    public void PointDiscountAndSplitPaymentRangesUseInvariantLimitsUnderBrazilianCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var ranges = new[]
            {
                typeof(AppointmentPointDiscountRequest).GetConstructors().Single().GetParameters()
                    .Single(x => x.Name == nameof(AppointmentPointDiscountRequest.Value))
                    .GetCustomAttribute<RangeAttribute>()!,
                typeof(ManagerPaymentAllocationRequest).GetConstructors().Single().GetParameters()
                    .Single(x => x.Name == nameof(ManagerPaymentAllocationRequest.Amount))
                    .GetCustomAttribute<RangeAttribute>()!,
            };
            foreach (var range in ranges)
            {
                Assert.True(range.IsValid(0.01m));
                Assert.True(range.IsValid(10m));
                Assert.False(range.IsValid(-0.01m));
            }
            Assert.True(ranges[0].IsValid(0m)); // Removing a point discount is an explicit supported operation.
            Assert.False(ranges[1].IsValid(0m)); // A payment allocation must remain strictly positive.
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

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
        var pendingAppointment = new Appointment
        {
            AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63),
            PatientAccountId = patient.Id,
            ProfessionalAccountId = doctor.Id,
            AppointmentTypeId = appointmentType.Id,
            CreatedByAccountId = manager.Id,
            StatusCode = "pending",
            ModalityCode = "in_person",
            StartsAtUtc = now.AddDays(2),
            EndsAtUtc = now.AddDays(2).AddMinutes(30),
            PriceAmount = 180m,
            BasePriceAmount = 180m,
            RequiresPayment = true,
            CurrencyCode = "BRL",
            PaymentLocationCode = "clinic",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Appointments.Add(pendingAppointment);
        await database.SaveChangesAsync();
        var schedulingAudit = new SchedulingAuditAdapter(new IdentityAuditWriter(database,
            new HttpContextAccessor { HttpContext = httpContext },
            IdentitySecurityOptions.Load(configuration, allowInsecureLoopbackHttp: true)));
        var scheduling = new PatientSchedulingService(database, schedulingAudit, clock);
        var discounted = await scheduling.ApplyPointDiscountForManagerAsync(manager.Id, pendingAppointment.Id,
            new AppointmentPointDiscountRequest("percent", 10m, pendingAppointment.RowVersion), CancellationToken.None);
        Assert.Equal(162m, discounted.PriceAmount);
        Assert.Equal(18m, discounted.PointDiscountAmount);
        var firstPayment = await new ManagerExperienceService(database, null!, null!, audit, clock, cash)
            .ConfirmPaymentAsync(manager.Id, pendingAppointment.Id, Guid.NewGuid().ToString("N"),
                new ManagerPaymentConfirmRequest("cash", now.AddDays(-1), null, null, discounted.RowVersion,
                [new("cash", 100m, null, null), new("pix", 62m, null, null)]), CancellationToken.None);
        Assert.Equal("mixed", firstPayment.MethodCode);
        Assert.Equal(2, await database.CashMovements.CountAsync(item => item.PaymentId == firstPayment.Id));
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
            new ManagerPaymentConfirmRequest("cash", now, null, null, appointment.RowVersion,
            [
                new("cash", 50m, null, null),
                new("credit_card", 20m, "1234", "AUTH-MISTO"),
                new("pix", 110m, null, null),
            ]), CancellationToken.None);
        Assert.Equal("mixed", replacement.MethodCode);
        Assert.Equal(3, replacement.Allocations.Count);
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
        Assert.Equal(5, day.Summary.MovementCount);
        Assert.Equal(50m, day.Summary.ByMethod.Single(item => item.MethodCode == "cash").Net);
        Assert.Equal(20m, day.Summary.ByMethod.Single(item => item.MethodCode == "credit_card").Net);
        Assert.Equal(110m, day.Summary.ByMethod.Single(item => item.MethodCode == "pix").Net);
        Assert.Contains(day.Page.Items, item => item.MethodCode == "credit_card"
            && item.CardLastFour == "1234" && item.AuthorizationReference == "AUTH-MISTO");
        Assert.Contains(day.Page.Items, item => item.TypeCode == "payment_reversal" && item.RelatedMovementId.HasValue);

        var unfilteredDay = await cash.DayAsync(date, ViverAppRoles.Manager, null, null, null, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(200m, unfilteredDay.Summary.Withdrawals);
        Assert.Equal(-20m, unfilteredDay.Summary.NetTotal);
        Assert.NotNull(day.CumulativeSummary);
        Assert.Equal(unfilteredDay.CumulativeSummary!.ByMethod, day.CumulativeSummary!.ByMethod);
        Assert.Equal(day.CumulativeSummary.NetTotal, day.CumulativeSummary.ByMethod.Sum(item => item.Net));
        Assert.All(day.CumulativeSummary.ByMethod, item => Assert.Equal(item.Entries - item.Outflows, item.Net));
        // The whole cumulative breakdown follows the existing manager permission, not just its net total.
        var cumulativeSetting = await database.ApplicationSettings.SingleAsync(item => item.SettingKey == "cash.manager_can_view_cumulative_totals");
        var previousCumulativeValue = cumulativeSetting.ValueJson;
        cumulativeSetting.ValueJson = "false";
        await database.SaveChangesAsync();
        var hiddenCumulative = await cash.DayAsync(date, ViverAppRoles.Manager, null, null, null, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Null(hiddenCumulative.CumulativeSummary);
        var administratorCumulative = await cash.DayAsync(date, ViverAppRoles.Administrator, null, null, null, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(day.CumulativeSummary.ByMethod, administratorCumulative.CumulativeSummary!.ByMethod);
        cumulativeSetting.ValueJson = previousCumulativeValue;
        await database.SaveChangesAsync();
        Assert.Equal(6, unfilteredDay.Summary.MovementCount);
        Assert.Contains(unfilteredDay.Page.Items, item => item.Id == withdrawal.Id && item.TypeCode == "withdrawal");
        Assert.Equal(unfilteredDay.Page.Items.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id), unfilteredDay.Page.Items);
        Assert.Equal(unfilteredDay.Page.Items.Max(item => item.Id), unfilteredDay.LastMovementId);

        var filterOptions = await cash.FilterOptionsAsync(CancellationToken.None);
        Assert.Contains(filterOptions.Professionals, item => item.AccountId == doctor.Id && item.RoleCode == ViverAppRoles.Doctor);
        Assert.Contains(filterOptions.Responsibles, item => item.AccountId == manager.Id);
        var professionalDay = await cash.DayAsync(date, ViverAppRoles.Manager, null, null, null, null, null, null, null,
            1, 25, CancellationToken.None, professionalAccountId: doctor.Id);
        Assert.Equal(5, professionalDay.Page.Items.Count);
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
        Assert.Equal(5, fullPrint.Movements.Count);
        Assert.Empty(totalsPrint.Movements);
        Assert.Equal(fullPrint.Summary.NetTotal, totalsPrint.Summary.NetTotal);
        Assert.NotNull(fullPrint.CumulativeSummary);
        Assert.Equal(fullPrint.CumulativeSummary!.NetTotal, totalsPrint.CumulativeSummary?.NetTotal);
        var professionalPrint = await cash.PrintAsync(manager.Id, date, null, null, null, null, null, null, null,
            false, CancellationToken.None, professionalAccountId: doctor.Id);
        Assert.Equal(5, professionalPrint.Movements.Count);
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
        Assert.Equal(3, refundDay.Page.Items.Count);
        Assert.All(refundDay.Page.Items, refundMovement =>
        {
            Assert.Equal("payment_reversal", refundMovement.TypeCode);
            Assert.Equal("outflow", refundMovement.DirectionCode);
            Assert.Equal(noShowPayment.Id, refundMovement.PaymentId);
            Assert.NotNull(refundMovement.RelatedMovementId);
        });
        Assert.Equal(50m, refundDay.Summary.ByMethod.Single(item => item.MethodCode == "cash").Outflows);
        Assert.Equal(20m, refundDay.Summary.ByMethod.Single(item => item.MethodCode == "credit_card").Outflows);
        Assert.Equal(110m, refundDay.Summary.ByMethod.Single(item => item.MethodCode == "pix").Outflows);
        var originalDayAfterRefund = await cash.DayAsync(date, ViverAppRoles.Manager, null, null,
            appointment.AppointmentNumber, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(5, originalDayAfterRefund.Summary.MovementCount);

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

    private sealed class SchedulingAuditAdapter(IdentityAuditWriter writer) : IPatientSchedulingAuditWriter
    {
        public Task WriteAsync(string eventCode, ulong actorAccountId, ulong appointmentId,
            IReadOnlyDictionary<string, string>? safeData, CancellationToken cancellationToken) =>
            writer.WriteAsync(eventCode, actorAccountId, "appointment",
                appointmentId.ToString(CultureInfo.InvariantCulture), safeData, cancellationToken);
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
