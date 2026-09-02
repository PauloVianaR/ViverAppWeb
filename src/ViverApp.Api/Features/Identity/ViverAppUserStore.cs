using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Identity;

public sealed class ViverAppUserStore(
    ViverAppDbContext database,
    IDataProtectionProvider dataProtectionProvider,
    IdentitySecurityOptions securityOptions) :
    IUserStore<ViverAppUser>,
    IUserPasswordStore<ViverAppUser>,
    IUserEmailStore<ViverAppUser>,
    IUserPhoneNumberStore<ViverAppUser>,
    IUserSecurityStampStore<ViverAppUser>,
    IUserLockoutStore<ViverAppUser>,
    IUserRoleStore<ViverAppUser>,
    IUserLoginStore<ViverAppUser>,
    IUserTwoFactorStore<ViverAppUser>,
    IUserAuthenticatorKeyStore<ViverAppUser>,
    IUserTwoFactorRecoveryCodeStore<ViverAppUser>,
    IUserPasskeyStore<ViverAppUser>
{
    private const int MaximumPasskeysPerAccount = 10;
    private readonly IDataProtector authenticatorProtector = dataProtectionProvider.CreateProtector(
        "ViverApp.Identity.AuthenticatorKey.v1");

    public async Task<IdentityResult> CreateAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ViverAppRoles.All.Contains(user.RoleCode))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidRole",
                Description = "O papel informado é inválido.",
            });
        }

        var now = DateTime.UtcNow;
        var securityStamp = RandomNumberGenerator.GetBytes(32);
        var account = new Account
        {
            RoleCode = user.RoleCode,
            StatusCode = user.StatusCode,
            FullName = user.FullName,
            Email = user.Email,
            NormalizedEmail = user.NormalizedEmail,
            PhoneE164 = user.PhoneNumber,
            PasswordHash = user.PasswordHash,
            EmailVerified = user.EmailConfirmed,
            PhoneVerified = user.PhoneNumberConfirmed,
            SecurityStamp = securityStamp,
            FailedLoginCount = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };

        database.Accounts.Add(account);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateAccount",
                Description = "Não foi possível criar a conta.",
            });
        }

        user.Id = account.Id;
        user.SecurityStamp = Convert.ToHexString(securityStamp);
        user.RowVersion = account.RowVersion;
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        var account = await database.Accounts.SingleOrDefaultAsync(
            item => item.Id == user.Id,
            cancellationToken);
        if (account is null)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "AccountNotFound",
                Description = "A conta não foi encontrada.",
            });
        }

        database.Entry(account).Property(item => item.RowVersion).OriginalValue = user.RowVersion;
        account.RoleCode = user.RoleCode;
        account.StatusCode = user.StatusCode;
        account.FullName = user.FullName;
        account.Email = user.Email;
        account.NormalizedEmail = user.NormalizedEmail;
        account.PhoneE164 = user.PhoneNumber;
        account.PasswordHash = user.PasswordHash;
        account.EmailVerified = user.EmailConfirmed;
        account.PhoneVerified = user.PhoneNumberConfirmed;
        account.SecurityStamp = SecurityStampToBytes(user.SecurityStamp);
        account.FailedLoginCount = checked((ushort)Math.Clamp(user.AccessFailedCount, 0, ushort.MaxValue));
        account.LockoutEndUtc = user.LockoutEnd?.UtcDateTime;
        account.UpdatedAtUtc = DateTime.UtcNow;
        account.RowVersion++;

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        }
        catch (DbUpdateException)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "AccountUpdateFailed",
                Description = "Não foi possível atualizar a conta.",
            });
        }

        user.SecurityStamp = Convert.ToHexString(account.SecurityStamp);
        user.RowVersion = account.RowVersion;
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var account = await database.Accounts.SingleOrDefaultAsync(
            item => item.Id == user.Id,
            cancellationToken);
        if (account is null)
        {
            return IdentityResult.Success;
        }

        database.Accounts.Remove(account);
        await database.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public Task<string> GetUserIdAsync(ViverAppUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.Id.ToString(CultureInfo.InvariantCulture));
    }

    public Task<string?> GetUserNameAsync(ViverAppUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.UserName);
    }

    public Task SetUserNameAsync(
        ViverAppUser user,
        string? userName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.NormalizedUserName);
    }

    public Task SetNormalizedUserNameAsync(
        ViverAppUser user,
        string? normalizedName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public async Task<ViverAppUser?> FindByIdAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        return ulong.TryParse(userId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? await FindAccountAsync(item => item.Id == id, cancellationToken)
            : null;
    }

    public Task<ViverAppUser?> FindByNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUserName);
        return FindAccountAsync(
            item => item.NormalizedEmail == normalizedUserName
                || item.PhoneE164 == normalizedUserName,
            cancellationToken);
    }

    public Task SetPasswordHashAsync(
        ViverAppUser user,
        string? passwordHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.PasswordHash);
    }

    public Task<bool> HasPasswordAsync(ViverAppUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(!string.IsNullOrWhiteSpace(user.PasswordHash));
    }

    public Task SetEmailAsync(ViverAppUser user, string? email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(ViverAppUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.Email);
    }

    public Task<bool> GetEmailConfirmedAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.EmailConfirmed);
    }

    public Task SetEmailConfirmedAsync(
        ViverAppUser user,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task<ViverAppUser?> FindByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        return FindAccountAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    public Task<string?> GetNormalizedEmailAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.NormalizedEmail);
    }

    public Task SetNormalizedEmailAsync(
        ViverAppUser user,
        string? normalizedEmail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    public Task SetPhoneNumberAsync(
        ViverAppUser user,
        string? phoneNumber,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    public Task<string?> GetPhoneNumberAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.PhoneNumber);
    }

    public Task<bool> GetPhoneNumberConfirmedAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.PhoneNumberConfirmed);
    }

    public Task SetPhoneNumberConfirmedAsync(
        ViverAppUser user,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task SetSecurityStampAsync(
        ViverAppUser user,
        string stamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(stamp);
        user.SecurityStamp = Convert.ToHexString(SecurityStampToBytes(stamp));
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult<string?>(user.SecurityStamp);
    }

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.LockoutEnd);
    }

    public Task SetLockoutEndDateAsync(
        ViverAppUser user,
        DateTimeOffset? lockoutEnd,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.AccessFailedCount = Math.Min(user.AccessFailedCount + 1, ushort.MaxValue);
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task<bool> GetLockoutEnabledAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.LockoutEnabled);
    }

    public Task SetLockoutEnabledAsync(
        ViverAppUser user,
        bool enabled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task AddToRoleAsync(
        ViverAppUser user,
        string roleName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var normalizedRole = roleName.ToLowerInvariant();
        if (!ViverAppRoles.All.Contains(normalizedRole))
        {
            throw new InvalidOperationException("O papel informado é inválido.");
        }

        user.RoleCode = normalizedRole;
        return Task.CompletedTask;
    }

    public Task RemoveFromRoleAsync(
        ViverAppUser user,
        string roleName,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Uma conta deve possuir exatamente um papel.");
    }

    public Task<IList<string>> GetRolesAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult<IList<string>>([user.RoleCode]);
    }

    public Task<bool> IsInRoleAsync(
        ViverAppUser user,
        string roleName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(string.Equals(
            user.RoleCode,
            roleName,
            StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IList<ViverAppUser>> GetUsersInRoleAsync(
        string roleName,
        CancellationToken cancellationToken)
    {
        var normalizedRole = roleName.ToLowerInvariant();
        var accounts = await database.Accounts
            .AsNoTracking()
            .Where(item => item.RoleCode == normalizedRole)
            .ToListAsync(cancellationToken);
        return accounts.Select(MapUser).ToList();
    }

    public async Task AddLoginAsync(
        ViverAppUser user,
        UserLoginInfo login,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(login);
        database.ExternalLogins.Add(new ExternalLogin
        {
            ProviderCode = NormalizeProvider(login.LoginProvider),
            ProviderSubject = login.ProviderKey,
            AccountId = user.Id,
            LinkedAtUtc = DateTime.UtcNow,
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveLoginAsync(
        ViverAppUser user,
        string loginProvider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var provider = NormalizeProvider(loginProvider);
        var login = await database.ExternalLogins.SingleOrDefaultAsync(
            item => item.AccountId == user.Id
                && item.ProviderCode == provider
                && item.ProviderSubject == providerKey,
            cancellationToken);
        if (login is not null)
        {
            database.ExternalLogins.Remove(login);
            await database.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IList<UserLoginInfo>> GetLoginsAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        var logins = await database.ExternalLogins
            .AsNoTracking()
            .Where(item => item.AccountId == user.Id)
            .ToListAsync(cancellationToken);
        return logins.Select(item => new UserLoginInfo(
                item.ProviderCode,
                item.ProviderSubject,
                item.ProviderCode))
            .ToList();
    }

    public Task<ViverAppUser?> FindByLoginAsync(
        string loginProvider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var provider = NormalizeProvider(loginProvider);
        return FindAccountAsync(
            item => item.ExternalLogins.Any(login =>
                login.ProviderCode == provider && login.ProviderSubject == providerKey),
            cancellationToken);
    }

    public async Task SetTwoFactorEnabledAsync(
        ViverAppUser user,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var authenticator = await GetOrCreateAuthenticatorAsync(user.Id, cancellationToken);
        if (enabled && authenticator.ProtectedKey is null)
        {
            throw new InvalidOperationException("O autenticador ainda não possui uma chave.");
        }

        authenticator.IsEnabled = enabled;
        authenticator.EnabledAtUtc = enabled ? DateTime.UtcNow : null;
        authenticator.UpdatedAtUtc = DateTime.UtcNow;
        user.TwoFactorEnabled = enabled;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> GetTwoFactorEnabledAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        user.TwoFactorEnabled = await database.AccountAuthenticators
            .AsNoTracking()
            .AnyAsync(
                item => item.AccountId == user.Id && item.IsEnabled,
                cancellationToken);
        return user.TwoFactorEnabled;
    }

    public async Task SetAuthenticatorKeyAsync(
        ViverAppUser user,
        string? key,
        CancellationToken cancellationToken)
    {
        var authenticator = await GetOrCreateAuthenticatorAsync(user.Id, cancellationToken);
        authenticator.ProtectedKey = key is null
            ? null
            : authenticatorProtector.Protect(Encoding.UTF8.GetBytes(key));
        authenticator.IsEnabled = false;
        authenticator.EnabledAtUtc = null;
        authenticator.UpdatedAtUtc = DateTime.UtcNow;
        user.TwoFactorEnabled = false;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> GetAuthenticatorKeyAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        var protectedKey = await database.AccountAuthenticators
            .AsNoTracking()
            .Where(item => item.AccountId == user.Id)
            .Select(item => item.ProtectedKey)
            .SingleOrDefaultAsync(cancellationToken);
        return protectedKey is null
            ? null
            : Encoding.UTF8.GetString(authenticatorProtector.Unprotect(protectedKey));
    }

    public async Task ReplaceCodesAsync(
        ViverAppUser user,
        IEnumerable<string> recoveryCodes,
        CancellationToken cancellationToken)
    {
        var existing = await database.AccountRecoveryCodes
            .Where(item => item.AccountId == user.Id && item.UsedAtUtc == null)
            .ToListAsync(cancellationToken);
        database.AccountRecoveryCodes.RemoveRange(existing);
        var now = DateTime.UtcNow;
        foreach (var recoveryCode in recoveryCodes)
        {
            database.AccountRecoveryCodes.Add(new AccountRecoveryCode
            {
                Id = Guid.NewGuid().ToByteArray(),
                AccountId = user.Id,
                CodeHash = HashRecoveryCode(recoveryCode),
                CreatedAtUtc = now,
            });
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RedeemCodeAsync(
        ViverAppUser user,
        string code,
        CancellationToken cancellationToken)
    {
        var hash = HashRecoveryCode(code);
        var candidates = await database.AccountRecoveryCodes
            .AsNoTracking()
            .Where(item => item.AccountId == user.Id && item.UsedAtUtc == null)
            .ToListAsync(cancellationToken);
        var match = candidates.FirstOrDefault(item =>
            CryptographicOperations.FixedTimeEquals(item.CodeHash, hash));
        if (match is null)
        {
            return false;
        }

        var used = await database.AccountRecoveryCodes
            .Where(item => item.Id.SequenceEqual(match.Id) && item.UsedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.UsedAtUtc, DateTime.UtcNow),
                cancellationToken);
        return used == 1;
    }

    public Task<int> CountCodesAsync(ViverAppUser user, CancellationToken cancellationToken)
    {
        return database.AccountRecoveryCodes.CountAsync(
            item => item.AccountId == user.Id && item.UsedAtUtc == null,
            cancellationToken);
    }

    public async Task AddOrUpdatePasskeyAsync(
        ViverAppUser user,
        UserPasskeyInfo passkey,
        CancellationToken cancellationToken)
    {
        var entity = await database.AccountPasskeys.SingleOrDefaultAsync(
            item => item.CredentialId.SequenceEqual(passkey.CredentialId),
            cancellationToken);
        if (entity is null)
        {
            var count = await database.AccountPasskeys.CountAsync(
                item => item.AccountId == user.Id,
                cancellationToken);
            if (count >= MaximumPasskeysPerAccount)
            {
                throw new InvalidOperationException("Limite de passkeys atingido.");
            }

            entity = new AccountPasskey
            {
                CredentialId = passkey.CredentialId,
                AccountId = user.Id,
                CreatedAtUtc = passkey.CreatedAt.UtcDateTime,
            };
            database.AccountPasskeys.Add(entity);
        }
        else if (entity.AccountId != user.Id)
        {
            throw new InvalidOperationException("A passkey pertence a outra conta.");
        }

        entity.PublicKey = passkey.PublicKey;
        entity.DisplayName = passkey.Name;
        entity.SignCount = passkey.SignCount;
        entity.TransportsJson = JsonSerializer.Serialize(passkey.Transports);
        entity.IsUserVerified = passkey.IsUserVerified;
        entity.IsBackupEligible = passkey.IsBackupEligible;
        entity.IsBackedUp = passkey.IsBackedUp;
        entity.AttestationObject = passkey.AttestationObject;
        entity.ClientDataJson = passkey.ClientDataJson;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IList<UserPasskeyInfo>> GetPasskeysAsync(
        ViverAppUser user,
        CancellationToken cancellationToken)
    {
        var passkeys = await database.AccountPasskeys
            .AsNoTracking()
            .Where(item => item.AccountId == user.Id)
            .OrderBy(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return passkeys.Select(MapPasskey).ToList();
    }

    public async Task<ViverAppUser?> FindByPasskeyIdAsync(
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        var accountId = await database.AccountPasskeys
            .AsNoTracking()
            .Where(item => item.CredentialId.SequenceEqual(credentialId))
            .Select(item => (ulong?)item.AccountId)
            .SingleOrDefaultAsync(cancellationToken);
        return accountId is null
            ? null
            : await FindAccountAsync(item => item.Id == accountId.Value, cancellationToken);
    }

    public async Task<UserPasskeyInfo?> FindPasskeyAsync(
        ViverAppUser user,
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        var passkey = await database.AccountPasskeys
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.AccountId == user.Id
                    && item.CredentialId.SequenceEqual(credentialId),
                cancellationToken);
        return passkey is null ? null : MapPasskey(passkey);
    }

    public async Task RemovePasskeyAsync(
        ViverAppUser user,
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        var passkey = await database.AccountPasskeys.SingleOrDefaultAsync(
            item => item.AccountId == user.Id
                && item.CredentialId.SequenceEqual(credentialId),
            cancellationToken);
        if (passkey is not null)
        {
            database.AccountPasskeys.Remove(passkey);
            await database.SaveChangesAsync(cancellationToken);
        }
    }

    public void Dispose()
    {
    }

    private async Task<ViverAppUser?> FindAccountAsync(
        System.Linq.Expressions.Expression<Func<Account, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var account = await database.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(predicate, cancellationToken);
        return account is null ? null : MapUser(account);
    }

    private async Task<AccountAuthenticator> GetOrCreateAuthenticatorAsync(
        ulong accountId,
        CancellationToken cancellationToken)
    {
        var authenticator = await database.AccountAuthenticators.SingleOrDefaultAsync(
            item => item.AccountId == accountId,
            cancellationToken);
        if (authenticator is not null)
        {
            return authenticator;
        }

        var now = DateTime.UtcNow;
        authenticator = new AccountAuthenticator
        {
            AccountId = accountId,
            IsEnabled = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        database.AccountAuthenticators.Add(authenticator);
        return authenticator;
    }

    private byte[] HashRecoveryCode(string code)
    {
        return HMACSHA256.HashData(
            securityOptions.ChallengePepper,
            Encoding.UTF8.GetBytes(IdentifierNormalizer.NormalizeCode(code)));
    }

    private static ViverAppUser MapUser(Account account)
    {
        var userName = account.Email ?? account.PhoneE164;
        return new ViverAppUser
        {
            Id = account.Id,
            UserName = userName,
            NormalizedUserName = account.NormalizedEmail ?? account.PhoneE164,
            RoleCode = account.RoleCode,
            StatusCode = account.StatusCode,
            FullName = account.FullName,
            Email = account.Email,
            NormalizedEmail = account.NormalizedEmail,
            EmailConfirmed = account.EmailVerified,
            PhoneNumber = account.PhoneE164,
            PhoneNumberConfirmed = account.PhoneVerified,
            PasswordHash = account.PasswordHash,
            SecurityStamp = Convert.ToHexString(account.SecurityStamp),
            AccessFailedCount = account.FailedLoginCount,
            LockoutEnd = account.LockoutEndUtc is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(
                    account.LockoutEndUtc.Value,
                    DateTimeKind.Utc)),
            RowVersion = account.RowVersion,
        };
    }

    private static UserPasskeyInfo MapPasskey(AccountPasskey passkey)
    {
        var transports = JsonSerializer.Deserialize<string[]>(passkey.TransportsJson) ?? [];
        return new UserPasskeyInfo(
            passkey.CredentialId,
            passkey.PublicKey,
            new DateTimeOffset(DateTime.SpecifyKind(passkey.CreatedAtUtc, DateTimeKind.Utc)),
            passkey.SignCount,
            transports,
            passkey.IsUserVerified,
            passkey.IsBackupEligible,
            passkey.IsBackedUp,
            passkey.AttestationObject,
            passkey.ClientDataJson)
        {
            Name = passkey.DisplayName,
        };
    }

    private static byte[] SecurityStampToBytes(string stamp)
    {
        if (stamp.Length == 64)
        {
            try
            {
                return Convert.FromHexString(stamp);
            }
            catch (FormatException)
            {
            }
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(stamp));
    }

    private static string NormalizeProvider(string provider)
    {
        return provider.Trim().ToLowerInvariant();
    }
}
