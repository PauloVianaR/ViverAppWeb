using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ArrivalQueueSequence
{
    public DateTime BusinessDate { get; set; }

    public uint NextValue { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
