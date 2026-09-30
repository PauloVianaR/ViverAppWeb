using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.ClinicAdministration;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.CashManagement;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.ManagerExperience;

public sealed class ManagerExperienceService(ViverAppDbContext database, UserManager<ViverAppUser> users,
    IdentityChallengeService challenges, IClinicalOperationsAuditWriter audit, TimeProvider clock, CashManagementService? cash = null)
{
    private static readonly string[] AppointmentStatuses = ["pending", "confirmed", "arrived", "in_progress", "completed", "canceled", "rescheduled", "no_show"];
    private static readonly string[] PaymentFilters = ["paid", "pending"];
    private static readonly string[] Methods = ["credit_card", "debit_card", "pix", "cash"];

    public async Task<ManagerCapabilitiesResponse> CapabilitiesAsync(CancellationToken ct)
    {
        var values = await database.ApplicationSettings.AsNoTracking()
            .Where(item => item.SettingKey == "manager.appointment_types_enabled"
                || item.SettingKey == "manager.professional_schedules_enabled"
                || item.SettingKey == "manager.professional_services_enabled"
                || item.SettingKey == "premium.manager_can_manage"
                || item.SettingKey == "manager.medical_records_write_enabled"
                || item.SettingKey == "cash.manager_can_reopen"
                || item.SettingKey == "cash.manager_can_view_cumulative_totals")
            .ToDictionaryAsync(item => item.SettingKey, item => item.ValueJson, ct);
        return new(
            ReadManagerCapability(values, "manager.appointment_types_enabled"),
            ReadManagerCapability(values, "manager.professional_schedules_enabled"),
            ReadManagerCapability(values, "manager.professional_services_enabled"),
            ReadManagerCapability(values, "premium.manager_can_manage"),
            ReadManagerCapability(values, "manager.medical_records_write_enabled"),
            ReadManagerCapability(values, "cash.manager_can_reopen"),
            ReadManagerCapability(values, "cash.manager_can_view_cumulative_totals"));
    }

    public async Task<ManagerHomeResponse> HomeAsync(ulong actor, CancellationToken ct)
    {
        var profile = await ProfileAsync(actor, ct);
        var timezone = await TimezoneAsync(ct);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timezone).DateTime);
        var from = StartUtc(localToday, timezone); var to = StartUtc(localToday.AddDays(1), timezone);
        var rows = await AppointmentQuery().Where(x => x.StartsAtUtc >= from && x.StartsAtUtc < to)
            .OrderBy(x => x.StartsAtUtc).ToArrayAsync(ct);
        var mapped = rows.Select(MapAppointment).ToArray();
        var sources = new ManagerHomeSources(rows.Select(x => x.AppointmentNumber).ToArray(),
            rows.GroupBy(x => x.ProfessionalAccountId).Select(x => x.First().ProfessionalAccount.Account.FullName).OrderBy(x => x).ToArray(),
            rows.Where(x => x.CurrentPayment?.StatusCode == "paid").Select(x => x.AppointmentNumber).ToArray(),
            rows.Where(x => x.RequiresPayment && x.CurrentPayment?.StatusCode != "paid").Select(x => x.AppointmentNumber).ToArray(),
            rows.Where(x => x.ModalityCode == "online").Select(x => x.AppointmentNumber).ToArray(),
            rows.Where(x => x.ModalityCode == "in_person").Select(x => x.AppointmentNumber).ToArray());
        return new(profile, new(rows.Length, rows.Select(x => x.ProfessionalAccountId).Distinct().Count(),
            rows.Count(x => x.CurrentPayment != null && x.CurrentPayment.StatusCode == "paid"),
            rows.Count(x => x.RequiresPayment && (x.CurrentPayment == null || x.CurrentPayment.StatusCode != "paid")),
            rows.Count(x => x.ModalityCode == "online"), rows.Count(x => x.ModalityCode == "in_person")), sources, mapped);
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
        await database.ProfessionalProfiles.AsNoTracking().Where(x => x.Account.StatusCode == "active")
            .OrderBy(x => x.Account.FullName).Select(x => new ManagerDoctorOption(x.AccountId, x.Account.FullName,
                x.LicenseTypeCode + " " + x.LicenseStateCode + " " + x.LicenseNumber)).ToArrayAsync(ct);

    public async Task<IReadOnlyList<ManagerServiceOption>> ServicesAsync(CancellationToken ct) =>
        await database.AppointmentTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new ManagerServiceOption(x.Id, x.Name, x.Description, x.CategoryCode, x.ModalityCode, x.DurationMinutes, x.PriceAmount, x.RequiresPayment)).ToArrayAsync(ct);

    public async Task<ManagerAgendaResponse> AgendaAsync(DateOnly from, DateOnly to, string? status, string? modality,
        string? category, ulong? doctor, ulong? appointmentNumber, string? payment, TimeOnly? startTime, TimeOnly? endTime, string? search,
        string sort, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); if (to < from || to.DayNumber - from.DayNumber > 366) throw Invalid("O período deve ter no máximo 367 dias.");
        if (status is not null && !AppointmentStatuses.Contains(status, StringComparer.Ordinal)) throw Invalid("Estado inválido.");
        if (modality is not null && modality is not ("online" or "in_person")) throw Invalid("Modalidade inválida.");
        if (category is not null && category is not ("consultation" or "examination" or "surgery" or "procedure")) throw Invalid("Tipo inválido.");
        if (payment is not null && !PaymentFilters.Contains(payment, StringComparer.Ordinal)) throw Invalid("Filtro de pagamento inválido.");
        if (startTime.HasValue != endTime.HasValue || startTime >= endTime) throw Invalid("O intervalo de horário é inválido.");
        var timezone = await TimezoneAsync(ct); var fromUtc = StartUtc(from, timezone); var toUtc = StartUtc(to.AddDays(1), timezone);
        var query = AppointmentQuery().Where(x => x.StartsAtUtc >= fromUtc && x.StartsAtUtc < toUtc);
        if (status == "rescheduled") query = query.Where(x => x.AppointmentRescheduleHistories.Any() || x.RescheduledFromAppointmentId != null);
        else if (status is not null) query = query.Where(x => x.StatusCode == status);
        if (modality is not null) query = query.Where(x => x.ModalityCode == modality);
        if (category is not null) query = query.Where(x => x.AppointmentType.CategoryCode == category);
        if (doctor.HasValue) query = query.Where(x => x.ProfessionalAccountId == doctor);
        if (appointmentNumber.HasValue) query = query.Where(x => x.AppointmentNumber == appointmentNumber);
        if (payment == "paid") query = query.Where(x => x.CurrentPayment != null && x.CurrentPayment.StatusCode == "paid");
        if (payment == "pending") query = query.Where(x => x.RequiresPayment && (x.CurrentPayment == null || x.CurrentPayment.StatusCode != "paid"));
        var term = Text(search); if (term is not null) { if (term.Length > 120) throw Invalid("A busca deve ter no máximo 120 caracteres."); var isNumber = ulong.TryParse(term, out var number); query = query.Where(x => x.PatientAccount.FullName.Contains(term) || x.ProfessionalAccount.Account.FullName.Contains(term) || x.AppointmentType.Name.Contains(term) || isNumber && x.AppointmentNumber == number); }
        query = sort switch { "date_desc" => query.OrderByDescending(x => x.StartsAtUtc), "patient" => query.OrderBy(x => x.PatientAccount.FullName).ThenBy(x => x.StartsAtUtc), "doctor" => query.OrderBy(x => x.ProfessionalAccount.Account.FullName).ThenBy(x => x.StartsAtUtc), _ => query.OrderBy(x => x.StartsAtUtc) };
        var summaries = await query.Select(x => new ManagerAgendaSummary(
            x.Id, x.AppointmentNumber, x.StartsAtUtc, x.ModalityCode, x.RequiresPayment,
            x.AppointmentRescheduleHistories.Any() || x.RescheduledFromAppointmentId != null,
            x.CurrentPayment != null && x.CurrentPayment.StatusCode == "paid")).ToArrayAsync(ct);
        var filtered = startTime.HasValue
            ? summaries.Where(x => IsInsideLocalTimeRange(x.StartsAtUtc, timezone, startTime.Value, endTime!.Value)).ToArray()
            : summaries;
        var total = filtered.Length;
        var pageIds = filtered.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToArray();
        Appointment[] rows;
        if (startTime.HasValue)
        {
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
            rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        }
        var sources = new ManagerAgendaSources(filtered.Select(x => x.AppointmentNumber).ToArray(),
            filtered.Where(x => x.ModalityCode == "online").Select(x => x.AppointmentNumber).ToArray(),
            filtered.Where(x => x.ModalityCode == "in_person").Select(x => x.AppointmentNumber).ToArray(),
            filtered.Where(x => x.Rescheduled).Select(x => x.AppointmentNumber).ToArray(),
            filtered.Where(x => x.Paid).Select(x => x.AppointmentNumber).ToArray(),
            filtered.Where(x => x.RequiresPayment && !x.Paid).Select(x => x.AppointmentNumber).ToArray());
        var counters = new ManagerAgendaCounters(total, sources.Online.Count, sources.InPerson.Count, sources.Rescheduled.Count, sources.Paid.Count, sources.PendingPayment.Count);
        return new(counters, sources, new(rows.Select(MapAppointment).ToArray(), page, pageSize, total));
    }

    public async Task<ManagerAppointmentResponse> AppointmentAsync(ulong id, CancellationToken ct) =>
        MapAppointment(await AppointmentQuery().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing());

    public async Task<ManagerPatientsResponse> PatientsAsync(string? search, string? status, bool? premium, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); if (status is not null && status is not ("active" or "blocked" or "pending_confirmation")) throw Invalid("Estado inválido.");
        var now = clock.GetUtcNow().UtcDateTime;
        IQueryable<Account> query = database.Accounts.AsNoTracking().Where(x => x.RoleCode == ViverAppRoles.Patient)
            .Include(x => x.PatientProfile).Include(x => x.AccountAddress)
            .Include(x => x.PremiumMembershipAccounts).ThenInclude(x => x.ProofDocument)
            .Include(x => x.PremiumMembershipAccounts).ThenInclude(x => x.PremiumPlan);
        if (status is not null) query = query.Where(x => x.StatusCode == status);
        if (premium.HasValue) query = premium.Value ? query.Where(x => x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now))) : query.Where(x => !x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now)));
        var term = Text(search); if (term is not null) { if (term.Length > 120) throw Invalid("A busca deve ter no máximo 120 caracteres."); var taxTerm = new string(term.Where(char.IsAsciiDigit).ToArray()); var hasTaxTerm = taxTerm.Length >= 3; query = query.Where(x => x.FullName.Contains(term) || x.Email != null && x.Email.Contains(term) || x.PhoneE164 != null && x.PhoneE164.Contains(term) || hasTaxTerm && x.TaxId != null && x.TaxId.Contains(taxTerm)); }
        var total = await query.CountAsync(ct); var accounts = await query.OrderBy(x => x.FullName).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var ids = accounts.Select(x => x.Id).ToArray(); var appointments = await database.Appointments.AsNoTracking().Where(x => ids.Contains(x.PatientAccountId) && x.InverseRescheduledFromAppointment == null).Select(x => new PatientHistoryRow(x.PatientAccountId, x.StartsAtUtc, x.StatusCode)).ToArrayAsync(ct);
        var items = accounts.Select(x => MapPatient(x, appointments.Where(a => a.PatientAccountId == x.Id), now)).ToArray();
        var baseQuery = database.Accounts.AsNoTracking().Where(x => x.RoleCode == ViverAppRoles.Patient);
        var counters = new ManagerPatientCounters(await baseQuery.CountAsync(ct), await baseQuery.CountAsync(x => x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now)), ct), await baseQuery.CountAsync(x => x.StatusCode == "active", ct), await baseQuery.CountAsync(x => x.StatusCode == "blocked", ct), await baseQuery.CountAsync(x => x.PremiumMembershipAccounts.Any(m => m.StatusCode == "pending"), ct));
        var sources = new ManagerPatientSources(await baseQuery.OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct),
            await baseQuery.Where(x => x.PremiumMembershipAccounts.Any(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now))).OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct),
            await baseQuery.Where(x => x.StatusCode == "active").OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct),
            await baseQuery.Where(x => x.StatusCode == "blocked").OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct),
            await baseQuery.Where(x => x.PremiumMembershipAccounts.Any(m => m.StatusCode == "pending")).OrderBy(x => x.FullName).Select(x => x.FullName).ToArrayAsync(ct));
        return new(counters, sources, new(items, page, pageSize, total));
    }

    public async Task<ManagerPatientResponse> PatientAsync(ulong id, CancellationToken ct)
    {
        var account = await database.Accounts.AsNoTracking().Where(x => x.Id == id && x.RoleCode == ViverAppRoles.Patient)
            .Include(x => x.PatientProfile).Include(x => x.AccountAddress)
            .Include(x => x.PremiumMembershipAccounts).ThenInclude(x => x.ProofDocument)
            .Include(x => x.PremiumMembershipAccounts).ThenInclude(x => x.PremiumPlan)
            .SingleOrDefaultAsync(ct) ?? throw Missing();
        var appointments = await database.Appointments.AsNoTracking().Where(x => x.PatientAccountId == id && x.InverseRescheduledFromAppointment == null).Select(x => new PatientHistoryRow(x.PatientAccountId, x.StartsAtUtc, x.StatusCode)).ToArrayAsync(ct);
        return MapPatient(account, appointments, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<ManagerPatientResponse> CreatePatientAsync(ulong actor, ManagerPatientCreateRequest request, CancellationToken ct)
    {
        var taxId = Text(request.TaxId);
        if (taxId is null || !BrazilianDocumentValidator.IsValidCpf(taxId)) throw Invalid("O CPF informado é inválido.");
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        if (request.BirthDate > today || request.BirthDate < today.AddYears(-125)) throw Invalid("A data de nascimento é inválida.");
        var rawEmail = Text(request.Email); var email = rawEmail is null ? null : IdentifierNormalizer.NormalizeEmail(rawEmail);
        if (rawEmail is not null && email is null) throw Invalid("O e-mail informado é inválido.");
        var rawPhone = Text(request.PhoneE164); var phone = rawPhone is null ? null : IdentifierNormalizer.NormalizePhone(rawPhone);
        if (rawPhone is not null && phone is null) throw Invalid("Informe um telefone brasileiro válido com DDD e 10 ou 11 dígitos.");
        if (request.SendOnboarding && email is null && phone is null) throw Invalid("Informe um e-mail ou telefone para enviar o onboarding.");
        if (await database.Accounts.AnyAsync(x => x.TaxId == taxId || email != null && x.NormalizedEmail == email || phone != null && x.PhoneE164 == phone, ct)
            || await database.PatientProfiles.AnyAsync(x => x.TaxId == taxId, ct))
            throw Conflict("CPF, e-mail ou telefone já pertence a outro paciente. Nenhuma alteração foi feita.");
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var channel = email is not null ? "email" : "sms";
        var account = new Account
        {
            RoleCode = ViverAppRoles.Patient,
            StatusCode = request.SendOnboarding ? "pending_confirmation" : "active",
            FullName = request.FullName.Trim(),
            Email = rawEmail,
            NormalizedEmail = email,
            PhoneE164 = phone,
            TaxId = taxId,
            BirthDate = request.BirthDate.ToDateTime(TimeOnly.MinValue),
            PortalAccessEnabled = request.SendOnboarding,
            PreferredRecoveryChannel = request.SendOnboarding ? channel : null,
            SecurityStamp = RandomNumberGenerator.GetBytes(32),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        };
        database.Accounts.Add(account);
        database.PatientProfiles.Add(new PatientProfile { Account = account, TaxId = taxId, BirthDate = account.BirthDate, PreferredName = Text(request.PreferredName), CreatedAtUtc = now, UpdatedAtUtc = now });
        if (request.Address is not null)
            database.AccountAddresses.Add(new AccountAddress { Account = account, PostalCode = Text(request.Address.PostalCode), Street = Text(request.Address.Street), Number = Text(request.Address.Number), Complement = Text(request.Address.Complement), District = Text(request.Address.District), City = Text(request.Address.City), StateCode = Text(request.Address.StateCode)?.ToUpperInvariant(), UpdatedAtUtc = now });
        await database.SaveChangesAsync(ct);
        if (request.SendOnboarding)
        {
            var user = await users.FindByIdAsync(account.Id.ToString(CultureInfo.InvariantCulture)) ?? throw Conflict("Não foi possível iniciar o onboarding.");
            await challenges.CreateAsync(user, "contact_verification", channel, email ?? phone!, ct);
        }
        var actorRole = await database.Accounts.AsNoTracking().Where(x => x.Id == actor).Select(x => x.RoleCode).SingleAsync(ct);
        var eventCode = actorRole == ViverAppRoles.Administrator
            ? request.SendOnboarding ? "administrator.patient.invited" : "administrator.patient.registered"
            : request.SendOnboarding ? "manager.patient.invited" : "manager.patient.registered";
        await audit.WriteAsync(eventCode, actor, "account", account.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["portalAccessEnabled"] = request.SendOnboarding.ToString(), ["channel"] = request.SendOnboarding ? channel : "none" }, ct);
        await transaction.CommitAsync(ct); return await PatientAsync(account.Id, ct);
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
        if (phone is not null && normalizedPhone is null) throw Invalid("Informe um telefone brasileiro válido com DDD e 10 ou 11 dígitos.");
        if (account.PortalAccessEnabled && normalizedEmail is null && normalizedPhone is null)
            throw Invalid("Desative o onboarding antes de remover todos os contatos de uma conta com acesso ao portal.");
        if (request.BirthDate > DateOnly.FromDateTime(now) || request.BirthDate < DateOnly.FromDateTime(now).AddYears(-125)) throw Invalid("A data de nascimento é inválida.");
        if (await database.Accounts.AnyAsync(x => x.Id != id && ((taxId != null && x.TaxId == taxId) || (normalizedEmail != null && x.NormalizedEmail == normalizedEmail) || (normalizedPhone != null && x.PhoneE164 == normalizedPhone)), ct)
            || taxId is not null && await database.PatientProfiles.AnyAsync(x => x.AccountId != id && x.TaxId == taxId, ct))
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
        else if (account.AccountAddress is null) account.AccountAddress = new AccountAddress { AccountId = id, PostalCode = Text(request.Address.PostalCode), Street = Text(request.Address.Street), Number = Text(request.Address.Number), Complement = Text(request.Address.Complement), District = Text(request.Address.District), City = Text(request.Address.City), StateCode = Text(request.Address.StateCode)?.ToUpperInvariant(), UpdatedAtUtc = now };
        else { account.AccountAddress.PostalCode = Text(request.Address.PostalCode); account.AccountAddress.Street = Text(request.Address.Street); account.AccountAddress.Number = Text(request.Address.Number); account.AccountAddress.Complement = Text(request.Address.Complement); account.AccountAddress.District = Text(request.Address.District); account.AccountAddress.City = Text(request.Address.City); account.AccountAddress.StateCode = Text(request.Address.StateCode)?.ToUpperInvariant(); account.AccountAddress.UpdatedAtUtc = now; }
        await SaveAsync(ct);
        var user = await users.FindByIdAsync(id.ToString(CultureInfo.InvariantCulture));
        if (account.PortalAccessEnabled && user is not null && emailChanged && normalizedEmail is not null) await challenges.CreateAsync(user, "contact_verification", "email", normalizedEmail, ct);
        if (account.PortalAccessEnabled && user is not null && phoneChanged && normalizedPhone is not null) await challenges.CreateAsync(user, "contact_verification", "sms", normalizedPhone, ct);
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
        var contact = await database.Accounts.AsNoTracking().Where(x => x.Id == item.AccountId)
            .Select(x => new { x.Email, x.EmailVerified, x.PhoneE164, x.PhoneVerified }).SingleAsync(ct);
        var channel = contact.EmailVerified && contact.Email is not null ? "email"
            : contact.PhoneVerified && contact.PhoneE164 is not null ? "sms" : null;
        var recipient = channel == "email" ? contact.Email : contact.PhoneE164;
        if (channel is not null && recipient is not null)
            database.OutboxMessages.Add(new OutboxMessage
            {
                AccountId = item.AccountId,
                ChannelCode = channel,
                TemplateKey = request.Approve ? "manager.premium.approved" : "manager.premium.rejected",
                TemplateVersion = 1,
                Recipient = recipient,
                PayloadJson = JsonSerializer.Serialize(new { membershipId = id, status = item.StatusCode }),
                StatusCode = "pending",
                IdempotencyKey = Guid.NewGuid(),
                AttemptCount = 0,
                MaxAttempts = 5,
                NextAttemptAtUtc = now,
                CreatedAtUtc = now,
            });
        await database.SaveChangesAsync(ct); await audit.WriteAsync(request.Approve ? "manager.premium.approved" : "manager.premium.rejected", actor, "premium_membership", id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string> { ["previousStatus"] = "pending", ["newStatus"] = item.StatusCode }, ct);
        await transaction.CommitAsync(ct); return await PremiumRequestAsync(id, ct);
    }

    public async Task<ManagerPatientResponse> ActivatePremiumAsync(ulong actor, ulong patientId,
        PrivateDocument proof, bool administratorOverride, CancellationToken ct)
    {
        if (!administratorOverride && !await CapabilityEnabledAsync("premium.manager_can_manage", true, ct))
            throw Forbidden("A gestão de pacientes Premium está desabilitada pelo Administrador.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var patient = await database.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE id={patientId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (patient is null || patient.RoleCode != ViverAppRoles.Patient) throw Missing();
        var now = clock.GetUtcNow().UtcDateTime;
        var open = await database.PremiumMemberships.AnyAsync(x => x.AccountId == patientId
            && (x.StatusCode == "pending" || x.StatusCode == "active" && (x.EndsAtUtc == null || x.EndsAtUtc > now)), ct);
        if (open) throw Conflict("O paciente já possui uma solicitação ou benefício Premium ativo.");
        var plan = await database.PremiumPlans.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).FirstOrDefaultAsync(ct)
            ?? throw Conflict("Não existe plano Premium ativo configurado.");
        proof.OwnerAccountId = patientId;
        database.PrivateDocuments.Add(proof);
        var membership = new PremiumMembership
        {
            AccountId = patientId,
            PremiumPlanId = plan.Id,
            StatusCode = "active",
            StartsAtUtc = now,
            EndsAtUtc = plan.ValidityDays.HasValue ? now.AddDays(plan.ValidityDays.Value) : null,
            ProofDocumentId = proof.Id,
            ReviewedAtUtc = now,
            ReviewedByAccountId = actor,
            ReviewNotes = administratorOverride ? "Ativado pelo Administrador" : "Ativado pelo Gestor",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.PremiumMemberships.Add(membership);
        await SaveAsync(ct);
        await audit.WriteAsync(administratorOverride ? "administrator.patient.premium_activated" : "manager.patient.premium_activated",
            actor, "premium_membership", membership.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["patientId"] = patientId.ToString(CultureInfo.InvariantCulture), ["proofDocumentId"] = proof.Id.ToString("D") }, ct);
        await transaction.CommitAsync(ct);
        return await PatientAsync(patientId, ct);
    }

    public async Task<ManagerPatientResponse> DeactivatePremiumAsync(ulong actor, ulong patientId,
        ManagerPremiumCancelRequest request, bool administratorOverride, CancellationToken ct)
    {
        if (!administratorOverride && !await CapabilityEnabledAsync("premium.manager_can_manage", true, ct))
            throw Forbidden("A gestão de pacientes Premium está desabilitada pelo Administrador.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var membership = await database.PremiumMemberships.Include(x => x.ProofDocument)
            .Where(x => x.AccountId == patientId && x.StatusCode == "active")
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct) ?? throw Missing();
        RequireVersion(membership.RowVersion, request.RowVersion);
        var now = clock.GetUtcNow().UtcDateTime;
        var proofId = membership.ProofDocumentId;
        if (membership.ProofDocument is not null)
        {
            membership.ProofDocument.StatusCode = "deleted";
            membership.ProofDocument.RowVersion++;
        }
        membership.ProofDocumentId = null;
        membership.StatusCode = "canceled";
        membership.EndsAtUtc = now;
        membership.RejectionReason = request.Reason.Trim();
        membership.ReviewedAtUtc = now;
        membership.ReviewedByAccountId = actor;
        membership.UpdatedAtUtc = now;
        membership.RowVersion++;
        await SaveAsync(ct);
        await audit.WriteAsync(administratorOverride ? "administrator.patient.premium_deactivated" : "manager.patient.premium_deactivated",
            actor, "premium_membership", membership.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["patientId"] = patientId.ToString(CultureInfo.InvariantCulture), ["reason"] = request.Reason.Trim(), ["proofDocumentId"] = proofId?.ToString("D") ?? "none" }, ct);
        await transaction.CommitAsync(ct);
        return await PatientAsync(patientId, ct);
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
        await using var transaction = database.Database.CurrentTransaction is null
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        var stored = await database.IdempotencyRecords.SingleOrDefaultAsync(x => x.ScopeCode == scope && x.IdempotencyKey == key, ct);
        if (stored is not null) { if (!CryptographicOperations.FixedTimeEquals(stored.RequestHash, hash)) throw Conflict("A chave de idempotência já foi usada com outros dados."); var replay = JsonSerializer.Deserialize<ManagerPaymentResponse>(stored.ResponseBodyJson!); if (transaction is not null) await transaction.CommitAsync(ct); return replay ?? throw Conflict("Resposta idempotente inválida."); }
        var appointment = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id={appointmentId} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw Missing();
        if (appointment.RowVersion != request.AppointmentRowVersion) throw Conflict("O atendimento foi alterado por outra sessão.");
        if (appointment.ModalityCode != "in_person" || appointment.PaymentLocationCode != "clinic") throw Conflict("Somente pagamentos presenciais escolhidos para a clínica podem ser confirmados manualmente.");
        if (appointment.StatusCode is not ("pending" or "confirmed")) throw Conflict("O atendimento não aceita confirmação de pagamento.");
        if (!appointment.RequiresPayment) throw Conflict("Este atendimento não possui cobrança.");
        var payment = await database.Payments.FromSqlInterpolated($"SELECT * FROM payments WHERE appointment_id={appointmentId} AND active_appointment_id IS NOT NULL ORDER BY id DESC LIMIT 1 FOR UPDATE").SingleOrDefaultAsync(ct);
        if (payment is not null && payment.StatusCode == "paid") throw Conflict("O pagamento já foi reconciliado.");
        if (payment is not null && payment.ProviderCode != "internal") throw Conflict("Existe uma cobrança online vinculada; faça a reconciliação pelo provedor.");
        payment ??= new Payment { AppointmentId = appointmentId, AppointmentRequiresPayment = true, SupersedesPaymentId = appointment.CurrentPaymentId, ProviderCode = "internal", StatusCode = "pending", Amount = appointment.PriceAmount, CurrencyCode = "BRL", IdempotencyKey = GuidFromKey(key), CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = 1, ProviderReferenceAppointmentId = appointmentId };
        if (payment.Id == 0) database.Payments.Add(payment);
        payment.StatusCode = "paid"; payment.Amount = appointment.PriceAmount; payment.MethodCode = request.MethodCode; payment.PaidAtUtc = paidAt; payment.ConfirmedByAccountId = actor; payment.CardLastFour = request.CardLastFour; payment.AuthorizationReference = Text(request.AuthorizationReference); payment.UpdatedAtUtc = now; if (payment.Id != 0) payment.RowVersion++;
        var previous = appointment.StatusCode; if (appointment.StatusCode == "pending") { appointment.StatusCode = "confirmed"; appointment.UpdatedAtUtc = now; appointment.RowVersion++; database.AppointmentStatusHistories.Add(new AppointmentStatusHistory { AppointmentId = appointment.Id, ActorAccountId = actor, FromStatusCode = previous, ToStatusCode = "confirmed", Reason = "Pagamento presencial confirmado", StartsAtUtc = appointment.StartsAtUtc, EndsAtUtc = appointment.EndsAtUtc, OccurredAtUtc = now }); }
        await database.SaveChangesAsync(ct);
        appointment.CurrentPaymentId = payment.Id;
        if (cash is not null) await cash.RecordPaymentReceivedAsync(payment, actor, paidAt, ct);
        database.PaymentEvents.Add(new PaymentEvent { PaymentId = payment.Id, SourceCode = "manual", ProviderStatusCode = request.MethodCode, NormalizedStatusCode = "paid", EventFingerprint = SHA256.HashData(Encoding.UTF8.GetBytes($"{actor}:{appointmentId}:{key}")), ProviderOccurredAtUtc = paidAt, OccurredAtUtc = now, WasApplied = true });
        var response = new ManagerPaymentResponse(payment.Id, appointmentId, payment.StatusCode, payment.Amount, payment.MethodCode!, paidAt, payment.CardLastFour, payment.AuthorizationReference, payment.RowVersion);
        database.IdempotencyRecords.Add(new IdempotencyRecord { ScopeCode = scope, IdempotencyKey = key, RequestHash = hash, ResponseStatusCode = 200, ResponseBodyJson = JsonSerializer.Serialize(response), CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(24) });
        await database.SaveChangesAsync(ct); await audit.WriteAsync("manager.payment.confirmed", actor, "payment", payment.Id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string> { ["appointmentId"] = appointmentId.ToString(CultureInfo.InvariantCulture), ["method"] = request.MethodCode, ["previousStatus"] = previous, ["newStatus"] = appointment.StatusCode }, ct);
        if (transaction is not null) await transaction.CommitAsync(ct); return response;
    }

    private IQueryable<Appointment> AppointmentQuery() => database.Appointments.AsNoTracking()
        .Where(x => x.InverseRescheduledFromAppointment == null)
        .Include(x => x.PatientAccount).Include(x => x.ProfessionalAccount).ThenInclude(x => x.Account).Include(x => x.AppointmentType)
        .Include(x => x.CurrentPayment).Include(x => x.AppointmentReview).Include(x => x.MedicalReport).ThenInclude(x => x!.MedicalReportVersions)
        .Include(x => x.AppointmentDocuments).Include(x => x.InverseRescheduledFromAppointment).Include(x => x.AppointmentRescheduleHistories);
    private static ManagerAppointmentResponse MapAppointment(Appointment x) => new(x.Id, x.AppointmentNumber, x.PatientAccountId, x.PatientAccount.FullName, x.PatientAccount.PhoneE164,
        x.ProfessionalAccountId, x.ProfessionalAccount.Account.FullName, x.AppointmentTypeId, x.AppointmentType.Name, x.AppointmentType.CategoryCode, x.StatusCode,
        x.ModalityCode, x.StartsAtUtc, x.EndsAtUtc, x.PriceAmount, x.BasePriceAmount ?? x.PriceAmount, x.DiscountPercent, x.RequiresPayment, x.PaymentLocationCode, x.PatientNotes, x.CancellationReason,
        x.RescheduledFromAppointmentId, x.InverseRescheduledFromAppointment?.Id, x.AppointmentReview?.Rating, x.AppointmentReview?.Comment,
        new(x.CurrentPayment?.Id, x.CurrentPayment?.StatusCode ?? "unpaid", x.CurrentPayment?.MethodCode, x.CurrentPayment?.PaidAtUtc,
            x.CurrentPayment?.CardLastFour, x.CurrentPayment?.AuthorizationReference, x.CurrentPayment?.RowVersion ?? 0),
        new(x.MedicalReport is not null, x.MedicalReport?.StatusCode, (uint)(x.MedicalReport?.MedicalReportVersions.Count ?? 0), x.MedicalReport?.PublishedAtUtc),
        x.AppointmentDocuments.Count(d => d.StatusCode == "available"), x.ArrivedAtUtc,
        x.ArrivalBusinessDate is { } arrivalDate ? DateOnly.FromDateTime(arrivalDate) : null, x.ArrivalQueueNumber,
        x.AppointmentRescheduleHistories.OrderBy(h => h.SequenceNumber).Select(h => new AppointmentRescheduleHistoryResponse(h.SequenceNumber,
            h.PreviousStartsAtUtc, h.PreviousEndsAtUtc, h.NewStartsAtUtc, h.NewEndsAtUtc, h.Reason, h.OccurredAtUtc)).ToArray(),
        x.ModalityCode == "in_person" && x.StatusCode == "confirmed", x.StatusCode == "arrived", x.StatusCode is "pending" or "confirmed", x.StatusCode is "pending" or "confirmed",
        x.StatusCode is "confirmed" or "arrived" or "in_progress", x.StatusCode == "completed",
        x.RequiresPayment && x.CurrentPayment?.StatusCode == "paid" && x.StatusCode is ("confirmed" or "arrived" or "no_show"),
        x.RequiresPayment && x.ModalityCode == "in_person" && x.PaymentLocationCode == "clinic" && x.StatusCode is "pending" or "confirmed" && x.CurrentPayment?.StatusCode is not ("paid" or "reversal_pending"), x.RowVersion);
    private static ManagerProfileResponse MapProfile(Account x) => new(x.Id, x.FullName, x.Email, x.PhoneE164, x.TaxId, x.EmailVerified, x.PhoneVerified,
        x.ManagerPreference?.EmailEnabled ?? true, x.ManagerPreference?.SmsEnabled ?? true, x.RowVersion, x.ManagerPreference?.RowVersion ?? 1);
    private static ManagerPatientResponse MapPatient(Account x, IEnumerable<PatientHistoryRow> history, DateTime now)
    {
        var appointments = history.ToArray(); var premium = x.PremiumMembershipAccounts.OrderByDescending(m => m.CreatedAtUtc).FirstOrDefault();
        var activeMembership = x.PremiumMembershipAccounts.Where(m => m.StatusCode == "active" && m.StartsAtUtc <= now && (m.EndsAtUtc == null || m.EndsAtUtc > now))
            .OrderByDescending(m => m.CreatedAtUtc).FirstOrDefault();
        var active = activeMembership is not null;
        var address = x.AccountAddress is null ? null : new ManagerPatientAddressResponse(x.AccountAddress.PostalCode, x.AccountAddress.Street, x.AccountAddress.Number, x.AccountAddress.Complement, x.AccountAddress.District, x.AccountAddress.City, x.AccountAddress.StateCode);
        return new(x.Id, x.FullName, x.PatientProfile?.PreferredName, x.TaxId ?? x.PatientProfile?.TaxId, x.Email, x.PhoneE164, x.EmailVerified, x.PhoneVerified,
            x.PatientProfile?.BirthDate is { } birth ? DateOnly.FromDateTime(birth) : null, address,
            x.StatusCode, x.PortalAccessEnabled, active, premium?.StatusCode ?? "none", premium?.StatusCode == "pending" ? premium.Id : null,
            activeMembership?.Id, activeMembership?.RowVersion, activeMembership?.ProofDocumentId, activeMembership?.ProofDocument?.OriginalFileName,
            activeMembership?.PremiumPlan?.AppointmentDiscountPercent ?? 0, appointments.Length,
            appointments.Where(a => a.StartsAtUtc < now).Select(a => (DateTime?)a.StartsAtUtc).DefaultIfEmpty().Max(),
            appointments.Where(a => a.StartsAtUtc >= now && a.StatusCode is "pending" or "confirmed" or "arrived" or "in_progress").Select(a => (DateTime?)a.StartsAtUtc).DefaultIfEmpty().Min(), x.RowVersion);
    }
    private static ManagerPremiumRequestResponse MapPremium(PremiumMembership x) => new(x.Id, x.AccountId, x.Account.FullName, x.PremiumPlan.Name,
        x.PremiumPlan.AppointmentDiscountPercent, x.StatusCode, x.ProofDocumentId, x.ProofDocument?.OriginalFileName, x.ProofDocument?.SizeBytes,
        x.ReviewNotes, x.RejectionReason, x.CreatedAtUtc, x.ReviewedAtUtc, x.RowVersion);
    private async Task<TimeZoneInfo> TimezoneAsync(CancellationToken ct) { var name = await database.Clinics.AsNoTracking().Select(x => x.TimezoneName).SingleOrDefaultAsync(ct) ?? "America/Sao_Paulo"; try { return TimeZoneInfo.FindSystemTimeZoneById(name); } catch (TimeZoneNotFoundException) { throw new ManagerRuleException(503, "Fuso horário indisponível."); } }
    private static bool ReadManagerCapability(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var json) && ManagerFeatureGateFilter.TryReadBoolean(json, out var enabled) && enabled;
    private async Task<bool> CapabilityEnabledAsync(string key, bool fallback, CancellationToken ct)
    {
        var json = await database.ApplicationSettings.AsNoTracking().Where(x => x.SettingKey == key)
            .Select(x => x.ValueJson).SingleOrDefaultAsync(ct);
        return json is null ? fallback : ManagerFeatureGateFilter.TryReadBoolean(json, out var enabled) && enabled;
    }
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
    private sealed record ManagerAgendaSummary(ulong Id, ulong AppointmentNumber, DateTime StartsAtUtc,
        string ModalityCode, bool RequiresPayment, bool Rescheduled, bool Paid);
    private sealed record PatientHistoryRow(ulong PatientAccountId, DateTime StartsAtUtc, string StatusCode);
}
