using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ContactChangeRequest
{
    public Guid Id { get; set; }

    public ulong AccountId { get; set; }

    public byte[] ChallengeId { get; set; } = null!;

    public string ChannelCode { get; set; } = null!;

    public string Destination { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public virtual Account Account { get; set; } = null!;
}
