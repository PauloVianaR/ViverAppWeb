using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.ArrivalExperience;

public sealed record ArrivalRequest(ulong RowVersion);
public sealed record ArrivalResponse(ulong AppointmentId, ulong AppointmentNumber, string StatusCode,
    DateTime? ArrivedAtUtc, DateOnly? BusinessDate, uint? QueueNumber, ulong RowVersion);
public sealed record StartAppointmentRequest(ulong RowVersion);
public sealed record DoctorNotificationResponse(ulong Id, ulong AppointmentId, ulong AppointmentNumber,
    uint? QueueNumber, DateTime? StartsAtUtc, bool IsRead, DateTime CreatedAtUtc, ulong RowVersion);
public sealed record DoctorNotificationsResponse(int UnreadCount, bool PopupEnabled, bool SoundEnabled,
    int SoundVolume, string SoundKey, bool MarkReadOnOpen, IReadOnlyList<DoctorNotificationResponse> Items);
public sealed record ArrivalRealtimeMessage(ulong Id, ulong AppointmentId, ulong AppointmentNumber,
    uint QueueNumber, DateTime StartsAtUtc, bool IsRead, DateTime CreatedAtUtc, ulong RowVersion,
    bool PopupEnabled, bool SoundEnabled, int SoundVolume, string SoundKey);

[Authorize(Policy = ViverAppPolicies.Doctor)]
public sealed class DoctorNotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!ulong.TryParse(value, CultureInfo.InvariantCulture, out var doctor))
        {
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(doctor));
        await base.OnConnectedAsync();
    }

    internal static string Group(ulong doctor) => $"doctor:{doctor.ToString(CultureInfo.InvariantCulture)}";
}

public sealed class ArrivalExperienceService(ViverAppDbContext database, TimeProvider clock,
    IClinicalOperationsAuditWriter audit, IHubContext<DoctorNotificationsHub> hub,
    ILogger<ArrivalExperienceService> logger)
{
    public async Task<ArrivalResponse> RegisterAsync(ulong actor, ulong appointmentId, ArrivalRequest request, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var appointment = await database.Appointments
            .FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ArrivalRuleException(404, "Atendimento não encontrado.");

        if (appointment.ArrivedAtUtc.HasValue && appointment.ArrivalBusinessDate.HasValue && appointment.ArrivalQueueNumber.HasValue)
        {
            await transaction.CommitAsync(ct);
            return MapArrival(appointment);
        }
        if (appointment.RowVersion != request.RowVersion)
            throw new ArrivalRuleException(409, "O atendimento foi alterado por outra sessão.");
        if (appointment.ModalityCode != "in_person" || appointment.StatusCode != "confirmed")
            throw new ArrivalRuleException(409, "Somente um atendimento presencial confirmado pode registrar chegada.");

        var settings = await LoadSettingsAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (now < appointment.StartsAtUtc.AddMinutes(-settings.EarlyMinutes) || now > appointment.StartsAtUtc.AddMinutes(settings.LateMinutes))
            throw new ArrivalRuleException(409, "A chegada está fora da janela operacional configurada.");
        var timezone = await TimezoneAsync(ct);
        var localDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), timezone).Date;

        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO arrival_queue_sequences (business_date, next_value, updated_at_utc)
            VALUES ({localDate}, 100, {now})
            ON DUPLICATE KEY UPDATE updated_at_utc = VALUES(updated_at_utc)
            """, ct);
        var sequence = await database.ArrivalQueueSequences
            .FromSqlInterpolated($"SELECT * FROM arrival_queue_sequences WHERE business_date = {localDate} FOR UPDATE")
            .SingleAsync(ct);
        var queueNumber = sequence.NextValue;
        sequence.NextValue++;
        sequence.UpdatedAtUtc = now;

        appointment.ArrivedAtUtc = now;
        appointment.ArrivalBusinessDate = localDate;
        appointment.ArrivalQueueNumber = queueNumber;
        appointment.ArrivalRecordedByAccountId = actor;
        appointment.StatusCode = "arrived";
        appointment.UpdatedAtUtc = now;
        appointment.RowVersion++;
        database.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            ActorAccountId = actor,
            FromStatusCode = "confirmed",
            ToStatusCode = "arrived",
            Reason = "Chegada registrada na clínica",
            StartsAtUtc = appointment.StartsAtUtc,
            EndsAtUtc = appointment.EndsAtUtc,
            OccurredAtUtc = now
        });

        DoctorNotification? notification = null;
        if (settings.NotificationsEnabled)
        {
            notification = new DoctorNotification
            {
                DoctorAccountId = appointment.DoctorAccountId,
                AppointmentId = appointment.Id,
                SourceKey = $"arrival:{appointment.Id.ToString(CultureInfo.InvariantCulture)}",
                TypeCode = "patient_arrived",
                CreatedAtUtc = now,
                RowVersion = 1
            };
            database.DoctorNotifications.Add(notification);
        }
        await database.SaveChangesAsync(ct);
        await audit.WriteAsync("appointment.arrival_recorded", actor, "appointment", appointment.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["appointmentNumber"] = appointment.AppointmentNumber.ToString(CultureInfo.InvariantCulture), ["queueNumber"] = queueNumber.ToString(CultureInfo.InvariantCulture) }, ct);
        await transaction.CommitAsync(ct);

        if (notification is not null)
        {
            try
            {
                await hub.Clients.Group(DoctorNotificationsHub.Group(appointment.DoctorAccountId)).SendAsync("PatientArrived",
                    new ArrivalRealtimeMessage(notification.Id, appointment.Id, appointment.AppointmentNumber, queueNumber, appointment.StartsAtUtc, false, now, notification.RowVersion,
                        settings.PopupEnabled, settings.SoundEnabled, settings.SoundVolume, settings.SoundKey), CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "A chegada foi persistida, mas a entrega SignalR será recuperada pela caixa durável.");
            }
        }
        return MapArrival(appointment);
    }

    public async Task<ArrivalResponse> StartAsync(ulong doctor, ulong appointmentId, StartAppointmentRequest request, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var appointment = await database.Appointments
            .FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ArrivalRuleException(404, "Atendimento não encontrado.");
        if (appointment.DoctorAccountId != doctor) throw new ArrivalRuleException(404, "Atendimento não encontrado.");
        if (appointment.StatusCode == "in_progress") { await transaction.CommitAsync(ct); return MapArrival(appointment); }
        if (appointment.RowVersion != request.RowVersion) throw new ArrivalRuleException(409, "O atendimento foi alterado por outra sessão.");
        var valid = appointment.ModalityCode == "in_person" ? appointment.StatusCode == "arrived" : appointment.StatusCode == "confirmed";
        if (!valid) throw new ArrivalRuleException(409, "O atendimento ainda não pode ser iniciado.");
        var now = clock.GetUtcNow().UtcDateTime;
        var previous = appointment.StatusCode;
        appointment.StatusCode = "in_progress"; appointment.UpdatedAtUtc = now; appointment.RowVersion++;
        database.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            ActorAccountId = doctor,
            FromStatusCode = previous,
            ToStatusCode = "in_progress",
            Reason = "Atendimento iniciado pelo médico",
            StartsAtUtc = appointment.StartsAtUtc,
            EndsAtUtc = appointment.EndsAtUtc,
            OccurredAtUtc = now
        });
        await database.DoctorNotifications.Where(x => x.DoctorAccountId == doctor && x.AppointmentId == appointmentId && x.ReadAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.ReadAtUtc, now).SetProperty(n => n.RowVersion, n => n.RowVersion + 1), ct);
        await database.SaveChangesAsync(ct);
        await audit.WriteAsync("appointment.started", doctor, "appointment", appointment.Id.ToString(CultureInfo.InvariantCulture), null, ct);
        await transaction.CommitAsync(ct);
        return MapArrival(appointment);
    }

    public async Task<DoctorNotificationsResponse> NotificationsAsync(ulong doctor, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 50) throw new ArrivalRuleException(400, "Paginação inválida.");
        var settings = await LoadSettingsAsync(ct);
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-settings.RetentionDays);
        await database.DoctorNotifications.Where(x => x.DoctorAccountId == doctor && x.ReadAtUtc != null && x.CreatedAtUtc < cutoff).ExecuteDeleteAsync(ct);
        var unread = await database.DoctorNotifications.CountAsync(x => x.DoctorAccountId == doctor && x.ReadAtUtc == null, ct);
        var rows = await database.DoctorNotifications.AsNoTracking().Where(x => x.DoctorAccountId == doctor)
            .Include(x => x.Appointment).OrderByDescending(x => x.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(unread, settings.PopupEnabled, settings.SoundEnabled, settings.SoundVolume, settings.SoundKey,
            settings.MarkReadOnOpen, rows.Select(x => new DoctorNotificationResponse(x.Id, x.AppointmentId,
                x.Appointment.AppointmentNumber, x.Appointment.ArrivalQueueNumber, x.Appointment.StartsAtUtc,
                x.ReadAtUtc.HasValue, x.CreatedAtUtc, x.RowVersion)).ToArray());
    }

    public async Task ReadAsync(ulong doctor, ulong id, ulong rowVersion, CancellationToken ct)
    {
        var item = await database.DoctorNotifications.SingleOrDefaultAsync(x => x.Id == id && x.DoctorAccountId == doctor, ct)
            ?? throw new ArrivalRuleException(404, "Notificação não encontrada.");
        if (item.RowVersion != rowVersion) throw new ArrivalRuleException(409, "A notificação foi alterada por outra sessão.");
        if (!item.ReadAtUtc.HasValue) { item.ReadAtUtc = clock.GetUtcNow().UtcDateTime; item.RowVersion++; await database.SaveChangesAsync(ct); }
    }

    public Task ReadAllAsync(ulong doctor, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return database.DoctorNotifications.Where(x => x.DoctorAccountId == doctor && x.ReadAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.ReadAtUtc, now).SetProperty(n => n.RowVersion, n => n.RowVersion + 1), ct);
    }

    private static ArrivalResponse MapArrival(Appointment item) => new(item.Id, item.AppointmentNumber, item.StatusCode,
        item.ArrivedAtUtc, item.ArrivalBusinessDate is { } date ? DateOnly.FromDateTime(date) : null,
        item.ArrivalQueueNumber, item.RowVersion);

    private async Task<ArrivalSettings> LoadSettingsAsync(CancellationToken ct)
    {
        var values = await database.ApplicationSettings.AsNoTracking().Where(x => x.SettingKey.StartsWith("appointments.arrival_"))
            .ToDictionaryAsync(x => x.SettingKey, x => x.ValueJson, ct);
        return new(ReadBool(values, "appointments.arrival_notifications_enabled", true), ReadBool(values, "appointments.arrival_popup_enabled", true),
            ReadBool(values, "appointments.arrival_sound_enabled", true), ReadInt(values, "appointments.arrival_sound_volume", 60),
            ReadString(values, "appointments.arrival_sound_key", "soft_chime"), ReadInt(values, "appointments.arrival_early_minutes", 120),
            ReadInt(values, "appointments.arrival_late_minutes", 30), ReadInt(values, "appointments.arrival_notification_retention_days", 30),
            ReadBool(values, "appointments.arrival_mark_read_on_open", true));
    }

    private async Task<TimeZoneInfo> TimezoneAsync(CancellationToken ct)
    {
        var name = await database.Clinics.AsNoTracking().Select(x => x.TimezoneName).SingleAsync(ct);
        try { return TimeZoneInfo.FindSystemTimeZoneById(name); }
        catch (TimeZoneNotFoundException) { throw new ArrivalRuleException(503, "Fuso horário da clínica indisponível."); }
    }
    private static bool ReadBool(IReadOnlyDictionary<string, string> values, string key, bool fallback) => values.TryGetValue(key, out var json) && bool.TryParse(json, out var value) ? value : fallback;
    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback) => values.TryGetValue(key, out var json) && int.TryParse(json, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static string ReadString(IReadOnlyDictionary<string, string> values, string key, string fallback) { if (!values.TryGetValue(key, out var json)) return fallback; try { return JsonSerializer.Deserialize<string>(json) ?? fallback; } catch (JsonException) { return fallback; } }
    private sealed record ArrivalSettings(bool NotificationsEnabled, bool PopupEnabled, bool SoundEnabled, int SoundVolume,
        string SoundKey, int EarlyMinutes, int LateMinutes, int RetentionDays, bool MarkReadOnOpen);
}

public sealed class ArrivalRuleException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
