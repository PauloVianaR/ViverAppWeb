using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.PatientExperience;

public sealed class PatientExperienceService(ViverAppDbContext database, PatientSchedulingService scheduling,
    IdentityAuditWriter audit, TimeProvider clock, IConfiguration configuration)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private async Task<(DateTime? From, DateTime? Until)> UtcPeriodAsync(DateTime? from, DateTime? until, CancellationToken ct)
    {
        if (from > until || from?.Year < 1900 || until?.Year >= 9999) throw Invalid("O período é inválido.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById((await ClinicAsync(ct)).Timezone);
        DateTime Utc(DateTime day) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), zone);
        return (from.HasValue ? Utc(from.Value.Date) : null, until.HasValue ? Utc(until.Value.Date.AddDays(1)) : null);
    }
    public static PatientExperienceException Invalid(string message) => new(400, message);
    public static PatientExperienceException Missing() => new(404, "Registro não encontrado.");
    public static PatientExperienceException Conflict(string message) => new(409, message);

    public static void ValidatePage(int page, int size)
    {
        if (page < 1 || page > 100000 || size is < 1 or > 50) throw Invalid("Paginação inválida.");
    }

    public async Task<PatientClinicResponse> ClinicAsync(CancellationToken ct)
    {
        var clinic = await database.Clinics.AsNoTracking().SingleOrDefaultAsync(ct);
        if (clinic is null) return new("ViverApp", "Endereço da clínica ainda não informado", null, null, "America/Sao_Paulo");
        var address = string.Join(", ", new[] { clinic.Street, clinic.Number, clinic.Complement, clinic.District, clinic.City, clinic.StateCode, clinic.PostalCode }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new(clinic.DisplayName, address, clinic.PhoneE164,
            address.Length == 0 ? null : "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(address), clinic.TimezoneName);
    }

    public async Task<PatientHomeResponse> HomeAsync(ulong actor, CancellationToken ct)
    {
        var name = await database.Accounts.Where(x => x.Id == actor).Select(x => x.FullName).SingleAsync(ct);
        var premium = await IsPremiumAsync(actor, ct);
        var next = await database.Appointments.AsNoTracking().Where(x => x.PatientAccountId == actor
            && (x.StatusCode == "pending" || x.StatusCode == "confirmed") && x.EndsAtUtc > Now && x.InverseRescheduledFromAppointment == null)
            .OrderBy(x => x.StartsAtUtc).Select(x => (ulong?)x.Id).FirstOrDefaultAsync(ct);
        var promotions = new List<PatientPromotionResponse>();
        if (!premium)
        {
            var json = await database.ApplicationSettings.Where(x => x.SettingKey == "patient.promotions").Select(x => x.ValueJson).SingleOrDefaultAsync(ct);
            var hosts = configuration.GetSection("PatientExperience:PromotionHosts").Get<string[]>() ?? [];
            try
            {
                foreach (var item in JsonSerializer.Deserialize<PatientPromotionResponse[]>(json ?? "[]", new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Take(10) ?? [])
                {
                    if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 120 || item.Description?.Length > 500) continue;
                    if (item.Url is not null && (!Uri.TryCreate(item.Url, UriKind.Absolute, out var uri)
                        || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
                        || !hosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))) continue;
                    promotions.Add(item);
                }
            }
            catch (JsonException) { /* Configuração inválida não exibe promoção. */ }
        }
        return new(name, premium, await ClinicAsync(ct), next.HasValue ? await AppointmentAsync(actor, next.Value, ct) : null, promotions);
    }

    public async Task<decimal> DiscountAsync(ulong actor, CancellationToken ct) => await database.PremiumMemberships.AsNoTracking()
        .Where(x => x.AccountId == actor && x.StatusCode == "active" && x.StartsAtUtc <= Now
            && (x.EndsAtUtc == null || x.EndsAtUtc > Now) && x.PremiumPlan.IsActive)
        .Select(x => (decimal?)x.PremiumPlan.AppointmentDiscountPercent).MaxAsync(ct) ?? 0;
    public Task<bool> IsPremiumAsync(ulong actor, CancellationToken ct) => database.PremiumMemberships.AsNoTracking()
        .AnyAsync(x => x.AccountId == actor && x.StatusCode == "active" && x.StartsAtUtc <= Now
            && (x.EndsAtUtc == null || x.EndsAtUtc > Now) && x.PremiumPlan.IsActive, ct);
    public static decimal DiscountedPrice(decimal price, decimal discount) => price - decimal.Round(price * discount / 100m, 2, MidpointRounding.AwayFromZero);

    public async Task<SchedulingPage<PatientServiceResponse>> ServicesAsync(ulong actor, int page, int pageSize, string? category, string? search, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        if (category is not (null or "" or "consultation" or "examination" or "surgery" or "procedure")) throw Invalid("Tipo inválido.");
        var query = database.AppointmentTypes.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrEmpty(category)) query = query.Where(x => x.CategoryCode == category);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Name.Contains(search.Trim()));
        var total = await query.CountAsync(ct);
        var discount = await DiscountAsync(actor, ct);
        var items = await query.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items.Select(x => new PatientServiceResponse(x.Id, x.Name, x.Description, x.CategoryCode, x.ModalityCode,
            x.DurationMinutes, x.PriceAmount, discount, DiscountedPrice(x.PriceAmount, discount))).ToArray(), page, pageSize, total);
    }

    public async Task<PatientServiceResponse> ServiceAsync(ulong actor, uint id, CancellationToken ct)
    {
        var item = await database.AppointmentTypes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct) ?? throw Missing();
        var discount = await DiscountAsync(actor, ct);
        return new(item.Id, item.Name, item.Description, item.CategoryCode, item.ModalityCode, item.DurationMinutes, item.PriceAmount, discount, DiscountedPrice(item.PriceAmount, discount));
    }

    public async Task<SchedulingPage<PatientAppointmentResponse>> AgendaAsync(ulong actor, int page, int pageSize,
        string view, string? search, ulong? appointmentNumber, DateTime? from, DateTime? until, string? status, string? category, string? modality, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        if (view is not ("future" or "history" or "all")) throw Invalid("Visualização inválida.");
        if (from > until) throw Invalid("O período é inválido.");
        (from, until) = await UtcPeriodAsync(from, until, ct);
        var query = database.Appointments.AsNoTracking().Where(x => x.PatientAccountId == actor && x.InverseRescheduledFromAppointment == null);
        if (view == "future") query = query.Where(x => (x.StatusCode == "pending" || x.StatusCode == "confirmed" || x.StatusCode == "arrived" || x.StatusCode == "in_progress") && x.EndsAtUtc >= Now);
        if (view == "history") query = query.Where(x => (x.StatusCode != "pending" && x.StatusCode != "confirmed" && x.StatusCode != "arrived" && x.StatusCode != "in_progress") || x.EndsAtUtc < Now);
        if (from.HasValue) query = query.Where(x => x.StartsAtUtc >= from);
        if (until.HasValue) query = query.Where(x => x.StartsAtUtc < until);
        if (status == "rescheduled") query = query.Where(x => x.AppointmentRescheduleHistories.Any() || x.RescheduledFromAppointmentId != null);
        else if (!string.IsNullOrEmpty(status)) query = query.Where(x => x.StatusCode == status);
        if (!string.IsNullOrEmpty(category)) query = query.Where(x => x.AppointmentType.CategoryCode == category);
        if (!string.IsNullOrEmpty(modality)) query = query.Where(x => x.ModalityCode == modality);
        if (appointmentNumber.HasValue) query = query.Where(x => x.AppointmentNumber == appointmentNumber);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); var isNumber = ulong.TryParse(term, out var number); query = query.Where(x => x.DoctorAccount.Account.FullName.Contains(term) || x.AppointmentType.Name.Contains(term) || isNumber && x.AppointmentNumber == number); }
        var total = await query.CountAsync(ct);
        query = view == "history" ? query.OrderByDescending(x => x.StartsAtUtc).ThenByDescending(x => x.Id) : query.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id);
        var ids = await query.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToArrayAsync(ct);
        var result = new List<PatientAppointmentResponse>();
        foreach (var id in ids) result.Add(await AppointmentAsync(actor, id, ct));
        return new(result, page, pageSize, total);
    }

    public async Task<PatientAppointmentResponse> AppointmentAsync(ulong actor, ulong id, CancellationToken ct)
    {
        var entity = await database.Appointments.AsNoTracking().Include(x => x.AppointmentType).Include(x => x.CurrentPayment)
            .Include(x => x.MedicalReport).Include(x => x.AppointmentReview)
            .SingleOrDefaultAsync(x => x.Id == id && x.PatientAccountId == actor, ct) ?? throw Missing();
        var response = await scheduling.GetAppointmentAsync(actor, id, ct);
        var active = entity.StatusCode is "pending" or "confirmed";
        var cutoffs = await database.ApplicationSettings.AsNoTracking().Where(x => x.SettingKey == "appointments.cancellation_cutoff_hours" || x.SettingKey == "appointments.reschedule_cutoff_hours")
            .ToDictionaryAsync(x => x.SettingKey, x => x.ValueJson, ct);
        int Cutoff(string key) => cutoffs.TryGetValue(key, out var value) && int.TryParse(value, out var hours) && hours is >= 0 and <= 720 ? hours : 24;
        var specialties = await database.DoctorSpecialties.Where(x => x.DoctorAccountId == entity.DoctorAccountId && x.Specialty.IsActive).Select(x => x.Specialty.Name).ToArrayAsync(ct);
        return new(response, entity.AppointmentType.CategoryCode, string.Join(" • ", specialties), entity.CurrentPayment?.StatusCode ?? "unpaid", entity.PaymentLocationCode,
            entity.BasePriceAmount ?? entity.PriceAmount, entity.DiscountPercent,
            active && entity.StartsAtUtc > Now && (entity.CurrentPayment == null || entity.CurrentPayment.StatusCode is "pending" or "failed" or "canceled" or "reversed" or "refunded"),
            active && entity.StartsAtUtc > Now.AddHours(Cutoff("appointments.cancellation_cutoff_hours")),
            active && entity.StartsAtUtc > Now.AddHours(Cutoff("appointments.reschedule_cutoff_hours")),
            entity.ModalityCode == "online" && entity.StatusCode == "confirmed" && entity.CurrentPayment?.StatusCode == "paid" && entity.StartsAtUtc <= Now.AddMinutes(15) && entity.EndsAtUtc >= Now,
            entity.MedicalReport?.StatusCode == "published", entity.AppointmentReview?.Rating,
            active && entity.StartsAtUtc > Now && entity.ModalityCode == "in_person" && (entity.CurrentPayment == null || entity.CurrentPayment.StatusCode is "reversed" or "refunded" or "canceled" or "failed") && await ClinicPaymentAllowedAsync(ct));
    }

    public async Task ReviewAsync(ulong actor, ulong id, PatientReviewRequest request, CancellationToken ct)
    {
        if (request.Rating is < 1 or > 5 || request.Comment?.Length > 1000) throw Invalid("Avaliação inválida.");
        await using var tx = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var appointment = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id={id} AND patient_account_id={actor} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw Missing();
        if (appointment.StatusCode != "completed" || await database.AppointmentReviews.AnyAsync(x => x.AppointmentId == id, ct)) throw Conflict("Só é possível avaliar uma vez, após a conclusão do atendimento.");
        database.AppointmentReviews.Add(new() { AppointmentId = id, Rating = request.Rating, Comment = request.Comment?.Trim(), CreatedAtUtc = Now });
        await audit.WriteAsync("patient.appointment_reviewed", actor, "appointment", id.ToString(CultureInfo.InvariantCulture), null, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<PatientProfileResponse> ProfileAsync(ulong actor, CancellationToken ct)
    {
        var account = await database.Accounts.AsNoTracking().Include(x => x.PatientProfile).Include(x => x.AccountAddress).Include(x => x.PatientPreference).SingleAsync(x => x.Id == actor, ct);
        var address = account.AccountAddress;
        return new(account.FullName, account.Email, account.PhoneE164, account.EmailVerified, account.PhoneVerified,
            account.TaxId ?? account.PatientProfile?.TaxId, (account.BirthDate ?? account.PatientProfile?.BirthDate) is DateTime birth ? DateOnly.FromDateTime(birth) : null,
            address is null ? null : new PatientProfileAddressResponse(address.PostalCode, address.Street, address.Number, address.Complement, address.District, address.City, address.StateCode),
            account.PatientPreference?.EmailEnabled ?? true, account.PatientPreference?.SmsEnabled ?? true, account.RowVersion);
    }

    public async Task UpdateProfileAsync(ulong actor, PatientProfileRequest request, CancellationToken ct)
    {
        if (!BrazilianDocumentValidator.IsValidCpf(request.TaxId) || request.BirthDate > DateOnly.FromDateTime(Now) || request.BirthDate < DateOnly.FromDateTime(Now).AddYears(-125)) throw Invalid("Confira CPF e nascimento.");
        if (request.FullName.Trim().Length < 3 || new[] { request.Address.Street, request.Address.Number, request.Address.District, request.Address.City }.Any(string.IsNullOrWhiteSpace)
            || !new[] { "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO" }.Contains(request.Address.StateCode)) throw Invalid("Confira nome e endereço completo.");
        if (await database.Accounts.AnyAsync(x => x.Id != actor && x.TaxId == request.TaxId, ct)) throw Conflict("Não foi possível salvar os dados informados.");
        await using var tx = await database.Database.BeginTransactionAsync(ct);
        var account = await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE id={actor} FOR UPDATE").SingleAsync(ct);
        if (account.RowVersion != request.RowVersion) throw Conflict("O perfil mudou. Atualize a página antes de salvar.");
        account.FullName = request.FullName.Trim(); account.UpdatedAtUtc = Now; account.RowVersion++;
        account.TaxId = request.TaxId; account.BirthDate = request.BirthDate.ToDateTime(TimeOnly.MinValue);
        var profile = await database.PatientProfiles.SingleOrDefaultAsync(x => x.AccountId == actor, ct);
        if (profile is null) { profile = new() { AccountId = actor, CreatedAtUtc = Now }; database.PatientProfiles.Add(profile); }
        profile.TaxId = request.TaxId; profile.BirthDate = request.BirthDate.ToDateTime(TimeOnly.MinValue); profile.UpdatedAtUtc = Now;
        var address = await database.AccountAddresses.SingleOrDefaultAsync(x => x.AccountId == actor, ct);
        if (address is null) { address = new() { AccountId = actor }; database.AccountAddresses.Add(address); }
        address.PostalCode = request.Address.PostalCode; address.Street = request.Address.Street.Trim(); address.Number = request.Address.Number.Trim();
        address.Complement = request.Address.Complement?.Trim(); address.District = request.Address.District.Trim(); address.City = request.Address.City.Trim(); address.StateCode = request.Address.StateCode; address.UpdatedAtUtc = Now;
        var prefs = await database.PatientPreferences.SingleOrDefaultAsync(x => x.AccountId == actor, ct);
        if (prefs is null) { prefs = new() { AccountId = actor }; database.PatientPreferences.Add(prefs); }
        prefs.EmailEnabled = request.EmailEnabled; prefs.SmsEnabled = request.SmsEnabled; prefs.UpdatedAtUtc = Now;
        await audit.WriteAsync("patient.profile_updated", actor, null, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<PatientPremiumResponse> PremiumAsync(ulong actor, CancellationToken ct)
    {
        var member = await database.PremiumMemberships.AsNoTracking().Where(x => x.AccountId == actor).OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var plans = await database.PremiumPlans.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => new PatientPremiumPlan(x.Id, x.Name, x.AppointmentDiscountPercent)).ToArrayAsync(ct);
        var active = await IsPremiumAsync(actor, ct);
        var next = member is { StatusCode: "rejected" } ? (member.ReviewedAtUtc ?? member.UpdatedAtUtc).AddDays(3) : (DateTime?)null;
        var pending = await database.PremiumMemberships.AnyAsync(x => x.AccountId == actor && x.StatusCode == "pending", ct);
        return new(active, member?.Id, active ? "active" : member?.StatusCode == "active" ? "expired" : member?.StatusCode ?? "none", member?.RejectionReason,
            member?.ReviewedAtUtc, next, !active && !pending && (next == null || next <= Now) && plans.Length > 0,
            await DiscountAsync(actor, ct), member?.RowVersion ?? 0, member?.ProofDocumentId, plans);
    }

    public async Task RequestPremiumAsync(ulong actor, uint planId, PrivateDocument document, CancellationToken ct)
    {
        await using var tx = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE id={actor} FOR UPDATE").SingleAsync(ct);
        await database.PremiumMemberships.Where(x => x.AccountId == actor && x.StatusCode == "active" && x.EndsAtUtc <= Now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StatusCode, "expired").SetProperty(x => x.UpdatedAtUtc, Now).SetProperty(x => x.RowVersion, x => x.RowVersion + 1), ct);
        var state = await PremiumAsync(actor, ct);
        if (!state.CanRequest || !state.Plans.Any(x => x.Id == planId)) throw Conflict("Não é possível enviar uma nova solicitação agora. Confira o plano e o prazo de três dias.");
        database.PrivateDocuments.Add(document);
        database.PremiumMemberships.Add(new() { AccountId = actor, PremiumPlanId = planId, StatusCode = "pending", ProofDocumentId = document.Id, CreatedAtUtc = Now, UpdatedAtUtc = Now, RowVersion = 1 });
        await audit.WriteAsync("patient.premium_requested", actor, null, ct);
        await tx.CommitAsync(ct);
    }

    public async Task CancelPremiumAsync(ulong actor, ulong id, ulong version, CancellationToken ct)
    {
        await using var tx = await database.Database.BeginTransactionAsync(ct);
        await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE id={actor} FOR UPDATE").SingleAsync(ct);
        var member = await database.PremiumMemberships.SingleOrDefaultAsync(x => x.Id == id && x.AccountId == actor, ct) ?? throw Missing();
        if (member.RowVersion != version || member.StatusCode is not ("pending" or "active")) throw Conflict("O benefício foi alterado. Atualize antes de cancelar.");
        member.StatusCode = "canceled"; member.UpdatedAtUtc = Now; member.RowVersion++;
        await audit.WriteAsync("patient.premium_canceled", actor, null, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<SchedulingPage<PatientPaymentItem>> PaymentsAsync(ulong actor, int page, int pageSize, string view,
        DateTime? from, DateTime? until, decimal? min, decimal? max, string? method, string? location, string? status, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        if (view is not ("pending" or "history") || from > until || min < 0 || max < min) throw Invalid("Filtros inválidos.");
        (from, until) = await UtcPeriodAsync(from, until, ct);
        var query = database.Appointments.AsNoTracking().Where(x => x.PatientAccountId == actor && x.InverseRescheduledFromAppointment == null);
        query = view == "pending" ? query.Where(x => (x.StatusCode == "pending" || x.StatusCode == "confirmed") && x.StartsAtUtc > Now && (x.CurrentPayment == null || x.CurrentPayment.StatusCode == "pending" || x.CurrentPayment.StatusCode == "failed" || x.CurrentPayment.StatusCode == "canceled" || x.CurrentPayment.StatusCode == "reversed" || x.CurrentPayment.StatusCode == "refunded")) : query.Where(x => x.CurrentPayment != null);
        if (from.HasValue) query = query.Where(x => (x.CurrentPayment != null ? x.CurrentPayment.CreatedAtUtc : x.CreatedAtUtc) >= from);
        if (until.HasValue) query = query.Where(x => (x.CurrentPayment != null ? x.CurrentPayment.CreatedAtUtc : x.CreatedAtUtc) < until);
        if (min.HasValue) query = query.Where(x => x.PriceAmount >= min);
        if (max.HasValue) query = query.Where(x => x.PriceAmount <= max);
        if (!string.IsNullOrEmpty(method)) query = query.Where(x => x.CurrentPayment != null && x.CurrentPayment.MethodCode == method);
        if (!string.IsNullOrEmpty(location)) query = query.Where(x => x.PaymentLocationCode == location);
        if (!string.IsNullOrEmpty(status)) query = status == "unpaid" ? query.Where(x => x.CurrentPayment == null) : query.Where(x => x.CurrentPayment != null && x.CurrentPayment.StatusCode == status);
        var total = await query.CountAsync(ct);
        var clinicAllowed = await ClinicPaymentAllowedAsync(ct);
        var entities = await query.Include(x => x.CurrentPayment).Include(x => x.AppointmentType).Include(x => x.DoctorAccount.Account)
            .OrderByDescending(x => x.StartsAtUtc).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(entities.Select(x => new PatientPaymentItem(x.Id, x.CurrentPayment?.Id, x.AppointmentType.Name, x.DoctorAccount.Account.FullName,
            DateTime.SpecifyKind(x.StartsAtUtc, DateTimeKind.Utc), x.CurrentPayment?.StatusCode ?? "unpaid", x.CurrentPayment?.Amount ?? x.PriceAmount, x.CurrentPayment?.MethodCode,
            x.PaymentLocationCode, x.CurrentPayment?.PaidAtUtc, clinicAllowed && x.ModalityCode == "in_person" && (x.CurrentPayment == null || x.CurrentPayment.StatusCode is "reversed" or "refunded" or "canceled" or "failed"),
            (x.StatusCode is "pending" or "confirmed") && x.StartsAtUtc > Now && (x.CurrentPayment == null || x.CurrentPayment.StatusCode is "pending" or "failed" or "canceled" or "reversed" or "refunded"), x.ModalityCode)).ToArray(), page, pageSize, total);
    }

    private async Task<bool> ClinicPaymentAllowedAsync(CancellationToken ct)
    {
        var json = await database.ApplicationSettings.Where(x => x.SettingKey == "appointments.allow_clinic_payment").Select(x => x.ValueJson).SingleOrDefaultAsync(ct);
        return string.Equals(json?.Trim(), "true", StringComparison.Ordinal);
    }

    public async Task ChooseClinicPaymentAsync(ulong actor, ulong id, CancellationToken ct)
    {
        await using var tx = await database.Database.BeginTransactionAsync(ct);
        var item = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id={id} AND patient_account_id={actor} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw Missing();
        if (item.ModalityCode != "in_person" || item.StatusCode is not ("pending" or "confirmed") || item.StartsAtUtc <= Now
            || !await ClinicPaymentAllowedAsync(ct) || await database.Payments.AnyAsync(x => x.AppointmentId == id && x.ActiveAppointmentId != null, ct)) throw Conflict("Pagamento na clínica não está disponível para este atendimento.");
        if (item.PaymentLocationCode != "clinic") { item.PaymentLocationCode = "clinic"; item.RowVersion++; item.UpdatedAtUtc = Now; }
        await audit.WriteAsync("patient.clinic_payment_selected", actor, "appointment", id.ToString(CultureInfo.InvariantCulture), null, ct);
        await tx.CommitAsync(ct);
    }
}
