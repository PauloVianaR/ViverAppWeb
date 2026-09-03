using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ProfessionalReview
{
    public ulong Id { get; set; }

    public ulong ProfessionalAccountId { get; set; }

    public ulong ReviewerAccountId { get; set; }

    public string DecisionCode { get; set; } = null!;

    public string? Reason { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public virtual Account ProfessionalAccount { get; set; } = null!;

    public virtual Account ReviewerAccount { get; set; } = null!;
}
