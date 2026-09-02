using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ApplicationSetting
{
    public string SettingKey { get; set; } = null!;

    public string ValueJson { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsSecret { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong? UpdatedByAccountId { get; set; }

    public virtual Account? UpdatedByAccount { get; set; }
}
