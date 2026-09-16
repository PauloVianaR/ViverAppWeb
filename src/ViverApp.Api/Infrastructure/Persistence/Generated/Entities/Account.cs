using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class Account
{
    public ulong Id { get; set; }

    public string RoleCode { get; set; } = null!;

    public string StatusCode { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    public string? PhoneE164 { get; set; }

    public string? TaxId { get; set; }

    public DateTime? BirthDate { get; set; }

    public string? PasswordHash { get; set; }

    public bool EmailVerified { get; set; }

    public bool PhoneVerified { get; set; }

    public bool PortalAccessEnabled { get; set; }

    public string? PreferredRecoveryChannel { get; set; }

    public byte[] SecurityStamp { get; set; } = null!;

    public ushort FailedLoginCount { get; set; }

    public DateTime? LockoutEndUtc { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual AccountAddress? AccountAddress { get; set; }

    public virtual AccountAuthenticator? AccountAuthenticator { get; set; }

    public virtual ICollection<AccountChallenge> AccountChallenges { get; set; } = new List<AccountChallenge>();

    public virtual AccountConsent? AccountConsent { get; set; }

    public virtual ICollection<AccountPasskey> AccountPasskeys { get; set; } = new List<AccountPasskey>();

    public virtual ICollection<AccountRecoveryCode> AccountRecoveryCodes { get; set; } = new List<AccountRecoveryCode>();

    public virtual AccountUiPreference? AccountUiPreference { get; set; }

    public virtual ICollection<AdministratorNotification> AdministratorNotifications { get; set; } = new List<AdministratorNotification>();

    public virtual ICollection<ApplicationSetting> ApplicationSettings { get; set; } = new List<ApplicationSetting>();

    public virtual ICollection<Appointment> AppointmentArrivalRecordedByAccounts { get; set; } = new List<Appointment>();

    public virtual ICollection<Appointment> AppointmentCanceledByAccounts { get; set; } = new List<Appointment>();

    public virtual ICollection<Appointment> AppointmentCompletedByAccounts { get; set; } = new List<Appointment>();

    public virtual ICollection<Appointment> AppointmentCreatedByAccounts { get; set; } = new List<Appointment>();

    public virtual ICollection<AppointmentDocument> AppointmentDocumentDeletedByAccounts { get; set; } = new List<AppointmentDocument>();

    public virtual ICollection<AppointmentDocument> AppointmentDocumentUploadedByAccounts { get; set; } = new List<AppointmentDocument>();

    public virtual ICollection<Appointment> AppointmentNoShowRecordedByAccounts { get; set; } = new List<Appointment>();

    public virtual ICollection<Appointment> AppointmentPatientAccounts { get; set; } = new List<Appointment>();

    public virtual ICollection<AppointmentRescheduleHistory> AppointmentRescheduleHistories { get; set; } = new List<AppointmentRescheduleHistory>();

    public virtual ICollection<AppointmentStatusHistory> AppointmentStatusHistories { get; set; } = new List<AppointmentStatusHistory>();

    public virtual ICollection<AuditEvent> AuditEvents { get; set; } = new List<AuditEvent>();

    public virtual ICollection<AuthSession> AuthSessions { get; set; } = new List<AuthSession>();

    public virtual ICollection<CashClosure> CashClosures { get; set; } = new List<CashClosure>();

    public virtual ICollection<CashMovement> CashMovements { get; set; } = new List<CashMovement>();

    public virtual ICollection<ClinicalAccessEvent> ClinicalAccessEventActorAccounts { get; set; } = new List<ClinicalAccessEvent>();

    public virtual ICollection<ClinicalAccessEvent> ClinicalAccessEventPatientAccounts { get; set; } = new List<ClinicalAccessEvent>();

    public virtual ICollection<ContactChangeRequest> ContactChangeRequests { get; set; } = new List<ContactChangeRequest>();

    public virtual ICollection<DoctorPatientLink> DoctorPatientLinkCreatedByAccounts { get; set; } = new List<DoctorPatientLink>();

    public virtual ICollection<DoctorPatientLink> DoctorPatientLinkPatientAccounts { get; set; } = new List<DoctorPatientLink>();

    public virtual DoctorProfile? DoctorProfile { get; set; }

    public virtual ElectronicHealthRecord? ElectronicHealthRecord { get; set; }

    public virtual ICollection<ExternalLogin> ExternalLogins { get; set; } = new List<ExternalLogin>();

    public virtual ManagerPreference? ManagerPreference { get; set; }

    public virtual ICollection<MedicalRecordDocument> MedicalRecordDocumentDeletedByAccounts { get; set; } = new List<MedicalRecordDocument>();

    public virtual ICollection<MedicalRecordDocument> MedicalRecordDocumentUploadedByAccounts { get; set; } = new List<MedicalRecordDocument>();

    public virtual ICollection<MedicalRecordDraft> MedicalRecordDrafts { get; set; } = new List<MedicalRecordDraft>();

    public virtual ICollection<MedicalRecordEntry> MedicalRecordEntries { get; set; } = new List<MedicalRecordEntry>();

    public virtual ICollection<MedicalRecordVersion> MedicalRecordVersions { get; set; } = new List<MedicalRecordVersion>();

    public virtual PatientPreference? PatientPreference { get; set; }

    public virtual PatientProfile? PatientProfile { get; set; }

    public virtual ICollection<Payment> PaymentConfirmedByAccounts { get; set; } = new List<Payment>();

    public virtual ICollection<PaymentReversal> PaymentReversals { get; set; } = new List<PaymentReversal>();

    public virtual ICollection<Payment> PaymentReversedByAccounts { get; set; } = new List<Payment>();

    public virtual ICollection<PremiumMembership> PremiumMembershipAccounts { get; set; } = new List<PremiumMembership>();

    public virtual ICollection<PremiumMembership> PremiumMembershipReviewedByAccounts { get; set; } = new List<PremiumMembership>();

    public virtual ICollection<PrivateDocument> PrivateDocuments { get; set; } = new List<PrivateDocument>();

    public virtual ICollection<ProfessionalReview> ProfessionalReviewProfessionalAccounts { get; set; } = new List<ProfessionalReview>();

    public virtual ICollection<ProfessionalReview> ProfessionalReviewReviewerAccounts { get; set; } = new List<ProfessionalReview>();

    public virtual Role RoleCodeNavigation { get; set; } = null!;

    public virtual ICollection<TeleconsultationPeer> TeleconsultationPeers { get; set; } = new List<TeleconsultationPeer>();
}
