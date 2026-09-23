using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class NotificationSuppression
{
    public ulong Id { get; set; }

    public string ChannelCode { get; set; } = null!;

    public byte[] RecipientHash { get; set; } = null!;

    public string ReasonCode { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }
}
