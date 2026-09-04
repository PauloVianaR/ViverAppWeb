using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AccountConsent
{
    public ulong AccountId { get; set; }

    public string TermsVersion { get; set; } = null!;

    public string PrivacyVersion { get; set; } = null!;

    public DateTime AcceptedAtUtc { get; set; }

    public string SourceCode { get; set; } = null!;

    public virtual Account Account { get; set; } = null!;
}
