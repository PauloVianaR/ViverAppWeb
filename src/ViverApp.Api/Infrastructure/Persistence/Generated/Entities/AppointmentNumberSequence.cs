using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class AppointmentNumberSequence
{
    public byte SequenceKey { get; set; }

    public ulong NextValue { get; set; }
}
