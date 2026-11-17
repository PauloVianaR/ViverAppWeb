using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.PatientExperience;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.ClinicalOperations.Tests;

public sealed class PatientBirthdayIntegrationTests
{
    [Fact]
    public async Task MonthAndDailyNoticeUseClinicDateFallbackBirthAndPerAccountReadState()
    {
        await using var db = CreateContext();
        await db.Database.OpenConnectionAsync();
        Assert.StartsWith("8.0.41", db.Database.GetDbConnection().ServerVersion);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Clinics.ExecuteUpdateAsync(s => s.SetProperty(c => c.TimezoneName, "America/Sao_Paulo"));
        var marker = "__phase34_" + Guid.NewGuid().ToString("N");
        var manager = Account("manager", marker + " Gestor");
        var doctor = Account("doctor", marker + " Médico");
        var psychologist = Account("psychologist", marker + " Psicólogo");
        var admin = Account("administrator", marker + " Admin");
        var today = Account("patient", marker + " Hoje", new(2000, 11, 17));
        var fallback = Account("patient", marker + " Fallback");
        var missing = Account("patient", marker + " Sem nascimento");
        var other = Account("patient", marker + " Outro mês", new(2000, 10, 17));
        var future = Account("patient", marker + " Data futura", new(2030, 11, 17));
        db.Accounts.AddRange(manager, doctor, psychologist, admin, today, fallback, missing, other, future);
        await db.SaveChangesAsync();
        db.PatientProfiles.Add(new PatientProfile { AccountId = fallback.Id, BirthDate = new(1985, 11, 2), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 18, 2, 30, 0, TimeSpan.Zero));
        var service = new PatientBirthdayService(db, clock);
        foreach (var actor in new[] { manager.Id, doctor.Id, psychologist.Id, admin.Id })
        {
            var month = await service.MonthAsync(actor, 1, 50, default);
            Assert.Equal(new DateOnly(2026, 11, 17), month.Today);
            Assert.Contains(month.Items, p => p.AccountId == today.Id && p.IsToday && p.TurningAge == 26);
            Assert.Contains(month.Items, p => p.AccountId == fallback.Id && !p.IsToday && p.Day == 2);
            Assert.DoesNotContain(month.Items, p => new[] { missing.Id, other.Id, future.Id }.Contains(p.AccountId));
            Assert.Equal(month.Items.OrderBy(p => p.Day).Select(p => p.Day), month.Items.Select(p => p.Day));
        }
        var first = await service.CheckAsync(manager.Id, default);
        Assert.True(first.IsNew);
        Assert.True(first.Notice.BirthdayCount > 0);
        Assert.False((await service.CheckAsync(manager.Id, default)).IsNew);
        var own = await service.CheckAsync(doctor.Id, default);
        Assert.NotEqual(first.Notice.Id, own.Notice.Id);
        var forbidden = await Assert.ThrowsAsync<PatientExperienceException>(() => service.ReadAsync(doctor.Id, first.Notice.Id, default));
        Assert.Equal(404, forbidden.StatusCode);
        await service.ReadAsync(manager.Id, first.Notice.Id, default);
        Assert.True((await service.CheckAsync(manager.Id, default)).Notice.IsRead);
        Assert.False((await service.CheckAsync(doctor.Id, default)).Notice.IsRead);
        clock.Now = clock.Now.AddHours(1);
        var next = await service.CheckAsync(manager.Id, default);
        Assert.True(next.IsNew);
        Assert.Equal(new DateOnly(2026, 11, 18), next.Notice.OperationalDate);
        Assert.NotEqual(first.Notice.Id, next.Notice.Id);
        Assert.Equal(403, (await Assert.ThrowsAsync<PatientExperienceException>(() => service.MonthAsync(today.Id, 1, 24, default))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<PatientExperienceException>(() => service.CheckAsync(today.Id, default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<PatientExperienceException>(() => service.MonthAsync(manager.Id, 0, 24, default))).StatusCode);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task February29RemainsInFebruaryAndChangedDailyCountDoesNotDuplicateTheNotice()
    {
        await using var db = CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var staff = Account("manager", "__phase34_fevereiro__");
        var patient = Account("patient", "__phase34_bissexto__", new(2000, 2, 29));
        db.Accounts.AddRange(staff, patient);
        await db.SaveChangesAsync();
        var clock = new MutableClock(new DateTimeOffset(2028, 2, 29, 15, 0, 0, TimeSpan.Zero));
        var service = new PatientBirthdayService(db, clock);
        var check = await service.CheckAsync(staff.Id, default);
        await service.ReadAsync(staff.Id, check.Notice.Id, default);
        var additional = Account("patient", "__phase34_bissexto_extra__", new(2004, 2, 29));
        db.Accounts.Add(additional);
        await db.SaveChangesAsync();
        var changed = await service.CheckAsync(staff.Id, default);
        Assert.Equal(check.Notice.Id, changed.Notice.Id);
        Assert.Equal(check.Notice.BirthdayCount + 1, changed.Notice.BirthdayCount);
        Assert.False(changed.Notice.IsRead);
        clock.Now = new(2027, 2, 28, 15, 0, 0, TimeSpan.Zero);
        var month = await service.MonthAsync(staff.Id, 1, 50, default);
        Assert.Contains(month.Items, p => p.AccountId == patient.Id && p.Day == 29 && !p.IsToday);
        await tx.RollbackAsync();
    }

    private static ViverAppDbContext CreateContext()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<PatientBirthdayIntegrationTests>().Build();
        var connection = new MySqlConnectionStringBuilder(configuration.GetConnectionString("LocalConnection"));
        if (connection.Database != "viverappweb" || connection.Server is not ("localhost" or "127.0.0.1")) throw new InvalidOperationException("Banco recusado.");
        return new(new DbContextOptionsBuilder<ViverAppDbContext>().UseMySQL(connection.ConnectionString).Options);
    }
    private static Account Account(string role, string name, DateTime? birth = null)
    {
        var email = Guid.NewGuid().ToString("N") + "@phase34.example.test";
        return new()
        {
            FullName = name,
            RoleCode = role,
            StatusCode = "active",
            BirthDate = birth,
            PortalAccessEnabled = role != "patient",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = RandomNumberGenerator.GetBytes(32),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = 1
        };
    }
    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
