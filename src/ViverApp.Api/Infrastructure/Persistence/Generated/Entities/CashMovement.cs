using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class CashMovement
{
    public ulong Id { get; set; }

    public DateTime OperationalDate { get; set; }

    public string DirectionCode { get; set; } = null!;

    public string TypeCode { get; set; } = null!;

    public string MethodCode { get; set; } = null!;

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public ulong? AppointmentId { get; set; }

    public ulong? PaymentId { get; set; }

    public ulong? RelatedMovementId { get; set; }

    public ulong? ResponsibleAccountId { get; set; }

    public string Description { get; set; } = null!;

    public string? Reason { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public DateTime OccurredAtUtc { get; set; }

    public bool AfterClosure { get; set; }

    public string? CardLastFour { get; set; }

    public string? AuthorizationReference { get; set; }

    public virtual Appointment? Appointment { get; set; }

    public virtual ICollection<CashClosure> CashClosures { get; set; } = new List<CashClosure>();

    public virtual ICollection<CashMovement> InverseRelatedMovement { get; set; } = new List<CashMovement>();

    public virtual Payment? Payment { get; set; }

    public virtual CashMovement? RelatedMovement { get; set; }

    public virtual Account? ResponsibleAccount { get; set; }
}
