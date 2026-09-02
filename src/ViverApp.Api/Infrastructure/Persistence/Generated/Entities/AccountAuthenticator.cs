using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AccountAuthenticator
{
    public ulong AccountId { get; set; }

    public byte[]? ProtectedKey { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? EnabledAtUtc { get; set; }

    public virtual Account Account { get; set; } = null!;
}
