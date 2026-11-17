using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.AdministratorExperience;

public sealed class AdministratorAccountService(ViverAppDbContext database, IdentityNotificationService notifications,
    IdentityAuditWriter audit, TimeProvider clock)
{
    public const string WelcomeEmailKey = "users.admin_registration_email_enabled";
    private static readonly HashSet<string> States = new("AC AL AP AM BA CE DF ES GO MA MT MS MG PA PB PR PE PI RJ RN RS RO RR SC SP SE TO".Split(' '), StringComparer.Ordinal);
    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static AdministratorRuleException Invalid(string message) => new(400, message);
    private static AdministratorRuleException Conflict(string message) => new(409, message);

    private async Task RequireAdministratorAsync(ulong actor, CancellationToken ct)
    {
        if (!await database.Accounts.AsNoTracking().AnyAsync(a => a.Id == actor && a.RoleCode == ViverAppRoles.Administrator && a.StatusCode == "active", ct))
            throw new AdministratorRuleException(403, "Somente o Administrador pode cadastrar ou editar usuários nesta área.");
    }

    private IQueryable<Account> Query() => database.Accounts.Include(a => a.PatientProfile).Include(a => a.AccountAddress)
        .Include(a => a.ProfessionalProfile!).ThenInclude(p => p.ProfessionalSpecialties);

    public async Task<AdministratorAccountResponse> GetAsync(ulong actor, ulong id, CancellationToken ct)
    {
        await RequireAdministratorAsync(actor, ct);
        return Map(await Query().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new AdministratorRuleException(404, "Usuário não encontrado."));
    }

    private async Task<(string? Email, string? Phone, uint[] Specialties)> ValidateAsync(string role, AdministratorAccountData data, ulong? id, CancellationToken ct)
    {
        if (!ViverAppRoles.All.Contains(role)) throw Invalid("Escolha um tipo de usuário válido.");
        if (Text(data.FullName) is not { Length: >= 3 and <= 200 }) throw Invalid("Informe o nome completo, com 3 a 200 caracteres.");
        var email = IdentifierNormalizer.NormalizeEmail(data.Email);
        if (id is null && email is null || Text(data.Email) is not null && email is null)
            throw Invalid("Informe um e-mail válido para o acesso e a recuperação de senha.");
        var phone = IdentifierNormalizer.NormalizePhone(data.Phone);
        var staff = role is ViverAppRoles.Doctor or ViverAppRoles.Psychologist or ViverAppRoles.Manager;
        if ((staff || Text(data.Phone) is not null) && phone is null) throw Invalid("Informe um telefone brasileiro válido com DDD.");
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if ((staff || id is null && role == ViverAppRoles.Patient) && data.BirthDate is null)
            throw Invalid("Informe a data de nascimento.");
        if (data.BirthDate is { } birth && (birth < new DateOnly(1900, 1, 1) || birth > today || staff && birth > today.AddYears(-18)))
            throw Invalid(staff ? "Médicos, psicólogos e gestores devem ter 18 anos ou mais e uma data de nascimento válida." : "Informe uma data de nascimento válida.");
        var taxId = Text(data.TaxId);
        if (role != ViverAppRoles.Administrator || taxId is not null)
        {
            if (!await PatientRegistrationPolicy.IsValidCpfAsync(database, role, taxId, ct)) throw Invalid("Informe um CPF válido. O cadastro de paciente sem CPF depende da configuração administrativa.");
        }
        if (await database.Accounts.AnyAsync(a => (!id.HasValue || a.Id != id.Value)
                && (taxId != null && a.TaxId == taxId || email != null && a.NormalizedEmail == email || phone != null && a.PhoneE164 == phone), ct)
            || taxId is not null && await database.PatientProfiles.AnyAsync(p => (!id.HasValue || p.AccountId != id.Value) && p.TaxId == taxId, ct))
            throw Conflict("CPF, e-mail ou telefone já pertence a outro usuário. Confira os dados antes de salvar.");
        if (role != ViverAppRoles.Patient && data.Patient is not null) throw Invalid("Dados de paciente são aceitos somente para o perfil Paciente.");
        if (data.Patient is { } patient)
        {
            if (patient.PreferredName?.Length > 120 || patient.AdministrativeNotes?.Length > 2000) throw Invalid("Revise o nome preferido e a observação do paciente.");
            if (patient.Address is { } address && (address.PostalCode is not null && (address.PostalCode.Length != 8 || address.PostalCode.Any(c => !char.IsAsciiDigit(c)))
                || address.StateCode is not null && !States.Contains(address.StateCode))) throw Invalid("Revise o CEP e a UF do endereço.");
        }
        if (role is not (ViverAppRoles.Doctor or ViverAppRoles.Psychologist))
        {
            if (data.Professional is not null) throw Invalid("Dados profissionais são aceitos somente para Médico ou Psicólogo.");
            return (email, phone, []);
        }
        var profile = data.Professional ?? throw Invalid("Preencha os dados profissionais obrigatórios.");
        var professional = profile.Registration ?? throw Invalid("Preencha o registro profissional.");
        if ((role == ViverAppRoles.Doctor ? professional.ProfessionalTitle is not ("Dr." or "Dra.") : professional.ProfessionalTitle is not ("Psic." or "Psicóloga"))
            || !States.Contains(professional.LicenseStateCode) || Text(professional.LicenseNumber) is not { Length: >= 1 and <= 30 }
            || professional.YearsExperience > 80 || profile.DefaultAppointmentDurationMinutes is < 5 or > 240 || profile.Biography?.Length > 4000)
            throw Invalid("Revise título, UF, número do CRM/CRP, experiência e duração padrão (5 a 240 minutos).");
        var specialties = (profile.SpecialtyIds ?? [professional.PrimarySpecialtyId]).Distinct().ToArray();
        if (specialties.Length is < 1 or > 100 || !specialties.Contains(professional.PrimarySpecialtyId)) throw Invalid("Selecione as especialidades e inclua a principal.");
        var active = await database.Specialties.AsNoTracking().Where(s => s.IsActive && specialties.Contains(s.Id)).Select(s => s.Id).ToArrayAsync(ct);
        if (active.Length != specialties.Length) throw Invalid("Uma especialidade selecionada não está disponível.");
        if (await database.ProfessionalProfiles.AnyAsync(p => (!id.HasValue || p.AccountId != id.Value)
            && p.LicenseTypeCode == (role == ViverAppRoles.Psychologist ? "CRP" : "CRM") && p.LicenseStateCode == professional.LicenseStateCode && p.LicenseNumber == professional.LicenseNumber.Trim(), ct))
            throw Conflict("Este CRM/CRP e UF já estão vinculados a outro profissional.");
        return (email, phone, specialties);
    }

    public async Task<AdministratorAccountSavedResponse> CreateAsync(ulong actor, AdministratorAccountCreateRequest request, CancellationToken ct)
    {
        await RequireAdministratorAsync(actor, ct);
        var validated = await ValidateAsync(request.RoleCode, request.Data, null, ct);
        await using var tx = database.Database.CurrentTransaction is null ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var now = clock.GetUtcNow().UtcDateTime;
        var account = new Account
        {
            RoleCode = request.RoleCode,
            StatusCode = "active",
            FullName = request.Data.FullName.Trim(),
            Email = request.Data.Email!.Trim(),
            NormalizedEmail = validated.Email,
            PhoneE164 = validated.Phone,
            TaxId = Text(request.Data.TaxId),
            BirthDate = request.Data.BirthDate?.ToDateTime(TimeOnly.MinValue),
            PortalAccessEnabled = true,
            PasswordSetupRequired = true,
            AdminEmailVerificationPending = true,
            PreferredRecoveryChannel = "email",
            SecurityStamp = RandomNumberGenerator.GetBytes(32),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
        database.Accounts.Add(account);
        ApplyDetails(account, request.Data, validated.Specialties, now);
        if (account.RoleCode == ViverAppRoles.Manager)
            database.ManagerPreferences.Add(new ManagerPreference { ManagerAccount = account, EmailEnabled = true, SmsEnabled = true, UpdatedAtUtc = now, RowVersion = 1 });
        if (account.RoleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist)
            database.ProfessionalPreferences.Add(new ProfessionalPreference { ProfessionalAccount = account.ProfessionalProfile!, EmailEnabled = true, SmsEnabled = true, OnlineEnabled = true, MaxOnlineDaily = 8, MaxInPersonDaily = 16, UpdatedAtUtc = now, RowVersion = 1 });
        await SaveAsync(ct);
        if (account.RoleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist or ViverAppRoles.Manager)
            database.ProfessionalReviews.Add(new ProfessionalReview { ProfessionalAccountId = account.Id, ReviewerAccountId = actor, DecisionCode = "approved", Reason = "Cadastro realizado diretamente pelo Administrador, sem necessidade de aprovação posterior.", OccurredAtUtc = now });
        var enabled = await database.ApplicationSettings.AsNoTracking().Where(s => s.SettingKey == WelcomeEmailKey).Select(s => s.ValueJson).SingleOrDefaultAsync(ct) == "true";
        if (enabled) await notifications.QueueAdministratorRegistrationAsync(account, ct);
        await audit.WriteAsync("administrator.user.created", actor, "account", account.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["role"] = account.RoleCode, ["welcomeEmailQueued"] = enabled.ToString(), ["passwordSetupRequired"] = "true" }, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new(Map(account), enabled);
    }

    public async Task<AdministratorAccountResponse> UpdateAsync(ulong actor, ulong id, AdministratorAccountUpdateRequest request, CancellationToken ct)
    {
        await RequireAdministratorAsync(actor, ct);
        await using var tx = database.Database.CurrentTransaction is null ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var account = await Query().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new AdministratorRuleException(404, "Usuário não encontrado.");
        if (account.RowVersion != request.RowVersion || account.ProfessionalProfile is { } p && request.Data.Professional?.RowVersion != p.RowVersion)
            throw Conflict("Este cadastro foi alterado por outra sessão. Recarregue antes de salvar.");
        var validated = await ValidateAsync(account.RoleCode, request.Data, id, ct);
        if (account.PortalAccessEnabled && validated.Email is null && validated.Phone is null || account.PasswordSetupRequired && validated.Email is null)
            throw Invalid("Mantenha um e-mail válido para definir a primeira senha e recuperar o acesso.");
        if (account.Id == actor && (account.NormalizedEmail != validated.Email || account.PhoneE164 != validated.Phone))
            throw Conflict("Para alterar seus próprios contatos, use Segurança da conta e confirme o novo contato. O MFA do Administrador será preservado.");
        var now = clock.GetUtcNow().UtcDateTime;
        var emailChanged = account.NormalizedEmail != validated.Email;
        var phoneChanged = account.PhoneE164 != validated.Phone;
        account.FullName = request.Data.FullName.Trim(); account.Email = Text(request.Data.Email); account.NormalizedEmail = validated.Email;
        account.PhoneE164 = validated.Phone; account.TaxId = Text(request.Data.TaxId); account.BirthDate = request.Data.BirthDate?.ToDateTime(TimeOnly.MinValue);
        if (emailChanged) { account.EmailVerified = false; account.AdminEmailVerificationPending = account.PortalAccessEnabled && validated.Email is not null; }
        if (phoneChanged) account.PhoneVerified = false;
        if (emailChanged || phoneChanged)
        {
            account.SecurityStamp = RandomNumberGenerator.GetBytes(32);
            await database.AuthSessions.Where(s => s.AccountId == id && s.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now).SetProperty(x => x.RevokeReasonCode, "admin_contact_changed"), ct);
            await database.AccountChallenges.Where(c => c.AccountId == id && c.ConsumedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, now), ct);
        }
        if (account.PreferredRecoveryChannel == "email" && validated.Email is null) account.PreferredRecoveryChannel = validated.Phone is null ? null : "sms";
        if (account.PreferredRecoveryChannel == "sms" && validated.Phone is null) account.PreferredRecoveryChannel = validated.Email is null ? null : "email";
        account.UpdatedAtUtc = now; account.RowVersion++;
        ApplyDetails(account, request.Data, validated.Specialties, now);
        await SaveAsync(ct);
        await audit.WriteAsync("administrator.user.registration_updated", actor, "account", id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["role"] = account.RoleCode, ["emailChanged"] = emailChanged.ToString(), ["phoneChanged"] = phoneChanged.ToString() }, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Map(account);
    }

    private void ApplyDetails(Account account, AdministratorAccountData data, uint[] specialties, DateTime now)
    {
        if (account.RoleCode == ViverAppRoles.Patient)
        {
            account.PatientProfile ??= new PatientProfile { Account = account, CreatedAtUtc = now };
            var profile = account.PatientProfile;
            profile.TaxId = account.TaxId; profile.BirthDate = account.BirthDate; profile.PreferredName = Text(data.Patient?.PreferredName);
            profile.AdministrativeNotes = Text(data.Patient?.AdministrativeNotes); profile.UpdatedAtUtc = now;
            if (data.Patient?.Address is not { } address)
            { if (account.AccountAddress is not null) database.AccountAddresses.Remove(account.AccountAddress); account.AccountAddress = null; }
            else
            {
                account.AccountAddress ??= new AccountAddress { Account = account };
                var a = account.AccountAddress;
                a.PostalCode = Text(address.PostalCode); a.Street = Text(address.Street); a.Number = Text(address.Number); a.Complement = Text(address.Complement);
                a.District = Text(address.District); a.City = Text(address.City); a.StateCode = Text(address.StateCode); a.UpdatedAtUtc = now;
            }
        }
        if (data.Professional is { } professional)
        {
            var isNew = account.ProfessionalProfile is null;
            account.ProfessionalProfile ??= new ProfessionalProfile { Account = account, CreatedAtUtc = now, RowVersion = 0 };
            var p = account.ProfessionalProfile;
            p.LicenseTypeCode = account.RoleCode == ViverAppRoles.Psychologist ? "CRP" : "CRM";
            p.ProfessionalTitle = professional.Registration.ProfessionalTitle; p.LicenseStateCode = professional.Registration.LicenseStateCode;
            p.LicenseNumber = professional.Registration.LicenseNumber.Trim(); p.YearsExperience = professional.Registration.YearsExperience;
            p.Biography = Text(professional.Biography); p.DefaultAppointmentDurationMinutes = professional.DefaultAppointmentDurationMinutes;
            p.UpdatedAtUtc = now; p.RowVersion++;
            foreach (var old in p.ProfessionalSpecialties.ToArray())
            {
                if (!specialties.Contains(old.SpecialtyId)) { database.ProfessionalSpecialties.Remove(old); p.ProfessionalSpecialties.Remove(old); }
                else old.IsPrimary = old.SpecialtyId == professional.Registration.PrimarySpecialtyId;
            }
            foreach (var specialty in specialties.Where(s => !p.ProfessionalSpecialties.Any(x => x.SpecialtyId == s)))
                p.ProfessionalSpecialties.Add(new ProfessionalSpecialty { ProfessionalAccount = p, SpecialtyId = specialty, IsPrimary = specialty == professional.Registration.PrimarySpecialtyId });
            if (isNew) database.ProfessionalProfiles.Add(p);
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await database.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Conflict("Este cadastro foi alterado por outra sessão. Recarregue antes de salvar."); }
        catch (DbUpdateException ex) when (ex.InnerException is MySql.Data.MySqlClient.MySqlException { Number: 1062 })
        { throw Conflict("CPF, e-mail, telefone ou registro profissional já está em uso. Confira os dados."); }
    }

    private static AdministratorAccountResponse Map(Account a)
    {
        var address = a.AccountAddress;
        var p = a.ProfessionalProfile;
        var primary = p?.ProfessionalSpecialties.FirstOrDefault(s => s.IsPrimary)?.SpecialtyId ?? 0;
        return new(a.Id, a.RoleCode, a.StatusCode, a.PortalAccessEnabled, a.PasswordSetupRequired, a.EmailVerified, a.PhoneVerified, a.RowVersion,
            new(a.FullName, a.Email, a.PhoneE164, a.TaxId ?? a.PatientProfile?.TaxId,
                (a.BirthDate ?? a.PatientProfile?.BirthDate) is { } birth ? DateOnly.FromDateTime(birth) : null,
                a.RoleCode == ViverAppRoles.Patient ? new(a.PatientProfile?.PreferredName, a.PatientProfile?.AdministrativeNotes,
                    address is null ? null : new(address.PostalCode, address.Street, address.Number, address.Complement, address.District, address.City, address.StateCode)) : null,
                p is null ? null : new(new(p.ProfessionalTitle, p.LicenseStateCode, p.LicenseNumber, p.YearsExperience, primary), p.Biography, p.DefaultAppointmentDurationMinutes, p.ProfessionalSpecialties.Select(s => s.SpecialtyId).ToArray(), p.RowVersion)));
    }
}
