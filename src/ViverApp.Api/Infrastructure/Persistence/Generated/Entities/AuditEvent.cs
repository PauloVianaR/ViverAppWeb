using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AuditEvent
{
    public ulong Id { get; set; }

    public ulong? ActorAccountId { get; set; }

    public string EventCode { get; set; } = null!;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public Guid CorrelationId { get; set; }

    public byte[]? IpAddressHash { get; set; }

    public string? DataJson { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public virtual Account? ActorAccount { get; set; }
}
