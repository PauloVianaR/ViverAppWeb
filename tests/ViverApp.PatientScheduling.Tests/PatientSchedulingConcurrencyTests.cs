using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.PatientScheduling.Tests;

public sealed class PatientSchedulingConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RescheduleUpdatesSameAppointment_PreservesStatusAndAddsHistory(bool paid)
    {
        var configuration = LoadConfiguration();
        await DeleteStaleFixturesAsync(configuration);
        var fixture = await CreateFixtureAsync(configuration);
        try
        {
            var createRequest = new AppointmentCreateRequest(
                fixture.DoctorId,
                fixture.AppointmentTypeId,
                "online",
                fixture.LocalDate,
                new TimeOnly(10, 0),
                null);
            var createdAttempt = await AttemptCreateAsync(
                configuration,
                fixture.PatientId,
                $"schedule-{Guid.NewGuid():N}",
                createRequest,
                fixture.UtcNow,
                CancellationToken.None);
            var created = Assert.IsType<AppointmentResponse>(createdAttempt.Response);
            if (paid)
            {
                await using var db = CreateContext(configuration);
                db.Payments.Add(new() { AppointmentId = created.Id, ProviderReferenceAppointmentId = created.Id, ProviderCode = "pagbank", StatusCode = "paid", Amount = created.PriceAmount, CurrencyCode = "BRL", CreatedAtUtc = fixture.UtcNow.UtcDateTime, UpdatedAtUtc = fixture.UtcNow.UtcDateTime, RowVersion = 1 });
                var appointment = await db.Appointments.SingleAsync(x => x.Id == created.Id);
                appointment.StatusCode = "confirmed";
                appointment.RowVersion++;
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                created = await new PatientSchedulingService(db, new NullAuditWriter(), new FixedTimeProvider(fixture.UtcNow))
                    .GetAppointmentAsync(fixture.PatientId, created.Id, CancellationToken.None);
            }

            var firstRescheduled = await RescheduleAsync(
                configuration,
                fixture,
                created,
                new TimeOnly(11, 0));
            var rescheduled = await RescheduleAsync(
                configuration,
                fixture,
                firstRescheduled,
                new TimeOnly(11, 30));
            Assert.Equal(created.Id, rescheduled.Id);
            Assert.Null(rescheduled.RescheduledFromAppointmentId);
            Assert.True(created.AppointmentNumber >= 100);
            Assert.Equal(created.AppointmentNumber, rescheduled.AppointmentNumber);
            Assert.Equal(paid ? "confirmed" : "pending", rescheduled.StatusCode);
            Assert.Equal(2, rescheduled.RescheduleHistory.Count);
            Assert.Equal(created.StartsAtUtc, rescheduled.RescheduleHistory[0].PreviousStartsAtUtc);
            Assert.Equal(firstRescheduled.StartsAtUtc, rescheduled.RescheduleHistory[1].PreviousStartsAtUtc);
            Assert.Equal(rescheduled.StartsAtUtc, rescheduled.RescheduleHistory[1].NewStartsAtUtc);
            if (paid)
            {
                await using var db = CreateContext(configuration);
                var payment = await db.Payments.SingleAsync(x => x.AppointmentId == rescheduled.Id);
                Assert.Equal(created.Id, payment.ProviderReferenceAppointmentId);
                Assert.Equal("paid", payment.StatusCode);
                Assert.Equal(created.PriceAmount, payment.Amount);
            }

            var canceled = await CancelAsync(configuration, fixture, rescheduled);
            Assert.Equal("canceled", canceled.StatusCode);
            Assert.Equal("Mudança de disponibilidade", canceled.CancellationReason);

            await using var verification = CreateContext(configuration);
            var original = await verification.Appointments.AsNoTracking().SingleAsync(item => item.Id == created.Id);
            Assert.Equal("canceled", original.StatusCode);
            Assert.False(await verification.Appointments.AnyAsync(item => item.RescheduledFromAppointmentId == created.Id));
            Assert.Equal(2, await verification.AppointmentRescheduleHistories.CountAsync(item => item.AppointmentId == created.Id));
            Assert.Equal(2, await verification.AppointmentStatusHistories.CountAsync(item => item.AppointmentId == created.Id));
        }
        finally
        {
            await DeleteFixtureAsync(configuration, fixture);
        }
    }

    [Fact]
    public async Task ConcurrentRequests_ReserveTheSlotOnlyOnce_AndReplayIsIdempotent()
    {
        var configuration = LoadConfiguration();
        await DeleteStaleFixturesAsync(configuration);
        var fixture = await CreateFixtureAsync(configuration);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var request = new AppointmentCreateRequest(
                fixture.DoctorId,
                fixture.AppointmentTypeId,
                "online",
                fixture.LocalDate,
                new TimeOnly(10, 0),
                "Teste concorrente");
            var firstKey = $"schedule-{Guid.NewGuid():N}";
            var secondKey = $"schedule-{Guid.NewGuid():N}";
            var attempts = await Task.WhenAll(
                AttemptCreateAsync(configuration, fixture.PatientId, firstKey, request, fixture.UtcNow, timeout.Token),
                AttemptCreateAsync(configuration, fixture.PatientId, secondKey, request, fixture.UtcNow, timeout.Token));

            var success = Assert.Single(attempts, item => item.Response is not null);
            var rejection = Assert.Single(attempts, item => item.Error is not null);
            Assert.Contains("não está mais disponível", rejection.Error, StringComparison.OrdinalIgnoreCase);

            var replay = await AttemptCreateAsync(
                configuration,
                fixture.PatientId,
                success.Key,
                request,
                fixture.UtcNow,
                timeout.Token);
            Assert.Null(replay.Error);
            Assert.True(replay.Replayed);
            Assert.Equal(success.Response!.Id, replay.Response!.Id);
            Assert.Equal(success.Response.AppointmentNumber, replay.Response.AppointmentNumber);
            Assert.True(success.Response.AppointmentNumber >= 100);

            await using var verification = CreateContext(configuration);
            Assert.Equal(1, await verification.Appointments.CountAsync(
                item => item.PatientAccountId == fixture.PatientId
                    && item.DoctorAccountId == fixture.DoctorId
                    && item.StartsAtUtc == success.Response.StartsAtUtc,
                timeout.Token));
            Assert.Equal(1, await verification.AppointmentStatusHistories.CountAsync(
                item => item.AppointmentId == success.Response.Id && item.ToStatusCode == "pending",
                timeout.Token));
        }
        finally
        {
            await DeleteFixtureAsync(configuration, fixture);
        }
    }

    private static async Task<Attempt> AttemptCreateAsync(
        IConfiguration configuration,
        ulong patientId,
        string key,
        AppointmentCreateRequest request,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var database = CreateContext(configuration);
        var service = new PatientSchedulingService(database, new NullAuditWriter(), new FixedTimeProvider(utcNow));
        try
        {
            var result = await service.CreateAsync(patientId, key, request, cancellationToken);
            return new Attempt(key, result.Response, result.Replayed, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new Attempt(key, null, false, exception.Message);
        }
    }

    private static async Task<AppointmentResponse> RescheduleAsync(
        IConfiguration configuration,
        Fixture fixture,
        AppointmentResponse original,
        TimeOnly newTime)
    {
        await using var database = CreateContext(configuration);
        var service = new PatientSchedulingService(database, new NullAuditWriter(), new FixedTimeProvider(fixture.UtcNow));
        var result = await service.RescheduleAsync(
            fixture.PatientId,
            original.Id,
            $"reschedule-{Guid.NewGuid():N}",
            new AppointmentRescheduleRequest(fixture.LocalDate, newTime, "Solicitação do paciente", original.RowVersion),
            CancellationToken.None);
        return result.Response;
    }

    private static async Task<AppointmentResponse> CancelAsync(
        IConfiguration configuration,
        Fixture fixture,
        AppointmentResponse appointment)
    {
        await using var database = CreateContext(configuration);
        var service = new PatientSchedulingService(database, new NullAuditWriter(), new FixedTimeProvider(fixture.UtcNow));
        return await service.CancelAsync(
            fixture.PatientId,
            appointment.Id,
            new AppointmentCancelRequest("Mudança de disponibilidade", appointment.RowVersion),
            CancellationToken.None);
    }

    private static async Task<Fixture> CreateFixtureAsync(IConfiguration configuration)
    {
        await using var database = CreateContext(configuration);
        var utcNow = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var localDate = new DateOnly(2026, 9, 10);
        var now = utcNow.UtcDateTime;
        var marker = Guid.NewGuid().ToString("N");
        var patient = NewAccount(ViverAppRoles.Patient, $"Paciente {marker}", $"patient-{marker}@example.test", now);
        var doctor = NewAccount(ViverAppRoles.Doctor, $"Médico {marker}", $"doctor-{marker}@example.test", now);
        database.Accounts.AddRange(patient, doctor);
        await database.SaveChangesAsync();
        doctor.DoctorProfile = new DoctorProfile
        {
            AccountId = doctor.Id,
            LicenseStateCode = "SP",
            LicenseNumber = marker[..12],
            DefaultAppointmentDurationMinutes = 30,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        var appointmentType = new AppointmentType
        {
            Name = $"Consulta {marker}",
            Description = "Tipo de atendimento exclusivo do teste.",
            ModalityCode = "online",
            DurationMinutes = 30,
            PriceAmount = 100,
            IsActive = true,
            DisplayOrder = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.AppointmentTypes.Add(appointmentType);
        database.DoctorWeeklyHours.Add(new DoctorWeeklyHour
        {
            DoctorAccountId = doctor.Id,
            DayOfWeek = (byte)localDate.DayOfWeek,
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(12),
            ValidFrom = localDate.ToDateTime(TimeOnly.MinValue),
            ValidUntil = localDate.ToDateTime(TimeOnly.MinValue),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        });
        await database.SaveChangesAsync();
        return new Fixture(patient.Id, doctor.Id, appointmentType.Id, localDate, utcNow);
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
        var appointmentIds = await database.Appointments
            .Where(item => item.PatientAccountId == fixture.PatientId || item.DoctorAccountId == fixture.DoctorId)
            .Select(item => item.Id)
            .ToArrayAsync();
        await database.IdempotencyRecords.Where(item => item.ScopeCode.EndsWith($":{fixture.PatientId}"))
            .ExecuteDeleteAsync();
        await database.Payments.Where(item => appointmentIds.Contains(item.AppointmentId)).ExecuteDeleteAsync();
        await database.AppointmentStatusHistories.Where(item => appointmentIds.Contains(item.AppointmentId))
            .ExecuteDeleteAsync();
        await database.Appointments
            .Where(item => appointmentIds.Contains(item.Id) && item.RescheduledFromAppointmentId != null)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.RescheduledFromAppointmentId, (ulong?)null));
        await database.Appointments.Where(item => appointmentIds.Contains(item.Id)).ExecuteDeleteAsync();
        await database.DoctorWeeklyHours.Where(item => item.DoctorAccountId == fixture.DoctorId).ExecuteDeleteAsync();
        await database.DoctorProfiles.Where(item => item.AccountId == fixture.DoctorId).ExecuteDeleteAsync();
        await database.AppointmentTypes.Where(item => item.Id == fixture.AppointmentTypeId).ExecuteDeleteAsync();
        await database.Accounts.Where(item => item.Id == fixture.PatientId || item.Id == fixture.DoctorId).ExecuteDeleteAsync();
    }

    private static async Task DeleteStaleFixturesAsync(IConfiguration configuration)
    {
        await using var database = CreateContext(configuration);
        var accountIds = await database.Accounts
            .Where(item => item.Email != null
                && (item.Email.StartsWith("patient-") || item.Email.StartsWith("doctor-"))
                && item.Email.EndsWith("@example.test")
                && (item.FullName.StartsWith("Paciente ") || item.FullName.StartsWith("Médico ")))
            .Select(item => item.Id)
            .ToArrayAsync();
        if (accountIds.Length == 0)
        {
            return;
        }

        var appointmentIds = await database.Appointments
            .Where(item => accountIds.Contains(item.PatientAccountId) || accountIds.Contains(item.DoctorAccountId))
            .Select(item => item.Id)
            .ToArrayAsync();
        var scopes = accountIds
            .SelectMany(id => new[] { $"appointment.create:{id}", $"appointment.move:{id}" })
            .ToArray();
        await database.IdempotencyRecords
            .Where(item => scopes.Contains(item.ScopeCode))
            .ExecuteDeleteAsync();
        await database.AppointmentStatusHistories.Where(item => appointmentIds.Contains(item.AppointmentId))
            .ExecuteDeleteAsync();
        await database.Appointments
            .Where(item => appointmentIds.Contains(item.Id) && item.RescheduledFromAppointmentId != null)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.RescheduledFromAppointmentId, (ulong?)null));
        await database.Appointments.Where(item => appointmentIds.Contains(item.Id)).ExecuteDeleteAsync();
        await database.DoctorWeeklyHours.Where(item => accountIds.Contains(item.DoctorAccountId)).ExecuteDeleteAsync();
        await database.DoctorProfiles.Where(item => accountIds.Contains(item.AccountId)).ExecuteDeleteAsync();
        await database.AppointmentTypes
            .Where(item => item.Description == "Tipo de atendimento exclusivo do teste.")
            .ExecuteDeleteAsync();
        await database.Accounts.Where(item => accountIds.Contains(item.Id)).ExecuteDeleteAsync();
    }

    private static IConfiguration LoadConfiguration() => new ConfigurationBuilder()
        .AddUserSecrets(typeof(PatientSchedulingConcurrencyTests).Assembly, optional: false)
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

        return new ViverAppDbContext(new DbContextOptionsBuilder<ViverAppDbContext>()
            .UseMySQL(builder.ConnectionString)
            .Options);
    }

    private sealed record Fixture(
        ulong PatientId,
        ulong DoctorId,
        uint AppointmentTypeId,
        DateOnly LocalDate,
        DateTimeOffset UtcNow);

    private sealed record Attempt(string Key, AppointmentResponse? Response, bool Replayed, string? Error);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class NullAuditWriter : IPatientSchedulingAuditWriter
    {
        public Task WriteAsync(
            string eventCode,
            ulong actorAccountId,
            ulong appointmentId,
            IReadOnlyDictionary<string, string>? safeData,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
