using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AdministratorNotification
{
    public ulong Id { get; set; }

    public ulong AdministratorAccountId { get; set; }

    public string SourceKey { get; set; } = null!;

    public string TypeCode { get; set; } = null!;

    public string SeverityCode { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    public DateTime? DismissedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Account AdministratorAccount { get; set; } = null!;
}
