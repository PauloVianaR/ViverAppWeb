using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.Calendar;
using ViverApp.Api.Features.ClinicAdministration;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Features.Notifications;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.PatientScheduling.Tests;

public sealed class PatientSchedulingConcurrencyTests
{
    [Fact]
    public async Task ReminderJob_QueuesOneEmailIdempotently_WithoutContactingProvider()
    {
        var configuration = LoadConfiguration();
        var fixture = await CreateFixtureAsync(configuration, free: true);
        ulong appointmentId = 0;
        try
        {
            var request = new AppointmentCreateRequest(fixture.DoctorId, fixture.AppointmentTypeId,
                "online", fixture.LocalDate, new TimeOnly(10, 0), null);
            var attempt = await AttemptCreateAsync(configuration, fixture.PatientId,
                $"reminder-{Guid.NewGuid():N}", request, fixture.UtcNow, CancellationToken.None);
            var appointment = Assert.IsType<AppointmentResponse>(attempt.Response);
            appointmentId = appointment.Id;
            await using (var database = CreateContext(configuration))
            {
                var patient = await database.Accounts.SingleAsync(item => item.Id == fixture.PatientId);
                patient.PortalAccessEnabled = true;
                patient.RowVersion++;
                database.AccountConsents.Add(new AccountConsent
                {
                    AccountId = fixture.PatientId, TermsVersion = "test",
                    PrivacyVersion = "test", SourceCode = "local",
                    AcceptedAtUtc = fixture.UtcNow.UtcDateTime,
                });
                database.ScheduledJobs.Add(new ScheduledJob
                {
                    JobKey = $"appointment_reminder:{appointment.Id}:{appointment.StartsAtUtc.Ticks}",
                    JobTypeCode = "appointment_reminder", AppointmentId = appointment.Id,
                    DueAtUtc = fixture.UtcNow.UtcDateTime, StatusCode = "pending",
                    AttemptCount = 0, MaxAttempts = 5,
                    NextAttemptAtUtc = fixture.UtcNow.UtcDateTime,
                    CreatedAtUtc = fixture.UtcNow.UtcDateTime,
                });
                await database.SaveChangesAsync();
            }
            ulong jobId;
            await using (var database = CreateContext(configuration))
            {
                var scheduler = new ReminderScheduler(database, new FixedTimeProvider(fixture.UtcNow));
                var claimed = await scheduler.ClaimAsync("scheduler-test", CancellationToken.None);
                jobId = Assert.IsType<ScheduledJob>(claimed).Id;
                await scheduler.ProcessAsync(jobId, "scheduler-test", CancellationToken.None);
                await scheduler.ProcessAsync(jobId, "scheduler-test", CancellationToken.None);
            }
            await using (var database = CreateContext(configuration))
            {
                var job = await database.ScheduledJobs.AsNoTracking().SingleAsync(item => item.Id == jobId);
                Assert.Equal("succeeded", job.StatusCode);
                var messages = await database.OutboxMessages.AsNoTracking()
                    .Where(item => item.AccountId == fixture.PatientId
                        && item.TemplateKey == "appointment.reminder").ToArrayAsync();
                Assert.Single(messages);
                Assert.Equal("email", messages[0].ChannelCode);
                Assert.DoesNotContain("@example.test", messages[0].PayloadJson, StringComparison.Ordinal);
            }
        }
        finally
        {
            await using (var database = CreateContext(configuration))
            {
                await database.OutboxMessages.Where(item => item.AccountId == fixture.PatientId)
                    .ExecuteDeleteAsync();
                if (appointmentId != 0)
                    await database.ScheduledJobs.Where(item => item.AppointmentId == appointmentId)
                        .ExecuteDeleteAsync();
            }
            await DeleteFixtureAsync(configuration, fixture);
        }
    }

    [Fact]
    public async Task VariableMode_UsesOnlyIntervalsRegisteredForTheDate()
    {
        var configuration = LoadConfiguration();
        var fixture = await CreateFixtureAsync(configuration);
        try
        {
            await using var database = CreateContext(configuration);
            var now = fixture.UtcNow.UtcDateTime;
            var preference = await database.ProfessionalPreferences.SingleOrDefaultAsync(x => x.ProfessionalAccountId == fixture.DoctorId);
            if (preference is null)
            {
                preference = new ProfessionalPreference
                {
                    ProfessionalAccountId = fixture.DoctorId, EmailEnabled = true, SmsEnabled = true,
                    OnlineEnabled = true, MaxOnlineDaily = 8, MaxInPersonDaily = 16,
                    AvailabilityMode = "variable", UpdatedAtUtc = now, RowVersion = 1,
                };
                database.ProfessionalPreferences.Add(preference);
            }
            else { preference.AvailabilityMode = "variable"; preference.RowVersion++; }
            database.ProfessionalVariableHours.Add(new ProfessionalVariableHour
            {
                ProfessionalAccountId = fixture.DoctorId,
                AvailableDate = fixture.LocalDate.ToDateTime(TimeOnly.MinValue),
                StartTime = TimeSpan.FromHours(13), EndTime = TimeSpan.FromHours(14),
                ModalityCode = "online", CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1,
            });
            await database.SaveChangesAsync();
            var service = new PatientSchedulingService(database, new NullAuditWriter(), new FixedTimeProvider(fixture.UtcNow));
            var slots = await service.GetAvailableSlotsAsync(fixture.PatientId, fixture.DoctorId,
                fixture.AppointmentTypeId, "online", fixture.LocalDate, 1, CancellationToken.None);
            Assert.Contains(slots, x => x.StartsAt == new TimeOnly(13, 0));
            Assert.DoesNotContain(slots, x => x.StartsAt == new TimeOnly(10, 0));
            var availableDates = await service.GetAvailableDatesAsync(fixture.PatientId, fixture.DoctorId,
                fixture.AppointmentTypeId, "online", fixture.LocalDate, 3, CancellationToken.None);
            Assert.Equal([fixture.LocalDate], availableDates);
        }
        finally
        {
            await DeleteFixtureAsync(configuration, fixture);
        }
    }

    [Fact]
    public async Task ProfessionalSelection_RequiresActiveAppointmentTypeLink()
    {
        var configuration = LoadConfiguration();
        var fixture = await CreateFixtureAsync(configuration);
        try
        {
            await using (var database = CreateContext(configuration))
            {
                await database.ProfessionalWeeklyHours
                    .Where(item => item.ProfessionalAccountId == fixture.DoctorId)
                    .ExecuteDeleteAsync();
                var service = new PatientSchedulingService(database, new NullAuditWriter(), new FixedTimeProvider(fixture.UtcNow));
                var linked = await service.SearchProfessionalsAsync(1, 20, null, null,
                    fixture.AppointmentTypeId, "online", CancellationToken.None);
                Assert.Contains(linked.Items, item => item.AccountId == fixture.DoctorId);

                var link = await database.ProfessionalServices.SingleAsync(item =>
                    item.ProfessionalAccountId == fixture.DoctorId
                    && item.AppointmentTypeId == fixture.AppointmentTypeId);
                link.IsActive = false;
                link.RowVersion++;
                link.UpdatedAtUtc = fixture.UtcNow.UtcDateTime;
                await database.SaveChangesAsync();
            }

            await using (var verification = CreateContext(configuration))
            {
                var service = new PatientSchedulingService(verification, new NullAuditWriter(), new FixedTimeProvider(fixture.UtcNow));
                var unlinked = await service.SearchProfessionalsAsync(1, 20, null, null,
                    fixture.AppointmentTypeId, "online", CancellationToken.None);
                Assert.DoesNotContain(unlinked.Items, item => item.AccountId == fixture.DoctorId);
            }

            var attempt = await AttemptCreateAsync(configuration, fixture.PatientId,
                $"schedule-{Guid.NewGuid():N}",
                new AppointmentCreateRequest(fixture.DoctorId, fixture.AppointmentTypeId,
                    "online", fixture.LocalDate, new TimeOnly(10, 0), null),
                fixture.UtcNow,
                CancellationToken.None);
            Assert.Null(attempt.Response);
            Assert.Contains("não está mais disponível", attempt.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await DeleteFixtureAsync(configuration, fixture);
        }
    }

    [Fact]
    public async Task PsychologistFreeAppointment_IsConfirmedWithoutPaymentAndCalendarRespectsOwnership()
    {
        var configuration = LoadConfiguration();
        var fixture = await CreateFixtureAsync(configuration, free: true, professionalRole: ViverAppRoles.Psychologist);
        try
        {
            var request = new AppointmentCreateRequest(fixture.DoctorId, fixture.AppointmentTypeId,
                "online", fixture.LocalDate, new TimeOnly(10, 0), null);
            var result = await AttemptCreateAsync(configuration, fixture.PatientId,
                $"schedule-{Guid.NewGuid():N}", request, fixture.UtcNow, CancellationToken.None);
            var created = Assert.IsType<AppointmentResponse>(result.Response);
            Assert.Equal("confirmed", created.StatusCode);
            Assert.False(created.RequiresPayment);
            Assert.Equal(0, created.PriceAmount);

            await using var database = CreateContext(configuration);
            Assert.False(await database.Payments.AnyAsync(item => item.AppointmentId == created.Id));
            var calendar = new CalendarService(database);
            var patientDay = await calendar.GetAsync(fixture.PatientId, ViverAppRoles.Patient,
                "day", fixture.LocalDate, null, CancellationToken.None);
            Assert.Contains(patientDay.Items, item => item.Id == created.Id);
            var professionalYear = await calendar.GetAsync(fixture.DoctorId, ViverAppRoles.Psychologist,
                "year", fixture.LocalDate, null, CancellationToken.None);
            Assert.Contains(professionalYear.Days, item => item.Date == fixture.LocalDate && item.Count >= 1);
            var crossProfessional = await Assert.ThrowsAsync<CalendarRuleException>(() =>
                calendar.GetAsync(fixture.DoctorId, ViverAppRoles.Psychologist, "day",
                    fixture.LocalDate, fixture.PatientId, CancellationToken.None));
            Assert.Equal(403, crossProfessional.StatusCode);
        }
        finally
        {
            await DeleteFixtureAsync(configuration, fixture);
        }
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("canceled")]
    [InlineData("no_show")]
    public async Task Calendar_OmitsCompletedCanceledAndNoShowAppointmentsInEveryView(string status)
    {
        var configuration = LoadConfiguration();
        var fixture = await CreateFixtureAsync(configuration, free: true, professionalRole: ViverAppRoles.Psychologist);
        try
        {
            var attempt = await AttemptCreateAsync(configuration, fixture.PatientId,
                $"calendar-{Guid.NewGuid():N}",
                new AppointmentCreateRequest(fixture.DoctorId, fixture.AppointmentTypeId,
                    "online", fixture.LocalDate, new TimeOnly(10, 0), null),
                fixture.UtcNow, CancellationToken.None);
            var created = Assert.IsType<AppointmentResponse>(attempt.Response);

            await using var database = CreateContext(configuration);
            var preference = await database.ProfessionalPreferences.SingleOrDefaultAsync(x => x.ProfessionalAccountId == fixture.DoctorId);
            if (preference is null)
            {
                database.ProfessionalPreferences.Add(new ProfessionalPreference
                {
                    ProfessionalAccountId = fixture.DoctorId, EmailEnabled = true, SmsEnabled = true,
                    OnlineEnabled = true, MaxOnlineDaily = 8, MaxInPersonDaily = 16,
                    AvailabilityMode = "recurring", UpdatedAtUtc = fixture.UtcNow.UtcDateTime, RowVersion = 1,
                });
                await database.SaveChangesAsync();
            }
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, fixture.DoctorId.ToString()),
                     new Claim(ClaimTypes.Role, ViverAppRoles.Psychologist)], "Test")),
            };
            var audit = new IdentityAuditWriter(database, new HttpContextAccessor { HttpContext = http },
                IdentitySecurityOptions.Load(configuration));
            var availability = new ProfessionalAvailabilityPlanController(database, audit,
                new FixedTimeProvider(fixture.UtcNow))
            {
                ControllerContext = new ControllerContext { HttpContext = http },
            };
            async Task<VariableAvailabilityPlan> PlanAsync()
            {
                var response = await availability.Get(fixture.DoctorId, fixture.LocalDate,
                    fixture.LocalDate, CancellationToken.None);
                return Assert.IsType<VariableAvailabilityPlan>(Assert.IsType<OkObjectResult>(response.Result).Value);
            }
            Assert.Contains(fixture.LocalDate, (await PlanAsync()).BookedDates);
            var appointment = await database.Appointments.SingleAsync(x => x.Id == created.Id);
            appointment.StatusCode = status;
            if (status == "completed")
            {
                appointment.CompletedByAccountId = fixture.DoctorId;
                appointment.CompletedAtUtc = fixture.UtcNow.UtcDateTime;
            }
            else if (status == "no_show")
            {
                appointment.NoShowRecordedByAccountId = fixture.DoctorId;
                appointment.NoShowRecordedAtUtc = fixture.UtcNow.UtcDateTime;
            }
            else
            {
                appointment.CanceledByAccountId = fixture.PatientId;
                appointment.CanceledAtUtc = fixture.UtcNow.UtcDateTime;
                appointment.CancellationReason = "Cancelamento sintético do teste de agenda";
            }
            appointment.RowVersion++;
            await database.SaveChangesAsync();
            Assert.DoesNotContain(fixture.LocalDate, (await PlanAsync()).BookedDates);
            var calendar = new CalendarService(database);
            foreach (var view in new[] { "day", "week", "month", "year" })
            {
                foreach (var role in new[] { ViverAppRoles.Patient, ViverAppRoles.Psychologist,
                    ViverAppRoles.Manager, ViverAppRoles.Administrator })
                {
                    var actor = role == ViverAppRoles.Patient ? fixture.PatientId : fixture.DoctorId;
                    var filter = role is ViverAppRoles.Manager or ViverAppRoles.Administrator
                        ? fixture.DoctorId : (ulong?)null;
                    var result = await calendar.GetAsync(actor, role, view, fixture.LocalDate,
                        filter, CancellationToken.None);
                    Assert.DoesNotContain(result.Items, item => item.Id == created.Id);
                    Assert.DoesNotContain(result.Days, item => item.Date == fixture.LocalDate && item.Count > 0);
                }
            }
        }
        finally
        {
            await DeleteFixtureAsync(configuration, fixture);
        }
    }

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
                db.Payments.Add(new() { AppointmentId = created.Id, AppointmentRequiresPayment = true, ProviderReferenceAppointmentId = created.Id, ProviderCode = "pagbank", StatusCode = "paid", Amount = created.PriceAmount, CurrencyCode = "BRL", CreatedAtUtc = fixture.UtcNow.UtcDateTime, UpdatedAtUtc = fixture.UtcNow.UtcDateTime, RowVersion = 1 });
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
                    && item.ProfessionalAccountId == fixture.DoctorId
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

    private static async Task<Fixture> CreateFixtureAsync(IConfiguration configuration, bool free = false,
        string professionalRole = ViverAppRoles.Doctor)
    {
        await using var database = CreateContext(configuration);
        var utcNow = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var localDate = new DateOnly(2026, 9, 10);
        var now = utcNow.UtcDateTime;
        var marker = Guid.NewGuid().ToString("N");
        var patient = NewAccount(ViverAppRoles.Patient, $"Paciente {marker}", $"patient-{marker}@example.test", now);
        var doctor = NewAccount(professionalRole, $"Médico {marker}", $"doctor-{marker}@example.test", now);
        database.Accounts.AddRange(patient, doctor);
        await database.SaveChangesAsync();
        doctor.ProfessionalProfile = new ProfessionalProfile
        {
            AccountId = doctor.Id,
            LicenseStateCode = "SP",
            LicenseTypeCode = professionalRole == ViverAppRoles.Psychologist ? "CRP" : "CRM",
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
            PriceAmount = free ? 0 : 100,
            RequiresPayment = !free,
            IsActive = true,
            DisplayOrder = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.AppointmentTypes.Add(appointmentType);
        database.ProfessionalWeeklyHours.Add(new ProfessionalWeeklyHour
        {
            ProfessionalAccountId = doctor.Id,
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
        database.ProfessionalServices.Add(new ProfessionalService
        {
            ProfessionalAccountId = doctor.Id,
            AppointmentTypeId = appointmentType.Id,
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
            .Where(item => item.PatientAccountId == fixture.PatientId || item.ProfessionalAccountId == fixture.DoctorId)
            .Select(item => item.Id)
            .ToArrayAsync();
        await database.IdempotencyRecords.Where(item => item.ScopeCode.EndsWith($":{fixture.PatientId}"))
            .ExecuteDeleteAsync();
        await database.Appointments.Where(item => appointmentIds.Contains(item.Id)).ExecuteUpdateAsync(update => update.SetProperty(item => item.CurrentPaymentId, (ulong?)null));
        await database.Payments.Where(item => appointmentIds.Contains(item.AppointmentId)).ExecuteDeleteAsync();
        await database.AppointmentStatusHistories.Where(item => appointmentIds.Contains(item.AppointmentId))
            .ExecuteDeleteAsync();
        await database.Appointments
            .Where(item => appointmentIds.Contains(item.Id) && item.RescheduledFromAppointmentId != null)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.RescheduledFromAppointmentId, (ulong?)null));
        await database.Appointments.Where(item => appointmentIds.Contains(item.Id)).ExecuteDeleteAsync();
        await database.ProfessionalWeeklyHours.Where(item => item.ProfessionalAccountId == fixture.DoctorId).ExecuteDeleteAsync();
        await database.ProfessionalProfiles.Where(item => item.AccountId == fixture.DoctorId).ExecuteDeleteAsync();
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
            .Where(item => accountIds.Contains(item.PatientAccountId) || accountIds.Contains(item.ProfessionalAccountId))
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
        await database.ProfessionalWeeklyHours.Where(item => accountIds.Contains(item.ProfessionalAccountId)).ExecuteDeleteAsync();
        await database.ProfessionalProfiles.Where(item => accountIds.Contains(item.AccountId)).ExecuteDeleteAsync();
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
