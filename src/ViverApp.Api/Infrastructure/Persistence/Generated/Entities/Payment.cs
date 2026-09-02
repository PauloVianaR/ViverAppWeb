using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class Payment
{
    public ulong Id { get; set; }

    public ulong AppointmentId { get; set; }

    public string ProviderCode { get; set; } = null!;

    public string StatusCode { get; set; } = null!;

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public Guid IdempotencyKey { get; set; }

    public string? ProviderCheckoutId { get; set; }

    public string? ProviderTransactionId { get; set; }

    public string? ProviderStatusCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public DateTime? CanceledAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;
}
