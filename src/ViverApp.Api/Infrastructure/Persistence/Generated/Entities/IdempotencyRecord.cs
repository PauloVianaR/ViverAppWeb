using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class IdempotencyRecord
{
    public string ScopeCode { get; set; } = null!;

    public string IdempotencyKey { get; set; } = null!;

    public byte[] RequestHash { get; set; } = null!;

    public ushort? ResponseStatusCode { get; set; }

    public string? ResponseBodyJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
}
