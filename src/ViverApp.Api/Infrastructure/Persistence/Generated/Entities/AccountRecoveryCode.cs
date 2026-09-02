using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AccountRecoveryCode
{
    public byte[] Id { get; set; } = null!;

    public ulong AccountId { get; set; }

    public byte[] CodeHash { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UsedAtUtc { get; set; }

    public virtual Account Account { get; set; } = null!;
}
