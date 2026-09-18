using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.ClinicAdministration;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.ManagerExperience;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.AdministratorExperience;

public sealed class AdministratorExperienceService(ViverAppDbContext database, ManagerExperienceService manager,
    IdentityAuditWriter audit, TimeProvider timeProvider)
{
    private static readonly HashSet<string> EditableSettings = new(StringComparer.Ordinal)
    {
        "web.maintenance_mode", "appointments.allow_clinic_payment", "appointments.online_calls_enabled",
        "appointments.booking_horizon_days", "appointments.minimum_lead_minutes", "appointments.cancellation_cutoff_hours",
        "appointments.reschedule_cutoff_hours", "appointments.slot_interval_minutes", "appointments.patient_daily_limit",
        "appointments.default_consultation_minutes", "appointments.default_examination_minutes", "appointments.default_surgery_minutes",
        "appointments.default_procedure_minutes", "manager.appointment_types_enabled", "manager.doctor_schedules_enabled",
        "doctor.patient_scheduling_enabled", "premium.manager_can_manage", "manager.medical_records_write_enabled",
        "cash.manager_can_reopen", "cash.manager_can_view_cumulative_totals",
        "appointments.interval_minutes", "communications.email_enabled", "communications.sms_enabled", "premium.manager_can_decide",
        "appointments.arrival_notifications_enabled", "appointments.arrival_popup_enabled", "appointments.arrival_sound_enabled",
        "appointments.arrival_sound_volume", "appointments.arrival_sound_key",
        "appointments.arrival_notification_retention_days", "appointments.arrival_mark_read_on_open"
    };
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<AdministratorHomeResponse> HomeAsync(ulong actor, CancellationToken ct)
    {
        await SynchronizeNotificationsAsync(actor, ct);
        var now = Now;
        var timezone = await TimezoneAsync(ct);
        var local = TimeZoneInfo.ConvertTimeFromUtc(now, timezone);
        var from = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), timezone);
        var to = from.AddDays(1);
        var activeUsers = await database.Accounts.AsNoTracking().Where(x => x.StatusCode == "active").OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct);
        var todayNumbers = await database.Appointments.AsNoTracking().Where(x => x.StartsAtUtc >= from && x.StartsAtUtc < to && x.StatusCode != "canceled" && x.InverseRescheduledFromAppointment == null).OrderBy(x => x.StartsAtUtc).Select(x => x.AppointmentNumber).ToArrayAsync(ct);
        var activePremium = await database.PremiumMemberships.AsNoTracking().Where(x => x.StatusCode == "active" && x.StartsAtUtc <= now && (x.EndsAtUtc == null || x.EndsAtUtc > now)).OrderBy(x => x.Account.FullName).Select(x => x.Account.FullName).ToArrayAsync(ct);
        var pendingApprovalNames = await database.Accounts.AsNoTracking().Where(x => x.StatusCode == "pending_approval" && (x.RoleCode == "doctor" || x.RoleCode == "manager")).OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct);
        var pendingPaymentNumbers = await database.Payments.AsNoTracking().Where(x => x.StatusCode == "pending").OrderBy(x => x.AppointmentNavigation.AppointmentNumber).Select(x => x.AppointmentNavigation.AppointmentNumber).ToArrayAsync(ct);
        var unreadNotifications = await database.AdministratorNotifications.AsNoTracking().Where(x => x.AdministratorAccountId == actor && x.ReadAtUtc == null && x.DismissedAtUtc == null).OrderByDescending(x => x.CreatedAtUtc).Select(x => x.Title).ToArrayAsync(ct);
        var counters = new AdministratorCounters(activeUsers.Length, todayNumbers.Length, activePremium.Length,
            pendingApprovalNames.Length, pendingPaymentNumbers.Length, unreadNotifications.Length);
        var sources = new AdministratorHomeSources(activeUsers, todayNumbers, activePremium, pendingApprovalNames, pendingPaymentNumbers, unreadNotifications);
        var pending = await database.Accounts.AsNoTracking().Include(x => x.DoctorProfile).ThenInclude(x => x!.DoctorSpecialties).ThenInclude(x => x.Specialty)
            .Where(x => x.StatusCode == "pending_approval" && (x.RoleCode == "doctor" || x.RoleCode == "manager"))
            .OrderBy(x => x.CreatedAtUtc).Take(8).Select(x => new AdministratorPendingProfessional(x.Id, x.FullName, x.RoleCode,
                x.Email ?? x.PhoneE164, x.DoctorProfile == null ? null : $"CRM {x.DoctorProfile.LicenseStateCode} {x.DoctorProfile.LicenseNumber}",
                x.DoctorProfile == null ? null : x.DoctorProfile.DoctorSpecialties.Where(s => s.IsPrimary).Select(s => s.Specialty.Name).FirstOrDefault(),
                x.DoctorProfile == null ? null : x.DoctorProfile.YearsExperience, x.RowVersion)).ToListAsync(ct);
        var today = (await manager.AgendaAsync(DateOnly.FromDateTime(local), DateOnly.FromDateTime(local),
            null, null, null, null, null, null, null, null, null, "date_asc", 1, 8, ct)).Page.Items;
        return new(counters, pending, sources, today);
    }

    public Task<ManagerAgendaResponse> AgendaAsync(DateOnly from, DateOnly to, string? status, string? modality, string? category,
        ulong? doctor, ulong? appointmentNumber, string? payment, string? search, string sort, int page, int size, CancellationToken ct) =>
        manager.AgendaAsync(from, to, status, modality, category, doctor, appointmentNumber, payment, null, null, search, sort, page, size, ct);
    public Task<ManagerAppointmentResponse> AppointmentAsync(ulong id, CancellationToken ct) => manager.AppointmentAsync(id, ct);
    public Task<ManagerPatientsResponse> PatientsAsync(string? search, string? status, bool? premium, int page, int size, CancellationToken ct) => manager.PatientsAsync(search, status, premium, page, size, ct);
    public Task<ManagerPatientResponse> PatientAsync(ulong id, CancellationToken ct) => manager.PatientAsync(id, ct);
    public Task<ManagerPatientResponse> UpdatePatientAsync(ulong actor, ulong id, ManagerPatientUpdateRequest request, CancellationToken ct) => manager.UpdatePatientAsync(actor, id, request, ct);
    public Task<ManagerPatientResponse> CreatePatientAsync(ulong actor, ManagerPatientCreateRequest request, CancellationToken ct) => manager.CreatePatientAsync(actor, request, ct);
    public Task<ManagerPatientResponse> ActivatePatientPremiumAsync(ulong actor, ulong id, PrivateDocument proof, CancellationToken ct) =>
        manager.ActivatePremiumAsync(actor, id, proof, true, ct);
    public Task<ManagerPatientResponse> DeactivatePatientPremiumAsync(ulong actor, ulong id, ManagerPremiumCancelRequest request, CancellationToken ct) =>
        manager.DeactivatePremiumAsync(actor, id, request, true, ct);
    public Task<ManagerPaymentResponse> ConfirmPaymentAsync(ulong actor, ulong id, string key, ManagerPaymentConfirmRequest request, CancellationToken ct) => manager.ConfirmPaymentAsync(actor, id, key, request, ct);
    public Task<IReadOnlyList<ManagerDoctorOption>> DoctorsAsync(CancellationToken ct) => manager.DoctorsAsync(ct);
    public async Task<IReadOnlyList<AdministratorDoctorAccessResponse>> DoctorAccessAsync(CancellationToken ct) => await database.DoctorPreferences.AsNoTracking()
        .Where(x => x.DoctorAccount.Account.StatusCode == "active").OrderBy(x => x.DoctorAccount.Account.FullName)
        .Select(x => new AdministratorDoctorAccessResponse(x.DoctorAccountId, x.DoctorAccount.Account.FullName, x.OnlineEnabled, x.RowVersion)).ToListAsync(ct);
    public Task<ViverApp.Api.Features.PatientScheduling.SchedulingPage<ManagerPremiumRequestResponse>> PremiumAsync(string? status, string? search, int page, int size, CancellationToken ct) => manager.PremiumAsync(status, search, page, size, ct);
    public Task<ManagerPremiumRequestResponse> DecidePremiumAsync(ulong actor, ulong id, ManagerPremiumDecisionRequest request, CancellationToken ct) => manager.DecidePremiumAsync(actor, id, request, ct, true);
    public async Task<ManagerPremiumRequestResponse> CancelPremiumAsync(ulong actor, ulong id, AdministratorPremiumCancelRequest request, CancellationToken ct)
    {
        var item = await database.PremiumMemberships.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdministratorRuleException(404, "Benefício Premium não encontrado.");
        if (item.RowVersion != request.RowVersion) throw new AdministratorRuleException(409, "A solicitação foi alterada por outra sessão.");
        if (item.StatusCode != "active") throw new AdministratorRuleException(409, "Somente um benefício ativo pode ser cancelado.");
        item.StatusCode = "canceled"; item.EndsAtUtc = Now; item.UpdatedAtUtc = Now; item.ReviewedAtUtc = Now; item.ReviewedByAccountId = actor; item.RejectionReason = request.Reason.Trim(); item.RowVersion++;
        await database.SaveChangesAsync(ct); await audit.WriteAsync("administrator.premium.canceled", actor, "premium_membership", id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string> { { "before", "active" }, { "after", "canceled" }, { "reason", request.Reason.Trim() } }, ct);
        return await manager.PremiumRequestAsync(id, ct);
    }

    public async Task ReopenProfessionalAsync(ulong actor, ulong id, AdministratorAccountStatusRequest request, CancellationToken ct)
    {
        var account = await database.Accounts.SingleOrDefaultAsync(x => x.Id == id && (x.RoleCode == "doctor" || x.RoleCode == "manager"), ct) ?? throw new AdministratorRuleException(404, "Profissional não encontrado.");
        if (account.RowVersion != request.RowVersion) throw new AdministratorRuleException(409, "O cadastro foi alterado por outra sessão.");
        if (account.StatusCode != "rejected") throw new AdministratorRuleException(409, "Somente cadastros rejeitados podem voltar para análise.");
        account.StatusCode = "pending_approval"; account.UpdatedAtUtc = Now; account.RowVersion++; await database.SaveChangesAsync(ct);
        await audit.WriteAsync("administrator.professional.reopened", actor, "account", id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string> { { "before", "rejected" }, { "after", "pending_approval" }, { "reason", request.Reason?.Trim() ?? "" } }, ct);
    }

    public async Task<AdministratorAnalyticsResponse> AnalyticsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366) throw new AdministratorRuleException(400, "O período deve ter no máximo 366 dias.");
        var timezone = await TimezoneAsync(ct); var start = ToUtc(from, timezone); var end = ToUtc(to.AddDays(1), timezone);
        var days = to.DayNumber - from.DayNumber + 1; var previousStart = start.AddDays(-days); var previousEnd = start;
        var query = database.Appointments.AsNoTracking().Where(x => x.StartsAtUtc >= start && x.StartsAtUtc < end && x.InverseRescheduledFromAppointment == null);
        var count = await query.CountAsync(ct);
        var paid = database.Payments.AsNoTracking().Where(x => x.StatusCode == "paid" && x.PaidAtUtc >= start && x.PaidAtUtc < end);
        var revenue = await paid.SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var previousRevenue = await database.Payments.Where(x => x.StatusCode == "paid" && x.PaidAtUtc >= previousStart && x.PaidAtUtc < previousEnd).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var previousAppointments = await database.Appointments.CountAsync(x => x.StartsAtUtc >= previousStart && x.StartsAtUtc < previousEnd && x.InverseRescheduledFromAppointment == null, ct);
        var satisfaction = await query.Where(x => x.AppointmentReview != null).AverageAsync(x => (double?)x.AppointmentReview!.Rating, ct);
        var monthlyRows = await paid.GroupBy(x => new { x.PaidAtUtc!.Value.Year, x.PaidAtUtc.Value.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Sum(x => x.Amount), Count = g.Count() }).OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync(ct);
        var revenueByUserTypeRows = await paid.GroupBy(x => new
        {
            x.PaidAtUtc!.Value.Year,
            x.PaidAtUtc.Value.Month,
            IsPremium = x.AppointmentNavigation.DiscountPercent > 0,
        }).Select(g => new { g.Key.Year, g.Key.Month, g.Key.IsPremium, Value = g.Sum(x => x.Amount) })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync(ct);
        var paymentEvolutionRows = await paid.GroupBy(x => new { x.PaidAtUtc!.Value.Year, x.PaidAtUtc.Value.Month, Method = x.MethodCode ?? "not_informed" })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Method, Value = g.Sum(x => x.Amount) })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync(ct);
        var paymentLocationTrendRows = await paid.GroupBy(x => new { x.PaidAtUtc!.Value.Year, x.PaidAtUtc.Value.Month, x.AppointmentNavigation.PaymentLocationCode })
            .Select(g => new { g.Key.Year, g.Key.Month, Location = g.Key.PaymentLocationCode, Count = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync(ct);
        var paymentLocationRows = await paid.GroupBy(x => x.AppointmentNavigation.PaymentLocationCode)
            .Select(g => new { Label = g.Key, Value = g.Sum(x => x.Amount), Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync(ct);
        var statusRows = await query.GroupBy(x => x.StatusCode).Select(g => new { Label = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync(ct);
        var methodRows = await paid.GroupBy(x => x.MethodCode ?? "not_informed").Select(g => new { Label = g.Key, Value = g.Sum(x => x.Amount), Count = g.Count() }).OrderByDescending(x => x.Value).ToListAsync(ct);
        var serviceRows = await query.GroupBy(x => x.AppointmentType.Name)
            .Select(g => new { Label = g.Key, Value = g.Sum(x => x.PriceAmount), Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync(ct);
        var categoryRows = await query.GroupBy(x => x.AppointmentType.CategoryCode)
            .Select(g => new { Label = g.Key, Value = g.Sum(x => x.PriceAmount), Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync(ct);
        var doctorRows = await query.GroupBy(x => x.DoctorAccount.Account.FullName).Select(g => new
        {
            Label = g.Key,
            Value = g.Where(x => x.AppointmentReview != null).Average(x => (double?)x.AppointmentReview!.Rating) ?? 0,
            Count = g.Count()
        }).OrderByDescending(x => x.Count).Take(20).ToListAsync(ct);
        var months = monthlyRows.Select(x => new AdministratorMetricPoint(new DateTime(x.Year, x.Month, 1).ToString("MMM/yyyy", CultureInfo.GetCultureInfo("pt-BR")), x.Value, x.Count)).ToArray();
        var statuses = statusRows.Select(x => new AdministratorMetricPoint(x.Label, 0, x.Count)).ToArray();
        var methods = methodRows.GroupBy(x => NormalizePaymentMethod(x.Label)).Select(g => new AdministratorMetricPoint(g.Key, g.Sum(x => x.Value), g.Sum(x => x.Count))).OrderByDescending(x => x.Value).ToArray();
        var doctors = doctorRows.Select(x => new AdministratorMetricPoint(x.Label, (decimal)x.Value, x.Count)).ToArray();
        var revenueByUserType = revenueByUserTypeRows.GroupBy(x => new { x.Year, x.Month }).Select(g => new AdministratorRevenueByUserTypePoint(
            MonthLabel(g.Key.Year, g.Key.Month), g.Where(x => !x.IsPremium).Sum(x => x.Value), g.Where(x => x.IsPremium).Sum(x => x.Value))).ToArray();
        var paymentEvolution = paymentEvolutionRows.GroupBy(x => new { x.Year, x.Month }).Select(g => new AdministratorPaymentMethodEvolutionPoint(
            MonthLabel(g.Key.Year, g.Key.Month),
            g.Where(x => NormalizePaymentMethod(x.Method) == "card").Sum(x => x.Value),
            g.Where(x => NormalizePaymentMethod(x.Method) == "pix").Sum(x => x.Value),
            g.Where(x => NormalizePaymentMethod(x.Method) == "cash").Sum(x => x.Value),
            g.Where(x => NormalizePaymentMethod(x.Method) == "bank_slip").Sum(x => x.Value))).ToArray();
        var paymentLocationTrend = paymentLocationTrendRows.GroupBy(x => new { x.Year, x.Month }).Select(g => new AdministratorPaymentLocationTrendPoint(
            MonthLabel(g.Key.Year, g.Key.Month), g.Where(x => x.Location == "web").Sum(x => x.Count), g.Where(x => x.Location == "clinic").Sum(x => x.Count))).ToArray();
        var paymentLocations = paymentLocationRows.Select(x => new AdministratorMetricPoint(x.Label, x.Value, x.Count)).ToArray();
        var services = serviceRows.Select(x => new AdministratorMetricPoint(x.Label, x.Value, x.Count)).ToArray();
        var categoryLookup = categoryRows.ToDictionary(x => x.Label, StringComparer.Ordinal);
        var categories = new[]
        {
            CategoryPoint("consultation", "Consultas"),
            CategoryPoint("examination", "Exames"),
            CategoryPoint("surgery", "Cirurgias"),
            CategoryPoint("procedure", "Procedimentos"),
        };
        return new(from, to, revenue, count, count == 0 ? 0 : revenue / count, satisfaction is null ? null : (decimal)satisfaction.Value,
            previousRevenue, previousAppointments, months, statuses, methods, doctors, revenueByUserType, paymentEvolution,
            paymentLocationTrend, paymentLocations, services, categories);

        AdministratorMetricPoint CategoryPoint(string code, string label) => categoryLookup.TryGetValue(code, out var row)
            ? new AdministratorMetricPoint(label, row.Value, row.Count)
            : new AdministratorMetricPoint(label, 0, 0);
    }

    private static string MonthLabel(int year, int month) => new DateTime(year, month, 1).ToString("MMM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
    private static string NormalizePaymentMethod(string value) => value switch
    {
        "credit_card" or "debit_card" or "card" => "card",
        "pix" => "pix",
        "cash" => "cash",
        "bank_slip" or "boleto" => "bank_slip",
        _ => "not_informed",
    };

    public async Task<IReadOnlyList<AdministratorSettingResponse>> SettingsAsync(CancellationToken ct) =>
        await database.ApplicationSettings.AsNoTracking().Where(x => !x.IsSecret && EditableSettings.Contains(x.SettingKey)).OrderBy(x => x.SettingKey)
            .Select(x => new AdministratorSettingResponse(x.SettingKey, x.ValueJson, x.Description, x.UpdatedAtUtc, x.RowVersion)).ToListAsync(ct);

    public async Task<AdministratorSettingResponse> UpdateSettingAsync(ulong actor, string key, AdministratorSettingUpdateRequest request, CancellationToken ct)
    {
        if (!EditableSettings.Contains(key)) throw new AdministratorRuleException(404, "Configuração não encontrada.");
        var setting = await database.ApplicationSettings.SingleOrDefaultAsync(x => x.SettingKey == key && !x.IsSecret, ct)
            ?? throw new AdministratorRuleException(404, "Configuração não encontrada.");
        if (setting.RowVersion != request.RowVersion) throw new AdministratorRuleException(409, "A configuração foi alterada por outra sessão.");
        try { using var json = JsonDocument.Parse(request.ValueJson); ValidateSetting(key, json.RootElement); }
        catch (JsonException) { throw new AdministratorRuleException(400, "Informe um valor JSON válido e tipado."); }
        var before = setting.ValueJson; setting.ValueJson = request.ValueJson; setting.UpdatedAtUtc = Now; setting.UpdatedByAccountId = actor; setting.RowVersion++;
        await database.SaveChangesAsync(ct);
        await audit.WriteAsync("administrator.setting.updated", actor, "application_setting", key,
            new Dictionary<string, string> { ["before"] = before, ["after"] = setting.ValueJson }, ct);
        return new(setting.SettingKey, setting.ValueJson, setting.Description, setting.UpdatedAtUtc, setting.RowVersion);
    }

    public async Task<IReadOnlyList<AdministratorPremiumPlanResponse>> PremiumPlansAsync(CancellationToken ct) => await database.PremiumPlans.AsNoTracking()
        .OrderBy(x => x.Name).Select(x => new AdministratorPremiumPlanResponse(x.Id, x.Name, x.AppointmentDiscountPercent, x.ValidityDays, x.IsActive, x.RowVersion)).ToListAsync(ct);
    public async Task<AdministratorPremiumPlanResponse> UpdatePremiumPlanAsync(ulong actor, uint id, AdministratorPremiumPlanUpdateRequest request, CancellationToken ct)
    {
        var plan = await database.PremiumPlans.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdministratorRuleException(404, "Plano Premium não encontrado.");
        if (plan.RowVersion != request.RowVersion) throw new AdministratorRuleException(409, "O plano foi alterado por outra sessão.");
        var before = $"{plan.AppointmentDiscountPercent}:{plan.ValidityDays}:{plan.IsActive}"; plan.AppointmentDiscountPercent = request.AppointmentDiscountPercent; plan.ValidityDays = request.ValidityDays; plan.IsActive = request.IsActive; plan.UpdatedAtUtc = Now; plan.RowVersion++;
        await database.SaveChangesAsync(ct); await audit.WriteAsync("administrator.premium_plan.updated", actor, "premium_plan", id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string> { { "before", before }, { "after", $"{plan.AppointmentDiscountPercent}:{plan.ValidityDays}:{plan.IsActive}" } }, ct);
        return new(plan.Id, plan.Name, plan.AppointmentDiscountPercent, plan.ValidityDays, plan.IsActive, plan.RowVersion);
    }

    public async Task<AdministratorNotificationsResponse> NotificationsAsync(ulong actor, string? type, string? severity, string? read, CancellationToken ct)
    {
        await SynchronizeNotificationsAsync(actor, ct);
        var query = database.AdministratorNotifications.AsNoTracking().Where(x => x.AdministratorAccountId == actor && x.DismissedAtUtc == null);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(x => x.TypeCode == type);
        if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(x => x.SeverityCode == severity);
        if (read == "read") query = query.Where(x => x.ReadAtUtc != null); else if (read == "unread") query = query.Where(x => x.ReadAtUtc == null);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).Take(100).Select(x => new AdministratorNotificationResponse(x.Id, x.TypeCode,
            x.SeverityCode, x.Title, x.Message, x.EntityType, x.EntityId, x.ReadAtUtc != null, x.CreatedAtUtc, x.RowVersion)).ToListAsync(ct);
        var pendingPayments = await database.Payments.AsNoTracking().Where(x => x.StatusCode == "pending").OrderBy(x => x.AppointmentNavigation.AppointmentNumber).Select(x => x.AppointmentNavigation.AppointmentNumber).ToArrayAsync(ct);
        var unread = await database.AdministratorNotifications.AsNoTracking().Where(x => x.AdministratorAccountId == actor && x.DismissedAtUtc == null && x.ReadAtUtc == null).OrderByDescending(x => x.CreatedAtUtc).Select(x => x.Title).ToArrayAsync(ct);
        var high = await database.AdministratorNotifications.AsNoTracking().Where(x => x.AdministratorAccountId == actor && x.DismissedAtUtc == null && x.SeverityCode == "high").OrderByDescending(x => x.CreatedAtUtc).Select(x => x.Title).ToArrayAsync(ct);
        var approvals = await database.Accounts.AsNoTracking().Where(x => x.StatusCode == "pending_approval" && (x.RoleCode == "doctor" || x.RoleCode == "manager")).OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct);
        var sources = new AdministratorNotificationSources(pendingPayments, unread, high, approvals);
        var counters = new AdministratorNotificationCounters(pendingPayments.Length, unread.Length, high.Length, approvals.Length);
        return new(counters, sources, items);
    }

    public async Task ReadNotificationAsync(ulong actor, ulong id, ulong version, CancellationToken ct)
    {
        var item = await database.AdministratorNotifications.SingleOrDefaultAsync(x => x.Id == id && x.AdministratorAccountId == actor && x.DismissedAtUtc == null, ct)
            ?? throw new AdministratorRuleException(404, "Notificação não encontrada.");
        if (item.RowVersion != version) throw new AdministratorRuleException(409, "A notificação foi alterada em outra sessão.");
        item.ReadAtUtc ??= Now; item.RowVersion++; await database.SaveChangesAsync(ct);
    }
    public async Task ReadAllNotificationsAsync(ulong actor, CancellationToken ct) => await database.AdministratorNotifications
        .Where(x => x.AdministratorAccountId == actor && x.DismissedAtUtc == null && x.ReadAtUtc == null)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAtUtc, Now).SetProperty(x => x.RowVersion, x => x.RowVersion + 1), ct);
    public async Task DismissNotificationAsync(ulong actor, ulong id, ulong version, CancellationToken ct)
    {
        var item = await database.AdministratorNotifications.SingleOrDefaultAsync(x => x.Id == id && x.AdministratorAccountId == actor && x.DismissedAtUtc == null, ct)
            ?? throw new AdministratorRuleException(404, "Notificação não encontrada.");
        if (item.RowVersion != version) throw new AdministratorRuleException(409, "A notificação foi alterada em outra sessão.");
        item.DismissedAtUtc = Now; item.RowVersion++; await database.SaveChangesAsync(ct);
    }

    private async Task SynchronizeNotificationsAsync(ulong actor, CancellationToken ct)
    {
        var existing = await database.AdministratorNotifications.Where(x => x.AdministratorAccountId == actor).Select(x => x.SourceKey).ToListAsync(ct);
        var known = existing.ToHashSet(StringComparer.Ordinal);
        var pendingProfessionals = await database.Accounts.AsNoTracking().Where(x => x.StatusCode == "pending_approval" && (x.RoleCode == "doctor" || x.RoleCode == "manager"))
            .Select(x => new { x.Id, x.FullName, x.RoleCode, x.CreatedAtUtc }).ToListAsync(ct);
        foreach (var item in pendingProfessionals) Add($"approval:{item.Id}", "approval_pending", "high", "Cadastro aguardando aprovação",
            $"{item.FullName} solicitou acesso como {(item.RoleCode == "doctor" ? "Médico" : "Gestor")}.", "account", item.Id.ToString(CultureInfo.InvariantCulture), item.CreatedAtUtc);
        var premium = await database.PremiumMemberships.AsNoTracking().Include(x => x.Account).Where(x => x.StatusCode == "pending").Select(x => new { x.Id, x.Account.FullName, x.CreatedAtUtc }).ToListAsync(ct);
        foreach (var item in premium) Add($"premium:{item.Id}", "premium_pending", "warning", "Solicitação Premium pendente", $"{item.FullName} enviou uma solicitação Premium.", "premium_membership", item.Id.ToString(CultureInfo.InvariantCulture), item.CreatedAtUtc);
        var payments = await database.Payments.AsNoTracking().Include(x => x.AppointmentNavigation).ThenInclude(x => x.PatientAccount).Where(x => x.StatusCode == "pending")
            .Select(x => new { x.Id, x.AppointmentId, x.AppointmentNavigation.PatientAccount.FullName, x.CreatedAtUtc }).Take(100).ToListAsync(ct);
        foreach (var item in payments) Add($"payment:{item.Id}", "payment_pending", "warning", "Pagamento pendente", $"Há um pagamento pendente para o atendimento de {item.FullName}.", "appointment", item.AppointmentId.ToString(CultureInfo.InvariantCulture), item.CreatedAtUtc);
        var operationalEvents = await database.AuditEvents.AsNoTracking()
            .Where(x => x.OccurredAtUtc >= Now.AddDays(-90) &&
                (x.EventCode == "appointment.canceled_by_manager" || x.EventCode == "appointment.canceled_by_doctor" ||
                 x.EventCode == "appointment.rescheduled_by_manager" || x.EventCode == "appointment.rescheduled_by_doctor" ||
                 x.EventCode == "manager.payment.confirmed" || x.EventCode == "administrator.premium.canceled" ||
                 x.EventCode == "administrator.setting.updated" || x.EventCode == "administrator.premium_plan.updated"))
            .OrderByDescending(x => x.OccurredAtUtc).Take(200)
            .Select(x => new { x.Id, x.EventCode, x.EntityType, x.EntityId, x.OccurredAtUtc }).ToListAsync(ct);
        foreach (var item in operationalEvents)
        {
            var presentation = item.EventCode switch
            {
                "appointment.canceled_by_manager" or "appointment.canceled_by_doctor" => ("canceled", "warning", "Consulta cancelada", "Uma consulta foi cancelada e pode exigir acompanhamento operacional."),
                "appointment.rescheduled_by_manager" or "appointment.rescheduled_by_doctor" => ("rescheduled", "info", "Consulta reagendada", "Uma consulta teve data ou horário alterado."),
                "manager.payment.confirmed" => ("payment_approved", "info", "Pagamento confirmado", "Um pagamento presencial foi confirmado."),
                "administrator.premium.canceled" => ("premium_decision", "warning", "Benefício Premium cancelado", "Um benefício Premium ativo foi cancelado."),
                "administrator.setting.updated" => ("system_update", "high", "Configuração alterada", "Uma configuração operacional do sistema foi alterada."),
                _ => ("system_update", "info", "Plano Premium alterado", "As regras de um plano Premium foram atualizadas.")
            };
            Add($"audit:{item.Id}", presentation.Item1, presentation.Item2, presentation.Item3, presentation.Item4,
                item.EntityType ?? "audit_event", item.EntityId ?? item.Id.ToString(CultureInfo.InvariantCulture), item.OccurredAtUtc);
        }
        if (database.ChangeTracker.HasChanges()) await database.SaveChangesAsync(ct);
        void Add(string source, string type, string severity, string title, string message, string entityType, string entityId, DateTime created)
        { if (!known.Add(source)) return; database.AdministratorNotifications.Add(new AdministratorNotification { AdministratorAccountId = actor, SourceKey = source, TypeCode = type, SeverityCode = severity, Title = title, Message = message, EntityType = entityType, EntityId = entityId, CreatedAtUtc = created, RowVersion = 1 }); }
    }

    public async Task SetAccountStatusAsync(ulong actor, ulong id, AdministratorAccountStatusRequest request, CancellationToken ct)
    {
        var account = await database.Accounts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdministratorRuleException(404, "Usuário não encontrado.");
        if (request.DecisionCode == "blocked" && (request.Reason?.Trim().Length ?? 0) < 5) throw new AdministratorRuleException(400, "Informe uma justificativa com pelo menos cinco caracteres.");
        if (account.RoleCode == ViverAppRoles.Administrator && request.DecisionCode == "blocked")
        {
            if (account.Id == actor) throw new AdministratorRuleException(409, "O Administrador atual não pode bloquear a própria conta.");
            if (await database.Accounts.CountAsync(x => x.RoleCode == ViverAppRoles.Administrator && x.StatusCode == "active", ct) <= 1)
                throw new AdministratorRuleException(409, "O último Administrador recuperável não pode ser bloqueado.");
        }
        if (account.RowVersion != request.RowVersion) throw new AdministratorRuleException(409, "Os dados foram alterados por outra sessão.");
        var expected = request.DecisionCode == "blocked" ? "active" : "blocked"; if (account.StatusCode != expected) throw new AdministratorRuleException(409, "A mudança não é válida para o estado atual.");
        var before = account.StatusCode; account.StatusCode = request.DecisionCode == "blocked" ? "blocked" : "active"; account.UpdatedAtUtc = Now; account.RowVersion++;
        if (account.StatusCode == "blocked") await database.AuthSessions.Where(x => x.AccountId == id && x.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, Now).SetProperty(x => x.RevokeReasonCode, "account_status"), ct);
        await database.SaveChangesAsync(ct); await audit.WriteAsync("administrator.user.status_changed", actor, "account", id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["before"] = before, ["after"] = account.StatusCode, ["reason"] = request.Reason?.Trim() ?? "" }, ct);
    }

    public async Task SetDoctorOnlineAsync(ulong actor, ulong id, AdministratorDoctorOnlineRequest request, CancellationToken ct)
    {
        var preference = await database.DoctorPreferences.SingleOrDefaultAsync(x => x.DoctorAccountId == id, ct) ?? throw new AdministratorRuleException(404, "Médico não encontrado.");
        if (preference.RowVersion != request.RowVersion) throw new AdministratorRuleException(409, "Os dados foram alterados por outra sessão.");
        var before = preference.OnlineEnabled; preference.OnlineEnabled = request.Enabled; preference.UpdatedAtUtc = Now; preference.RowVersion++;
        await database.SaveChangesAsync(ct); await audit.WriteAsync("administrator.doctor.online_changed", actor, "account", id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["before"] = before.ToString(), ["after"] = request.Enabled.ToString() }, ct);
    }

    private async Task<TimeZoneInfo> TimezoneAsync(CancellationToken ct) { var name = await database.Clinics.Select(x => x.TimezoneName).SingleOrDefaultAsync(ct) ?? "America/Sao_Paulo"; return TimeZoneInfo.FindSystemTimeZoneById(name); }
    private static DateTime ToUtc(DateOnly date, TimeZoneInfo timezone) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), timezone);
    private static void ValidateSetting(string key, JsonElement value)
    {
        if (key is "web.maintenance_mode" or "appointments.allow_clinic_payment" or "appointments.online_calls_enabled" or "communications.email_enabled" or "communications.sms_enabled" or "premium.manager_can_decide"
            or "manager.appointment_types_enabled" or "manager.doctor_schedules_enabled" or "doctor.patient_scheduling_enabled"
            or "premium.manager_can_manage" or "manager.medical_records_write_enabled"
            or "cash.manager_can_reopen" or "cash.manager_can_view_cumulative_totals"
            or "appointments.arrival_notifications_enabled" or "appointments.arrival_popup_enabled" or "appointments.arrival_sound_enabled" or "appointments.arrival_mark_read_on_open")
        { if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new JsonException(); return; }
        if (key == "appointments.arrival_sound_key")
        { if (value.ValueKind != JsonValueKind.String || value.GetString() is not "soft_chime") throw new JsonException(); return; }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number)) throw new JsonException();
        var valid = key switch
        {
            "appointments.booking_horizon_days" => number is >= 1 and <= 730,
            "appointments.minimum_lead_minutes" => number is >= 0 and <= 10080,
            "appointments.cancellation_cutoff_hours" or "appointments.reschedule_cutoff_hours" => number is >= 0 and <= 720,
            "appointments.slot_interval_minutes" or "appointments.interval_minutes" => number is >= 0 and <= 240,
            "appointments.patient_daily_limit" => number is >= 1 and <= 20,
            "appointments.default_consultation_minutes" or "appointments.default_examination_minutes" or "appointments.default_surgery_minutes" or "appointments.default_procedure_minutes" => number is >= 5 and <= 480,
            "appointments.arrival_sound_volume" => number is >= 0 and <= 100,
            "appointments.arrival_notification_retention_days" => number is >= 1 and <= 365,
            _ => false
        }; if (!valid) throw new JsonException();
    }
}

public sealed class AdministratorRuleException(int statusCode, string message) : Exception(message) { public int StatusCode { get; } = statusCode; }
