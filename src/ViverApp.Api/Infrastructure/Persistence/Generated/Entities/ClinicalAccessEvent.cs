using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ClinicalAccessEvent
{
    public ulong Id { get; set; }

    public ulong PatientAccountId { get; set; }

    public ulong ActorAccountId { get; set; }

    public string ActorRoleCode { get; set; } = null!;

    public string ScopeCode { get; set; } = null!;

    public string OutcomeCode { get; set; } = null!;

    public string? Purpose { get; set; }

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public virtual Account ActorAccount { get; set; } = null!;

    public virtual Account PatientAccount { get; set; } = null!;
}
