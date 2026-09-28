using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AdministratorAnalyticsExport
{
    public ulong Id { get; set; }

    public ulong RequestedByAccountId { get; set; }

    public DateTime PeriodFrom { get; set; }

    public DateTime PeriodTo { get; set; }

    public string StatusCode { get; set; } = null!;

    public ushort AttemptCount { get; set; }

    public DateTime? LeaseUntilUtc { get; set; }

    public byte[]? ProtectedContent { get; set; }

    public byte[]? ContentSha256 { get; set; }

    public uint? ContentSizeBytes { get; set; }

    public string? ErrorCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public virtual Account RequestedByAccount { get; set; } = null!;
}
