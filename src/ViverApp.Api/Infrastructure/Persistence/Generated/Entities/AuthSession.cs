using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AuthSession
{
    public byte[] Id { get; set; } = null!;

    public ulong AccountId { get; set; }

    public byte[] RefreshTokenHash { get; set; } = null!;

    public string AuthenticationMethod { get; set; } = null!;

    public bool MfaSatisfied { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? LastSeenAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? RevokeReasonCode { get; set; }

    public byte[]? IpAddressHash { get; set; }

    public byte[]? UserAgentHash { get; set; }

    public virtual Account Account { get; set; } = null!;
}
