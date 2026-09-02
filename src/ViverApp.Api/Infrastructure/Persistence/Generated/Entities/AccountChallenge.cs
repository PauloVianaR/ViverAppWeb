using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AccountChallenge
{
    public byte[] Id { get; set; } = null!;

    public ulong AccountId { get; set; }

    public string PurposeCode { get; set; } = null!;

    public string ChannelCode { get; set; } = null!;

    public byte[] SecretHash { get; set; } = null!;

    public byte[] DestinationHash { get; set; } = null!;

    public ushort AttemptCount { get; set; }

    public ushort MaxAttempts { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? ConsumedAtUtc { get; set; }

    public virtual Account Account { get; set; } = null!;
}
