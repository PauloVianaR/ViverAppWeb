using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class StaffMessage
{
    public ulong Id { get; set; }

    public ulong SenderAccountId { get; set; }

    public ulong RecipientAccountId { get; set; }

    public Guid ClientNonce { get; set; }

    public string Body { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    public virtual Account RecipientAccount { get; set; } = null!;

    public virtual Account SenderAccount { get; set; } = null!;
}
