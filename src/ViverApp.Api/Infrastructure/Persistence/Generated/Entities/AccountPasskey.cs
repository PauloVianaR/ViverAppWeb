using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AccountPasskey
{
    public byte[] CredentialId { get; set; } = null!;

    public ulong AccountId { get; set; }

    public byte[] PublicKey { get; set; } = null!;

    public string? DisplayName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public uint SignCount { get; set; }

    public string TransportsJson { get; set; } = null!;

    public bool IsUserVerified { get; set; }

    public bool IsBackupEligible { get; set; }

    public bool IsBackedUp { get; set; }

    public byte[] AttestationObject { get; set; } = null!;

    public byte[] ClientDataJson { get; set; } = null!;

    public virtual Account Account { get; set; } = null!;
}
