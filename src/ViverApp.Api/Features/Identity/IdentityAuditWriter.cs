using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.Identity;

public sealed class IdentityAuditWriter(
    ViverAppDbContext database,
    IHttpContextAccessor httpContextAccessor,
    IdentitySecurityOptions securityOptions)
{
    public async Task WriteAsync(
        string eventCode,
        ulong? actorAccountId,
        IReadOnlyDictionary<string, string>? safeData,
        CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        var correlation = context is null
            ? Guid.NewGuid()
            : ToGuid(CorrelationIdMiddleware.GetCorrelationId(context));
        var addressBytes = context?.Connection.RemoteIpAddress?.GetAddressBytes();
        database.AuditEvents.Add(new AuditEvent
        {
            ActorAccountId = actorAccountId,
            EventCode = eventCode,
            EntityType = actorAccountId is null ? null : "account",
            EntityId = actorAccountId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CorrelationId = correlation,
            IpAddressHash = addressBytes is null
                ? null
                : HMACSHA256.HashData(securityOptions.ChallengePepper, addressBytes),
            DataJson = safeData is null ? null : JsonSerializer.Serialize(safeData),
            OccurredAtUtc = DateTime.UtcNow,
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    private static Guid ToGuid(string value)
    {
        if (Guid.TryParse(value, out var correlation))
        {
            return correlation;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
