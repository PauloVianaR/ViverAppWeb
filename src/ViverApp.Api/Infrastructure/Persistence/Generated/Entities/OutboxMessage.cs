using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class OutboxMessage
{
    public ulong Id { get; set; }

    public string ChannelCode { get; set; } = null!;

    public string TemplateKey { get; set; } = null!;

    public ushort TemplateVersion { get; set; }

    public string Recipient { get; set; } = null!;

    public ulong? AccountId { get; set; }

    public string PayloadJson { get; set; } = null!;

    public string StatusCode { get; set; } = null!;

    public Guid IdempotencyKey { get; set; }

    public ushort AttemptCount { get; set; }

    public ushort MaxAttempts { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTime? LeaseUntilUtc { get; set; }

    public string? LastErrorCode { get; set; }

    public string? ProviderReference { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public virtual Account? Account { get; set; }
}
