using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class Appointment
{
    public ulong Id { get; set; }

    public ulong PatientAccountId { get; set; }

    public ulong DoctorAccountId { get; set; }

    public uint AppointmentTypeId { get; set; }

    public ulong CreatedByAccountId { get; set; }

    public string StatusCode { get; set; } = null!;

    public string ModalityCode { get; set; } = null!;

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public decimal PriceAmount { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public string? PatientNotes { get; set; }

    public string? CancellationReason { get; set; }

    public ulong? CanceledByAccountId { get; set; }

    public DateTime? CanceledAtUtc { get; set; }

    public ulong? RescheduledFromAppointmentId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual ICollection<AppointmentDocument> AppointmentDocuments { get; set; } = new List<AppointmentDocument>();

    public virtual ICollection<AppointmentStatusHistory> AppointmentStatusHistories { get; set; } = new List<AppointmentStatusHistory>();

    public virtual AppointmentType AppointmentType { get; set; } = null!;

    public virtual Account? CanceledByAccount { get; set; }

    public virtual Account CreatedByAccount { get; set; } = null!;

    public virtual DoctorProfile DoctorAccount { get; set; } = null!;

    public virtual Appointment? InverseRescheduledFromAppointment { get; set; }

    public virtual Account PatientAccount { get; set; } = null!;

    public virtual Payment? Payment { get; set; }

    public virtual Appointment? RescheduledFromAppointment { get; set; }
}
