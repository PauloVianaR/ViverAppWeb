using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class Clinic
{
    public byte SingletonId { get; set; }

    public string LegalName { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string? TaxId { get; set; }

    public string? Email { get; set; }

    public string? PhoneE164 { get; set; }

    public string TimezoneName { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }
}
