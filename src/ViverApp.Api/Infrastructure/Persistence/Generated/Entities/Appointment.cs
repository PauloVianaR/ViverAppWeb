using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class Appointment
{
    public ulong Id { get; set; }

    public ulong AppointmentNumber { get; set; }

    public ulong PatientAccountId { get; set; }

    public ulong ProfessionalAccountId { get; set; }

    public uint AppointmentTypeId { get; set; }

    public ulong CreatedByAccountId { get; set; }

    public string StatusCode { get; set; } = null!;

    public string ModalityCode { get; set; } = null!;

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public decimal PriceAmount { get; set; }

    public bool RequiresPayment { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public string? PatientNotes { get; set; }

    public string? CancellationReason { get; set; }

    public ulong? CanceledByAccountId { get; set; }

    public DateTime? CanceledAtUtc { get; set; }

    public ulong? RescheduledFromAppointmentId { get; set; }

    public ulong? CompletedByAccountId { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public ulong? NoShowRecordedByAccountId { get; set; }

    public DateTime? NoShowRecordedAtUtc { get; set; }

    public DateTime? ArrivedAtUtc { get; set; }

    public DateTime? ArrivalBusinessDate { get; set; }

    public uint? ArrivalQueueNumber { get; set; }

    public ulong? ArrivalRecordedByAccountId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public decimal? BasePriceAmount { get; set; }

    public decimal DiscountPercent { get; set; }

    public string PaymentLocationCode { get; set; } = null!;

    public ulong? CurrentPaymentId { get; set; }

    public virtual ICollection<AppointmentDocument> AppointmentDocuments { get; set; } = new List<AppointmentDocument>();

    public virtual ICollection<AppointmentRescheduleHistory> AppointmentRescheduleHistories { get; set; } = new List<AppointmentRescheduleHistory>();

    public virtual AppointmentReview? AppointmentReview { get; set; }

    public virtual ICollection<AppointmentStatusHistory> AppointmentStatusHistories { get; set; } = new List<AppointmentStatusHistory>();

    public virtual AppointmentType AppointmentType { get; set; } = null!;

    public virtual Account? ArrivalRecordedByAccount { get; set; }

    public virtual Account? CanceledByAccount { get; set; }

    public virtual ICollection<CashMovement> CashMovements { get; set; } = new List<CashMovement>();

    public virtual Account? CompletedByAccount { get; set; }

    public virtual Account CreatedByAccount { get; set; } = null!;

    public virtual Payment? CurrentPayment { get; set; }

    public virtual Appointment? InverseRescheduledFromAppointment { get; set; }

    public virtual ICollection<MedicalRecordDocument> MedicalRecordDocuments { get; set; } = new List<MedicalRecordDocument>();

    public virtual ICollection<MedicalRecordDraft> MedicalRecordDrafts { get; set; } = new List<MedicalRecordDraft>();

    public virtual MedicalRecordEntry? MedicalRecordEntry { get; set; }

    public virtual MedicalReport? MedicalReport { get; set; }

    public virtual Account? NoShowRecordedByAccount { get; set; }

    public virtual Account PatientAccount { get; set; } = null!;

    public virtual ICollection<Payment> PaymentAppointment1s { get; set; } = new List<Payment>();

    public virtual ICollection<Payment> PaymentAppointmentNavigations { get; set; } = new List<Payment>();

    public virtual ICollection<Payment> PaymentProviderReferenceAppointments { get; set; } = new List<Payment>();

    public virtual ProfessionalProfile ProfessionalAccount { get; set; } = null!;

    public virtual ICollection<ProfessionalNotification> ProfessionalNotifications { get; set; } = new List<ProfessionalNotification>();

    public virtual Appointment? RescheduledFromAppointment { get; set; }

    public virtual ICollection<ScheduledJob> ScheduledJobs { get; set; } = new List<ScheduledJob>();

    public virtual TeleconsultationGuestLink? TeleconsultationGuestLink { get; set; }

    public virtual ICollection<TeleconsultationPeer> TeleconsultationPeers { get; set; } = new List<TeleconsultationPeer>();
}
