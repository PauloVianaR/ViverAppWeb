using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class Holiday
{
    public uint Id { get; set; }

    public DateTime HolidayDate { get; set; }

    public string Name { get; set; } = null!;

    public TimeSpan? StartTime { get; set; }

    public TimeSpan? EndTime { get; set; }
}
