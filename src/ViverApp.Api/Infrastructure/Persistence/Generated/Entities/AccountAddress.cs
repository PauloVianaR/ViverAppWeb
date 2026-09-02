using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AccountAddress
{
    public ulong AccountId { get; set; }

    public string PostalCode { get; set; } = null!;

    public string Street { get; set; } = null!;

    public string Number { get; set; } = null!;

    public string? Complement { get; set; }

    public string District { get; set; } = null!;

    public string City { get; set; } = null!;

    public string StateCode { get; set; } = null!;

    public DateTime UpdatedAtUtc { get; set; }

    public virtual Account Account { get; set; } = null!;
}
