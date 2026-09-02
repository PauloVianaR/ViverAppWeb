using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class Role
{
    public string Code { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public bool IsPrivileged { get; set; }

    public virtual ICollection<Account> Accounts { get; set; } = new List<Account>();
}
