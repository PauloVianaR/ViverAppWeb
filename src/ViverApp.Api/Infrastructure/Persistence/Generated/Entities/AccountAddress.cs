using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AccountAddress
{
    public ulong AccountId { get; set; }

    public string? PostalCode { get; set; }

    public string? Street { get; set; }

    public string? Number { get; set; }

    public string? Complement { get; set; }

    public string? District { get; set; }

    public string? City { get; set; }

    public string? StateCode { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public virtual Account Account { get; set; } = null!;
}
