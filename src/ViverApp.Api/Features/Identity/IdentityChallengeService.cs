using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Identity;

public sealed class IdentityChallengeService(
    ViverAppDbContext database,
    IDataProtectionProvider dataProtectionProvider,
    IdentitySecurityOptions securityOptions)
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector outboxProtector = dataProtectionProvider.CreateProtector(
        "ViverApp.Identity.OutboxMessage.v1");

    public async Task<Guid> CreateAsync(
        ViverAppUser user,
        string purpose,
        string channel,
        string destination,
        CancellationToken cancellationToken)
    {
        if (purpose is not ("login" or "password_reset" or "contact_verification" or "contact_change"))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        if (channel is not ("email" or "sms"))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        var now = DateTime.UtcNow;
        var challengeId = Guid.NewGuid();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = new AccountChallenge
        {
            Id = challengeId.ToByteArray(),
            AccountId = user.Id,
            PurposeCode = purpose,
            ChannelCode = channel,
            SecretHash = HashCode(challengeId, code),
            DestinationHash = HashDestination(destination),
            AttemptCount = 0,
            MaxAttempts = 5,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(ChallengeLifetime),
        };

        var protectedMessage = outboxProtector.Protect(JsonSerializer.SerializeToUtf8Bytes(new
        {
            code,
            expiresAtUtc = challenge.ExpiresAtUtc,
        }));
        var outbox = new OutboxMessage
        {
            ChannelCode = channel,
            TemplateKey = $"identity.{purpose}",
            TemplateVersion = 1,
            Recipient = destination,
            AccountId = user.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                protectedPayload = Convert.ToBase64String(protectedMessage),
                protectionPurpose = "ViverApp.Identity.OutboxMessage.v1",
            }),
            StatusCode = "pending",
            IdempotencyKey = challengeId,
            AttemptCount = 0,
            MaxAttempts = 5,
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
        };

        database.AccountChallenges.Add(challenge);
        database.OutboxMessages.Add(outbox);
        await database.SaveChangesAsync(cancellationToken);
        return challengeId;
    }

    public async Task<ChallengeVerificationResult> VerifyAsync(
        Guid challengeId,
        string purpose,
        string code,
        CancellationToken cancellationToken)
    {
        var id = challengeId.ToByteArray();
        var now = DateTime.UtcNow;
        var challenge = await database.AccountChallenges
            .AsNoTracking()
            .SingleOrDefaultAsync(
            item => item.Id.SequenceEqual(id) && item.PurposeCode == purpose,
            cancellationToken);
        if (challenge is null
            || challenge.ConsumedAtUtc is not null
            || challenge.ExpiresAtUtc <= now
            || challenge.AttemptCount >= challenge.MaxAttempts)
        {
            return ChallengeVerificationResult.Failed;
        }

        var candidate = HashCode(challengeId, IdentifierNormalizer.NormalizeCode(code));
        if (!CryptographicOperations.FixedTimeEquals(candidate, challenge.SecretHash))
        {
            await database.AccountChallenges
                .Where(item => item.Id.SequenceEqual(id)
                    && item.PurposeCode == purpose
                    && item.ConsumedAtUtc == null
                    && item.ExpiresAtUtc > now
                    && item.AttemptCount < item.MaxAttempts)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        item => item.AttemptCount,
                        item => (ushort)(item.AttemptCount + 1)),
                    cancellationToken);
            return ChallengeVerificationResult.Failed;
        }

        var consumed = await database.AccountChallenges
            .Where(item => item.Id.SequenceEqual(id)
                && item.PurposeCode == purpose
                && item.ConsumedAtUtc == null
                && item.ExpiresAtUtc > now
                && item.AttemptCount < item.MaxAttempts)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.ConsumedAtUtc, now),
                cancellationToken);
        if (consumed != 1)
        {
            return ChallengeVerificationResult.Failed;
        }

        return new ChallengeVerificationResult(true, challenge.AccountId, challenge.ChannelCode);
    }

    public static Guid CreateOpaqueRequestId()
    {
        return Guid.NewGuid();
    }

    private byte[] HashCode(Guid challengeId, string code)
    {
        var id = challengeId.ToByteArray();
        var normalizedCode = Encoding.UTF8.GetBytes(IdentifierNormalizer.NormalizeCode(code));
        var input = new byte[id.Length + normalizedCode.Length];
        id.CopyTo(input, 0);
        normalizedCode.CopyTo(input, id.Length);
        return HMACSHA256.HashData(securityOptions.ChallengePepper, input);
    }

    private byte[] HashDestination(string destination)
    {
        return HMACSHA256.HashData(
            securityOptions.ChallengePepper,
            Encoding.UTF8.GetBytes(destination));
    }
}

public sealed record ChallengeVerificationResult(
    bool Succeeded,
    ulong? AccountId,
    string? Channel)
{
    public static ChallengeVerificationResult Failed { get; } = new(false, null, null);
}
