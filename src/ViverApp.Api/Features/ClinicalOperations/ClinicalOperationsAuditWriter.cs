using ViverApp.Api.Features.Identity;

namespace ViverApp.Api.Features.ClinicalOperations;

public interface IClinicalOperationsAuditWriter
{
    Task WriteAsync(
        string eventCode,
        ulong actorAccountId,
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, string>? safeData,
        CancellationToken cancellationToken);
}

internal sealed class ClinicalOperationsAuditWriter(IdentityAuditWriter writer) : IClinicalOperationsAuditWriter
{
    public Task WriteAsync(
        string eventCode,
        ulong actorAccountId,
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, string>? safeData,
        CancellationToken cancellationToken) =>
        writer.WriteAsync(eventCode, actorAccountId, entityType, entityId, safeData, cancellationToken);
}
