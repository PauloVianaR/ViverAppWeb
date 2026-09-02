using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ExternalLogin
{
    public string ProviderCode { get; set; } = null!;

    public string ProviderSubject { get; set; } = null!;

    public ulong AccountId { get; set; }

    public string? ProviderEmail { get; set; }

    public bool ProviderEmailVerified { get; set; }

    public DateTime LinkedAtUtc { get; set; }

    public DateTime? LastUsedAtUtc { get; set; }

    public virtual Account Account { get; set; } = null!;
}
