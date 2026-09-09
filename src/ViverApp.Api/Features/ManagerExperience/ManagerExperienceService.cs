using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.ManagerExperience;

public sealed class ManagerExperienceService(ViverAppDbContext database, UserManager<ViverAppUser> users,
    IdentityChallengeService challenges, IClinicalOperationsAuditWriter audit, TimeProvider clock)
{
    private static readonly string[] AppointmentStatuses = ["pending", "confirmed", "arrived", "in_progress", "completed", "canceled", "rescheduled", "no_show"];
    private static readonly string[] PaymentFilters = ["paid", "pending"];
    private static readonly string[] Methods = ["credit_card", "debit_card", "pix", "cash"];

    public async Task<ManagerHomeResponse> HomeAsync(ulong actor, CancellationToken ct)
    {
        var profile = await ProfileAsync(actor, ct);
        var timezone = await TimezoneAsync(ct);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timezone).DateTime);
        var from = StartUtc(localToday, timezone); var to = StartUtc(localToday.AddDays(1), timezone);
        var rows = await AppointmentQuery().Where(x => x.StartsAtUtc >= from && x.StartsAtUtc < to)
            .OrderBy(x => x.StartsAtUtc).ToArrayAsync(ct);
        var mapped = rows.Select(MapAppointment).ToArray();
        return new(profile, new(rows.Length, rows.Select(x => x.DoctorAccountId).Distinct().Count(),
            rows.Count(x => x.PaymentAppointment != null && x.PaymentAppointment.StatusCode == "paid"),
            rows.Count(x => x.PaymentAppointment == null || x.PaymentAppointment.StatusCode != "paid"),
            rows.Count(x => x.ModalityCode == "online"), rows.Count(x => x.ModalityCode == "in_person")), mapped);
    }

    public async Task<ManagerProfileResponse> ProfileAsync(ulong actor, CancellationToken ct)
    {
        var account = await database.Accounts.AsNoTracking().Include(x => x.ManagerPreference)
            .SingleOrDefaultAsync(x => x.Id == actor && x.RoleCode == ViverAppRoles.Manager && x.StatusCode == "active", ct) ?? throw Missing();
        return MapProfile(account);
    }

    public async Task<ManagerProfileResponse> UpdateProfileAsync(ulong actor, ManagerProfileUpdateRequest request, CancellationToken ct)
    {
        var account = await database.Accounts.Include(x => x.ManagerPreference)
            .SingleOrDefaultAsync(x => x.Id == actor && x.RoleCode == ViverAppRoles.Manager, ct) ?? throw Missing();
        RequireVersion(account.RowVersion, request.AccountRowVersion);
        var now = clock.GetUtcNow().UtcDateTime;
        account.ManagerPreference ??= new ManagerPreference { ManagerAccountId = actor, UpdatedAtUtc = now, RowVersion = 1 };
        RequireVersion(account.ManagerPreference.RowVersion, request.PreferenceRowVersion);
        account.FullName = request.FullName.Trim(); account.UpdatedAtUtc = now; account.RowVersion++;
        account.ManagerPreference.EmailEnabled = request.EmailEnabled; account.ManagerPreference.SmsEnabled = request.SmsEnabled;
        account.ManagerPreference.UpdatedAtUtc = now; account.ManagerPreference.RowVersion++;
        await SaveAsync(ct); await audit.WriteAsync("manager.profile.updated", actor, "account", actor.ToString(CultureInfo.InvariantCulture), null, ct);
        return await ProfileAsync(actor, ct);
    }

    public async Task<IReadOnlyList<ManagerDoctorOption>> DoctorsAsync(CancellationToken ct) =>
        await database.DoctorProfiles.AsNoTracking().Where(x => x.Account.StatusCode == "active")
            .OrderBy(x => x.Account.FullName).Select(x => new ManagerDoctorOption(x.AccountId, x.Account.FullName,
                "CRM " + x.LicenseStateCode + " " + x.LicenseNumber)).ToArrayAsync(ct);

    public async Task<IReadOnlyList<ManagerServiceOption>> ServicesAsync(CancellationToken ct) =>
        await database.AppointmentTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new ManagerServiceOption(x.Id, x.Name, x.CategoryCode, x.ModalityCode, x.DurationMinutes, x.PriceAmount)).ToArrayAsync(ct);

    public async Task<ManagerAgendaResponse> AgendaAsync(DateOnly from, DateOnly to, string? status, string? modality,
        string? category, ulong? doctor, string? payment, TimeOnly? startTime, TimeOnly? endTime, string? search,
        string sort, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); if (to < from || to.DayNumber - from.DayNumber > 366) throw Invalid("O período deve ter no máximo 367 dias.");
        if (status is not null && !AppointmentStatuses.Contains(status, StringComparer.Ordinal)) throw Invalid("Estado inválido.");
        if (modality is not null && modality is not ("online" or "in_person")) throw Invalid("Modalidade inválida.");
        if (category is not null && category is not ("consultation" or "examination" or "surgery")) throw Invalid("Tipo inválido.");
        if (payment is not null && !PaymentFilters.Contains(payment, StringComparer.Ordinal)) throw Invalid("Filtro de pagamento inválido.");
        if (startTime.HasValue != endTime.HasValue || startTime >= endTime) throw Invalid("O intervalo de horário é inválido.");
        var timezone = await TimezoneAsync(ct); var fromUtc = StartUtc(from, timezone); var toUtc = StartUtc(to.AddDays(1), timezone);
        var query = AppointmentQuery().Where(x => x.StartsAtUtc >= fromUtc && x.StartsAtUtc < toUtc);
        if (status is not null) query = query.Where(x => x.StatusCode == status);
        if (modality is not null) query = query.Where(x => x.ModalityCode == modality);
        if (category is not null) query = query.Where(x => x.AppointmentType.CategoryCode == category);
        if (doctor.HasValue) query = query.Where(x => x.DoctorAccountId == doctor);
        if (payment == "paid") query = query.Where(x => x.PaymentAppointment != null && x.PaymentAppointment.StatusCode == "paid");
        if (payment == "pending") query = query.Where(x => x.PaymentAppointment == null || x.PaymentAppointment.StatusCode != "paid");
        var term = Text(search); if (term is not null) { if (term.Length > 120) throw Invalid("A busca deve ter no máximo 120 caracteres."); var isNumber = ulong.TryParse(term, out var number); query = query.Where(x => x.PatientAccount.FullName.Contains(term) || x.DoctorAccount.Account.FullName.Contains(term) || x.AppointmentType.Name.Contains(term) || isNumber && x.AppointmentNumber == number); }
        query = sort switch { "date_desc" => query.OrderByDescending(x => x.StartsAtUtc), "patient" => query.OrderBy(x => x.PatientAccount.FullName).ThenBy(x => x.StartsAtUtc), "doctor" => query.OrderBy(x => x.DoctorAccount.Account.FullName).ThenBy(x => x.StartsAtUtc), _ => query.OrderBy(x => x.StartsAtUtc) };
        int total;
        Appointment[] rows;
        if (startTime.HasValue)
        {
            var candidates = await query.Select(x => new ManagerAgendaCandidate(x.Id, x.StartsAtUtc)).ToArrayAsync(ct);
            var matchingIds = candidates.Where(x => IsInsideLocalTimeRange(x.StartsAtUtc, timezone, startTime.Value, endTime!.Value))
                .Select(x => x.Id).ToArray();
            total = matchingIds.Length;
            var pageIds = matchingIds.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
            if (pageIds.Length == 0)
            {
                rows = [];
            }
            else
            {
                var pageRows = await AppointmentQuery().Where(x => pageIds.Contains(x.Id)).ToArrayAsync(ct);
                var rowsById = pageRows.ToDictionary(x => x.Id);
                rows = pageIds.Select(x => rowsById[x]).ToArray();
            }
        }
        else
        {
            total = await query.CountAsync(ct);
            rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        }
        var counterQuery = AppointmentQuery().Where(x => x.StartsAtUtc >= fromUtc && x.StartsAtUtc < toUtc);
        var all = await counterQuery.Select(x => new { x.ModalityCode, x.StatusCode, Paid = x.PaymentAppointment != null && x.PaymentAppointment.StatusCode == "paid" }).ToArrayAsync(ct);
        var counters = new ManagerAgendaCounters(total, all.Count(x => x.ModalityCode == "online"), all.Count(x => x.ModalityCode == "in_person"), all.Count(x => x.StatusCode == "rescheduled"), all.Count(x => x.Paid), all.Count(x => !x.Paid));
        return new(counters, new(rows.Select(MapAppointment).ToArray(), page, pageSize, total));
    }

    public async Task<ManagerAppointmentResponse> AppointmentAsync(ulong id, CancellationToken ct) =>
        MapAppointment(await AppointmentQuery().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing());

    public async Task<ManagerPatientsResponse> PatientsAsync(string? search, string? status, bool? premium, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); if (status is not null && status is not ("active" or "blocked" or "pending_confirmation")) throw Invalid("Estado inválido.");
        var now = clock.GetUtcNow().UtcDateTime;
        IQueryable<Account> query = database.Accounts.AsNoTracking().Where(x => x.RoleCode == ViverAppRoles.Patient).Include(x => x.PatientProfile).Include(x => x.AccountAddress).Include(x => x.PremiumMembershipAccounts);
        if (status is not null) query = query.Where(x => x.StatusCode == status);
        if (premium.HasValue) query = premium.Value ? query.Where(x => x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now))) : query.Where(x => !x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now)));
        var term = Text(search); if (term is not null) { if (term.Length > 120) throw Invalid("A busca deve ter no máximo 120 caracteres."); var taxTerm = new string(term.Where(char.IsAsciiDigit).ToArray()); var hasTaxTerm = taxTerm.Length >= 3; query = query.Where(x => x.FullName.Contains(term) || x.Email != null && x.Email.Contains(term) || x.PhoneE164 != null && x.PhoneE164.Contains(term) || hasTaxTerm && x.TaxId != null && x.TaxId.Contains(taxTerm)); }
        var total = await query.CountAsync(ct); var accounts = await query.OrderBy(x => x.FullName).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var ids = accounts.Select(x => x.Id).ToArray(); var appointments = await database.Appointments.AsNoTracking().Where(x => ids.Contains(x.PatientAccountId)).Select(x => new PatientHistoryRow(x.PatientAccountId, x.StartsAtUtc, x.StatusCode)).ToArrayAsync(ct);
        var items = accounts.Select(x => MapPatient(x, appointments.Where(a => a.PatientAccountId == x.Id), now)).ToArray();
        var baseQuery = database.Accounts.AsNoTracking().Where(x => x.RoleCode == ViverAppRoles.Patient);
        var counters = new ManagerPatientCounters(await baseQuery.CountAsync(ct), await baseQuery.CountAsync(x => x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now)), ct), await baseQuery.CountAsync(x => x.StatusCode == "active", ct), await baseQuery.CountAsync(x => x.StatusCode == "blocked", ct), await baseQuery.CountAsync(x => x.PremiumMembershipAccounts.Any(m => m.StatusCode == "pending"), ct));
        return new(counters, new(items, page, pageSize, total));
    }

    public async Task<ManagerPatientResponse> PatientAsync(ulong id, CancellationToken ct)
    {
        var account = await database.Accounts.AsNoTracking().Where(x => x.Id == id && x.RoleCode == ViverAppRoles.Patient).Include(x => x.PatientProfile).Include(x => x.AccountAddress).Include(x => x.PremiumMembershipAccounts).SingleOrDefaultAsync(ct) ?? throw Missing();
        var appointments = await database.Appointments.AsNoTracking().Where(x => x.PatientAccountId == id).Select(x => new PatientHistoryRow(x.PatientAccountId, x.StartsAtUtc, x.StatusCode)).ToArrayAsync(ct);
        return MapPatient(account, appointments, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<ManagerPatientResponse> CreatePatientAsync(ulong actor, ManagerPatientCreateRequest request, CancellationToken ct)
    {
        var email = IdentifierNormalizer.NormalizeEmail(request.Email); var phone = IdentifierNormalizer.NormalizePhone(request.PhoneE164);
        if (email is null && phone is null) throw Invalid("Informe um e-mail ou telefone brasileiro válido.");
        if (await database.Accounts.AnyAsync(x => email != null && x.NormalizedEmail == email || phone != null && x.PhoneE164 == phone, ct)) throw Conflict("Esse contato já pertence a uma conta. Nenhuma alteração foi feita.");
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var user = new ViverAppUser
        {
            UserName = email ?? phone,
            NormalizedUserName = email ?? phone,
            RoleCode = ViverAppRoles.Patient,
            StatusCode = "pending_confirmation",
            FullName = request.FullName.Trim(),
            Email = request.Email?.Trim(),
            NormalizedEmail = email,
            PhoneNumber = phone,
            BirthDate = request.BirthDate?.ToDateTime(TimeOnly.MinValue)
        };
        var created = await users.CreateAsync(user); if (!created.Succeeded) throw Conflict("Não foi possível criar o paciente.");
        var now = clock.GetUtcNow().UtcDateTime; database.PatientProfiles.Add(new PatientProfile { AccountId = user.Id, BirthDate = user.BirthDate, CreatedAtUtc = now, UpdatedAtUtc = now });
        await database.SaveChangesAsync(ct); var channel = email is not null ? "email" : "sms";
        await challenges.CreateAsync(user, "contact_verification", channel, email ?? phone!, ct);
        await audit.WriteAsync("manager.patient.invited", actor, "account", user.Id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string> { ["channel"] = channel }, ct);
        await transaction.CommitAsync(ct); return await PatientAsync(user.Id, ct);
    }

    public async Task<ManagerPatientResponse> UpdatePatientAsync(ulong actor, ulong id, ManagerPatientUpdateRequest request, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var account = await database.Accounts.Include(x => x.PatientProfile).Include(x => x.AccountAddress).SingleOrDefaultAsync(x => x.Id == id && x.RoleCode == ViverAppRoles.Patient, ct) ?? throw Missing();
        RequireVersion(account.RowVersion, request.RowVersion); var now = clock.GetUtcNow().UtcDateTime;
        var taxId = Text(request.TaxId); if (taxId is not null && !BrazilianDocumentValidator.IsValidCpf(taxId)) throw Invalid("O CPF informado é inválido.");
        var email = Text(request.Email); var normalizedEmail = email is null ? null : IdentifierNormalizer.NormalizeEmail(email);
        if (email is not null && normalizedEmail is null) throw Invalid("O e-mail informado é inválido.");
        var phone = Text(request.PhoneE164); var normalizedPhone = phone is null ? null : IdentifierNormalizer.NormalizePhone(phone);
        if (phone is not null && normalizedPhone is null) throw Invalid("O telefone deve estar no formato brasileiro com país e DDD.");
        if (normalizedEmail is null && normalizedPhone is null) throw Invalid("Mantenha ao menos um e-mail ou telefone para recuperação da conta.");
        if (request.BirthDate > DateOnly.FromDateTime(now) || request.BirthDate < DateOnly.FromDateTime(now).AddYears(-125)) throw Invalid("A data de nascimento é inválida.");
        if (await database.Accounts.AnyAsync(x => x.Id != id && ((taxId != null && x.TaxId == taxId) || (normalizedEmail != null && x.NormalizedEmail == normalizedEmail) || (normalizedPhone != null && x.PhoneE164 == normalizedPhone)), ct))
            throw Conflict("Não foi possível salvar os dados informados.");
        var changed = new List<string>();
        if (account.FullName != request.FullName.Trim()) changed.Add("fullName");
        if (account.TaxId != taxId) changed.Add("taxId");
        var emailChanged = account.NormalizedEmail != normalizedEmail; if (emailChanged) changed.Add("email");
        var phoneChanged = account.PhoneE164 != normalizedPhone; if (phoneChanged) changed.Add("phone");
        if (request.Address is not null || account.AccountAddress is not null) changed.Add("address");
        account.FullName = request.FullName.Trim(); account.TaxId = taxId; account.BirthDate = request.BirthDate?.ToDateTime(TimeOnly.MinValue);
        account.Email = email; account.NormalizedEmail = normalizedEmail; account.PhoneE164 = normalizedPhone;
        if (emailChanged) account.EmailVerified = false; if (phoneChanged) account.PhoneVerified = false;
        if (emailChanged || phoneChanged) account.SecurityStamp = RandomNumberGenerator.GetBytes(32);
        account.UpdatedAtUtc = now; account.RowVersion++;
        account.PatientProfile ??= new PatientProfile { AccountId = id, CreatedAtUtc = now };
        account.PatientProfile.PreferredName = Text(request.PreferredName); account.PatientProfile.TaxId = taxId; account.PatientProfile.BirthDate = account.BirthDate; account.PatientProfile.UpdatedAtUtc = now;
        if (request.Address is null) { if (account.AccountAddress is not null) database.AccountAddresses.Remove(account.AccountAddress); }
        else if (account.AccountAddress is null) account.AccountAddress = new AccountAddress { AccountId = id, PostalCode = request.Address.PostalCode, Street = request.Address.Street.Trim(), Number = request.Address.Number.Trim(), Complement = Text(request.Address.Complement), District = request.Address.District.Trim(), City = request.Address.City.Trim(), StateCode = request.Address.StateCode.ToUpperInvariant(), UpdatedAtUtc = now };
        else { account.AccountAddress.PostalCode = request.Address.PostalCode; account.AccountAddress.Street = request.Address.Street.Trim(); account.AccountAddress.Number = request.Address.Number.Trim(); account.AccountAddress.Complement = Text(request.Address.Complement); account.AccountAddress.District = request.Address.District.Trim(); account.AccountAddress.City = request.Address.City.Trim(); account.AccountAddress.StateCode = request.Address.StateCode.ToUpperInvariant(); account.AccountAddress.UpdatedAtUtc = now; }
        await SaveAsync(ct);
        var user = await users.FindByIdAsync(id.ToString(CultureInfo.InvariantCulture));
        if (user is not null && emailChanged && normalizedEmail is not null) await challenges.CreateAsync(user, "contact_verification", "email", normalizedEmail, ct);
        if (user is not null && phoneChanged && normalizedPhone is not null) await challenges.CreateAsync(user, "contact_verification", "sms", normalizedPhone, ct);
        var actorRole = await database.Accounts.AsNoTracking().Where(x => x.Id == actor).Select(x => x.RoleCode).SingleAsync(ct);
        await audit.WriteAsync(actorRole == ViverAppRoles.Administrator ? "administrator.patient.operational_data.updated" : "manager.patient.operational_data.updated", actor, "account", id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string> { ["changedFields"] = string.Join(',', changed) }, ct);
        await transaction.CommitAsync(ct);
        return await PatientAsync(id, ct);
    }

    public async Task<SchedulingPage<ManagerPremiumRequestResponse>> PremiumAsync(string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); if (status is not null && status is not ("pending" or "active" or "rejected" or "canceled" or "expired")) throw Invalid("Estado Premium inválido.");
        var query = database.PremiumMemberships.AsNoTracking().Include(x => x.Account).Include(x => x.PremiumPlan).Include(x => x.ProofDocument).AsQueryable();
        if (status is not null) query = query.Where(x => x.StatusCode == status);
        var term = Text(search); if (term is not null) query = query.Where(x => x.Account.FullName.Contains(term));
        var total = await query.CountAsync(ct); var rows = await query.OrderByDescending(x => x.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(MapPremium).ToArray(), page, pageSize, total);
    }

    public async Task<ManagerPremiumRequestResponse> PremiumRequestAsync(ulong id, CancellationToken ct) => MapPremium(
        await database.PremiumMemberships.AsNoTracking().Include(x => x.Account).Include(x => x.PremiumPlan).Include(x => x.ProofDocument).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing());

    public async Task<ManagerPremiumRequestResponse> DecidePremiumAsync(ulong actor, ulong id, ManagerPremiumDecisionRequest request, CancellationToken ct, bool administratorOverride = false)
    {
        var allowed = await database.ApplicationSettings.AsNoTracking().Where(x => x.SettingKey == "premium.manager_can_decide").Select(x => x.ValueJson).SingleOrDefaultAsync(ct);
        if (!administratorOverride && !string.Equals(allowed, "true", StringComparison.OrdinalIgnoreCase)) throw Forbidden("A decisão final está reservada ao Administrador.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var item = await database.PremiumMemberships.FromSqlInterpolated($"SELECT * FROM premium_memberships WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw Missing();
        if (item.StatusCode != "pending") throw Conflict("A solicitação já foi analisada por outra sessão."); RequireVersion(item.RowVersion, request.RowVersion);
        var notes = Text(request.Notes); var rejection = Text(request.RejectionReason); if (!request.Approve && (rejection?.Length ?? 0) < 5) throw Invalid("Informe um motivo de rejeição com pelo menos 5 caracteres.");
        var now = clock.GetUtcNow().UtcDateTime; item.StatusCode = request.Approve ? "active" : "rejected"; item.StartsAtUtc = request.Approve ? now : null;
        item.ReviewedAtUtc = now; item.ReviewedByAccountId = actor; item.ReviewNotes = notes; item.RejectionReason = request.Approve ? null : rejection; item.UpdatedAtUtc = now; item.RowVersion++;
        var recipient = await database.Accounts.AsNoTracking().Where(x => x.Id == item.AccountId).Select(x => x.Email ?? x.PhoneE164).SingleAsync(ct);
        if (recipient is not null) database.OutboxMessages.Add(new OutboxMessage { ChannelCode = recipient.Contains('@') ? "email" : "sms", TemplateKey = request.Approve ? "manager.premium.approved" : "manager.premium.rejected", Recipient = recipient, PayloadJson = JsonSerializer.Serialize(new { membershipId = id, status = item.StatusCode }), StatusCode = "pending", IdempotencyKey = Guid.NewGuid(), AttemptCount = 0, MaxAttempts = 5, NextAttemptAtUtc = now, CreatedAtUtc = now });
        await database.SaveChangesAsync(ct); await audit.WriteAsync(request.Approve ? "manager.premium.approved" : "manager.premium.rejected", actor, "premium_membership", id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string> { ["previousStatus"] = "pending", ["newStatus"] = item.StatusCode }, ct);
        await transaction.CommitAsync(ct); return await PremiumRequestAsync(id, ct);
    }

    public async Task<ManagerPaymentResponse> ConfirmPaymentAsync(ulong actor, ulong appointmentId, string key,
        ManagerPaymentConfirmRequest request, CancellationToken ct)
    {
        ValidateIdempotency(key); if (!Methods.Contains(request.MethodCode, StringComparer.Ordinal)) throw Invalid("Forma de pagamento inválida.");
        var isCard = request.MethodCode is "credit_card" or "debit_card";
        if (isCard && (request.CardLastFour is null || string.IsNullOrWhiteSpace(request.AuthorizationReference))) throw Invalid("Informe os quatro últimos dígitos e a autorização do cartão.");
        if (!isCard && (request.CardLastFour is not null || request.AuthorizationReference is not null)) throw Invalid("Dados de cartão só são permitidos para pagamentos com cartão.");
        var now = clock.GetUtcNow().UtcDateTime; var paidAt = DateTime.SpecifyKind(request.PaidAtUtc, DateTimeKind.Utc);
        if (paidAt > now.AddMinutes(5) || paidAt < now.AddYears(-1)) throw Invalid("A data do pagamento é inválida.");
        var scope = $"manager.payment:{actor}"; var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { actor, appointmentId, request })));
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var stored = await database.IdempotencyRecords.SingleOrDefaultAsync(x => x.ScopeCode == scope && x.IdempotencyKey == key, ct);
        if (stored is not null) { if (!CryptographicOperations.FixedTimeEquals(stored.RequestHash, hash)) throw Conflict("A chave de idempotência já foi usada com outros dados."); var replay = JsonSerializer.Deserialize<ManagerPaymentResponse>(stored.ResponseBodyJson!); await transaction.CommitAsync(ct); return replay ?? throw Conflict("Resposta idempotente inválida."); }
        var appointment = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id={appointmentId} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw Missing();
        if (appointment.RowVersion != request.AppointmentRowVersion) throw Conflict("O atendimento foi alterado por outra sessão.");
        if (appointment.ModalityCode != "in_person" || appointment.PaymentLocationCode != "clinic") throw Conflict("Somente pagamentos presenciais escolhidos para a clínica podem ser confirmados manualmente.");
        if (appointment.StatusCode is not ("pending" or "confirmed")) throw Conflict("O atendimento não aceita confirmação de pagamento.");
        var payment = await database.Payments.FromSqlInterpolated($"SELECT * FROM payments WHERE appointment_id={appointmentId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (payment is not null && payment.StatusCode == "paid") throw Conflict("O pagamento já foi reconciliado.");
        if (payment is not null && payment.ProviderCode != "internal") throw Conflict("Existe uma cobrança online vinculada; faça a reconciliação pelo provedor.");
        payment ??= new Payment { AppointmentId = appointmentId, ProviderCode = "internal", StatusCode = "pending", Amount = appointment.PriceAmount, CurrencyCode = "BRL", IdempotencyKey = GuidFromKey(key), CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1, ProviderReferenceAppointmentId = appointmentId };
        if (payment.Id == 0) database.Payments.Add(payment);
        payment.StatusCode = "paid"; payment.Amount = appointment.PriceAmount; payment.MethodCode = request.MethodCode; payment.PaidAtUtc = paidAt; payment.ConfirmedByAccountId = actor; payment.CardLastFour = request.CardLastFour; payment.AuthorizationReference = Text(request.AuthorizationReference); payment.UpdatedAtUtc = now; if (payment.Id != 0) payment.RowVersion++;
        var previous = appointment.StatusCode; if (appointment.StatusCode == "pending") { appointment.StatusCode = "confirmed"; appointment.UpdatedAtUtc = now; appointment.RowVersion++; database.AppointmentStatusHistories.Add(new AppointmentStatusHistory { AppointmentId = appointment.Id, ActorAccountId = actor, FromStatusCode = previous, ToStatusCode = "confirmed", Reason = "Pagamento presencial confirmado", StartsAtUtc = appointment.StartsAtUtc, EndsAtUtc = appointment.EndsAtUtc, OccurredAtUtc = now }); }
        await database.SaveChangesAsync(ct);
        database.PaymentEvents.Add(new PaymentEvent { PaymentId = payment.Id, SourceCode = "manual", ProviderStatusCode = request.MethodCode, NormalizedStatusCode = "paid", EventFingerprint = SHA256.HashData(Encoding.UTF8.GetBytes($"{actor}:{appointmentId}:{key}")), ProviderOccurredAtUtc = paidAt, OccurredAtUtc = now, WasApplied = true });
        var response = new ManagerPaymentResponse(payment.Id, appointmentId, payment.StatusCode, payment.Amount, payment.MethodCode!, paidAt, payment.CardLastFour, payment.AuthorizationReference, payment.RowVersion);
        database.IdempotencyRecords.Add(new IdempotencyRecord { ScopeCode = scope, IdempotencyKey = key, RequestHash = hash, ResponseStatusCode = 200, ResponseBodyJson = JsonSerializer.Serialize(response), CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(24) });
        await database.SaveChangesAsync(ct); await audit.WriteAsync("manager.payment.confirmed", actor, "payment", payment.Id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string> { ["appointmentId"] = appointmentId.ToString(CultureInfo.InvariantCulture), ["method"] = request.MethodCode, ["previousStatus"] = previous, ["newStatus"] = appointment.StatusCode }, ct);
        await transaction.CommitAsync(ct); return response;
    }

    private IQueryable<Appointment> AppointmentQuery() => database.Appointments.AsNoTracking()
        .Include(x => x.PatientAccount).Include(x => x.DoctorAccount).ThenInclude(x => x.Account).Include(x => x.AppointmentType)
        .Include(x => x.PaymentAppointment).Include(x => x.AppointmentReview).Include(x => x.MedicalReport).ThenInclude(x => x!.MedicalReportVersions)
        .Include(x => x.AppointmentDocuments).Include(x => x.InverseRescheduledFromAppointment);
    private static ManagerAppointmentResponse MapAppointment(Appointment x) => new(x.Id, x.AppointmentNumber, x.PatientAccountId, x.PatientAccount.FullName, x.PatientAccount.PhoneE164,
        x.DoctorAccountId, x.DoctorAccount.Account.FullName, x.AppointmentTypeId, x.AppointmentType.Name, x.AppointmentType.CategoryCode, x.StatusCode,
        x.ModalityCode, x.StartsAtUtc, x.EndsAtUtc, x.PriceAmount, x.DiscountPercent, x.PaymentLocationCode, x.PatientNotes, x.CancellationReason,
        x.RescheduledFromAppointmentId, x.InverseRescheduledFromAppointment?.Id, x.AppointmentReview?.Rating, x.AppointmentReview?.Comment,
        new(x.PaymentAppointment?.Id, x.PaymentAppointment?.StatusCode ?? "unpaid", x.PaymentAppointment?.MethodCode, x.PaymentAppointment?.PaidAtUtc,
            x.PaymentAppointment?.CardLastFour, x.PaymentAppointment?.AuthorizationReference, x.PaymentAppointment?.RowVersion ?? 0),
        new(x.MedicalReport is not null, x.MedicalReport?.StatusCode, (uint)(x.MedicalReport?.MedicalReportVersions.Count ?? 0), x.MedicalReport?.PublishedAtUtc),
        x.AppointmentDocuments.Count(d => d.StatusCode == "available"), x.ArrivedAtUtc,
        x.ArrivalBusinessDate is { } arrivalDate ? DateOnly.FromDateTime(arrivalDate) : null, x.ArrivalQueueNumber,
        x.ModalityCode == "in_person" && x.StatusCode == "confirmed", x.StatusCode is "pending" or "confirmed", x.StatusCode is "pending" or "confirmed",
        x.ModalityCode == "in_person" && x.PaymentLocationCode == "clinic" && x.StatusCode is "pending" or "confirmed" && x.PaymentAppointment?.StatusCode != "paid", x.RowVersion);
    private static ManagerProfileResponse MapProfile(Account x) => new(x.Id, x.FullName, x.Email, x.PhoneE164, x.TaxId, x.EmailVerified, x.PhoneVerified,
        x.ManagerPreference?.EmailEnabled ?? true, x.ManagerPreference?.SmsEnabled ?? true, x.RowVersion, x.ManagerPreference?.RowVersion ?? 1);
    private static ManagerPatientResponse MapPatient(Account x, IEnumerable<PatientHistoryRow> history, DateTime now)
    {
        var appointments = history.ToArray(); var premium = x.PremiumMembershipAccounts.OrderByDescending(m => m.CreatedAtUtc).FirstOrDefault();
        var active = x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now));
        var address = x.AccountAddress is null ? null : new ManagerPatientAddressResponse(x.AccountAddress.PostalCode, x.AccountAddress.Street, x.AccountAddress.Number, x.AccountAddress.Complement, x.AccountAddress.District, x.AccountAddress.City, x.AccountAddress.StateCode);
        return new(x.Id, x.FullName, x.PatientProfile?.PreferredName, x.TaxId ?? x.PatientProfile?.TaxId, x.Email, x.PhoneE164, x.EmailVerified, x.PhoneVerified,
            x.PatientProfile?.BirthDate is { } birth ? DateOnly.FromDateTime(birth) : null, address,
            x.StatusCode, active, premium?.StatusCode ?? "none", premium?.StatusCode == "pending" ? premium.Id : null, appointments.Length,
            appointments.Where(a => a.StartsAtUtc < now).Select(a => (DateTime?)a.StartsAtUtc).DefaultIfEmpty().Max(),
            appointments.Where(a => a.StartsAtUtc >= now && a.StatusCode is "pending" or "confirmed" or "arrived" or "in_progress").Select(a => (DateTime?)a.StartsAtUtc).DefaultIfEmpty().Min(), x.RowVersion);
    }
    private static ManagerPremiumRequestResponse MapPremium(PremiumMembership x) => new(x.Id, x.AccountId, x.Account.FullName, x.PremiumPlan.Name,
        x.PremiumPlan.AppointmentDiscountPercent, x.StatusCode, x.ProofDocumentId, x.ProofDocument?.OriginalFileName, x.ProofDocument?.SizeBytes,
        x.ReviewNotes, x.RejectionReason, x.CreatedAtUtc, x.ReviewedAtUtc, x.RowVersion);
    private async Task<TimeZoneInfo> TimezoneAsync(CancellationToken ct) { var name = await database.Clinics.AsNoTracking().Select(x => x.TimezoneName).SingleOrDefaultAsync(ct) ?? "America/Sao_Paulo"; try { return TimeZoneInfo.FindSystemTimeZoneById(name); } catch (TimeZoneNotFoundException) { throw new ManagerRuleException(503, "Fuso horário indisponível."); } }
    private static DateTime StartUtc(DateOnly date, TimeZoneInfo timezone) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), timezone);
    private static bool IsInsideLocalTimeRange(DateTime startsAtUtc, TimeZoneInfo timezone, TimeOnly start, TimeOnly end)
    {
        var utc = DateTime.SpecifyKind(startsAtUtc, DateTimeKind.Utc);
        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, timezone));
        return local >= start && local < end;
    }
    private async Task SaveAsync(CancellationToken ct) { try { await database.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict("Os dados foram alterados por outra sessão."); } catch (DbUpdateException) { throw Conflict("A operação não pôde ser concluída."); } }
    private static void Page(int page, int size) { if (page < 1 || size is < 1 or > 100) throw Invalid("Paginação inválida."); }
    private static void RequireVersion(ulong current, ulong supplied) { if (supplied == 0 || current != supplied) throw Conflict("Os dados foram alterados por outra sessão."); }
    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ValidateIdempotency(string key) { if (string.IsNullOrWhiteSpace(key) || key.Length is < 16 or > 100 || key.Any(c => c > 127 || char.IsWhiteSpace(c) || char.IsControl(c))) throw Invalid("Idempotency-Key inválida."); }
    private static Guid GuidFromKey(string key) { var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key)); return new Guid(bytes.AsSpan(0, 16)); }
    internal static ManagerRuleException Invalid(string message) => new(400, message);
    internal static ManagerRuleException Forbidden(string message) => new(403, message);
    internal static ManagerRuleException Missing() => new(404, "Recurso não encontrado.");
    internal static ManagerRuleException Conflict(string message) => new(409, message);
    private sealed record ManagerAgendaCandidate(ulong Id, DateTime StartsAtUtc);
    private sealed record PatientHistoryRow(ulong PatientAccountId, DateTime StartsAtUtc, string StatusCode);
}
