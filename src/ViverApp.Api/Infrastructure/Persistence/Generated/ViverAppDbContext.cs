using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Infrastructure.Persistence.Generated;

public partial class ViverAppDbContext : DbContext
{
    public ViverAppDbContext(DbContextOptions<ViverAppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Account> Accounts { get; set; }

    public virtual DbSet<AccountAddress> AccountAddresses { get; set; }

    public virtual DbSet<AccountAuthenticator> AccountAuthenticators { get; set; }

    public virtual DbSet<AccountChallenge> AccountChallenges { get; set; }

    public virtual DbSet<AccountConsent> AccountConsents { get; set; }

    public virtual DbSet<AccountPasskey> AccountPasskeys { get; set; }

    public virtual DbSet<AccountRecoveryCode> AccountRecoveryCodes { get; set; }

    public virtual DbSet<AdministratorNotification> AdministratorNotifications { get; set; }

    public virtual DbSet<ApplicationSetting> ApplicationSettings { get; set; }

    public virtual DbSet<Appointment> Appointments { get; set; }

    public virtual DbSet<AppointmentDocument> AppointmentDocuments { get; set; }

    public virtual DbSet<AppointmentReview> AppointmentReviews { get; set; }

    public virtual DbSet<AppointmentStatusHistory> AppointmentStatusHistories { get; set; }

    public virtual DbSet<AppointmentType> AppointmentTypes { get; set; }

    public virtual DbSet<AuditEvent> AuditEvents { get; set; }

    public virtual DbSet<AuthSession> AuthSessions { get; set; }

    public virtual DbSet<Clinic> Clinics { get; set; }

    public virtual DbSet<ClinicWeeklyHour> ClinicWeeklyHours { get; set; }

    public virtual DbSet<ContactChangeRequest> ContactChangeRequests { get; set; }

    public virtual DbSet<DoctorAvailabilityException> DoctorAvailabilityExceptions { get; set; }

    public virtual DbSet<DoctorPatientLink> DoctorPatientLinks { get; set; }

    public virtual DbSet<DoctorPreference> DoctorPreferences { get; set; }

    public virtual DbSet<DoctorProfile> DoctorProfiles { get; set; }

    public virtual DbSet<DoctorService> DoctorServices { get; set; }

    public virtual DbSet<DoctorSpecialty> DoctorSpecialties { get; set; }

    public virtual DbSet<DoctorWeeklyHour> DoctorWeeklyHours { get; set; }

    public virtual DbSet<ExternalLogin> ExternalLogins { get; set; }

    public virtual DbSet<Holiday> Holidays { get; set; }

    public virtual DbSet<IdempotencyRecord> IdempotencyRecords { get; set; }

    public virtual DbSet<ManagerPreference> ManagerPreferences { get; set; }

    public virtual DbSet<MedicalReport> MedicalReports { get; set; }

    public virtual DbSet<MedicalReportVersion> MedicalReportVersions { get; set; }

    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; }

    public virtual DbSet<PatientPreference> PatientPreferences { get; set; }

    public virtual DbSet<PatientProfile> PatientProfiles { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<PaymentEvent> PaymentEvents { get; set; }

    public virtual DbSet<PaymentWebhookReceipt> PaymentWebhookReceipts { get; set; }

    public virtual DbSet<PremiumMembership> PremiumMemberships { get; set; }

    public virtual DbSet<PremiumPlan> PremiumPlans { get; set; }

    public virtual DbSet<PrivateDocument> PrivateDocuments { get; set; }

    public virtual DbSet<ProfessionalReview> ProfessionalReviews { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Specialty> Specialties { get; set; }

    public virtual DbSet<TeleconsultationPeer> TeleconsultationPeers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("accounts");

            entity.HasIndex(e => new { e.RoleCode, e.StatusCode }, "ix_accounts_role_status");

            entity.HasIndex(e => e.NormalizedEmail, "ux_accounts_normalized_email").IsUnique();

            entity.HasIndex(e => e.PhoneE164, "ux_accounts_phone_e164").IsUnique();

            entity.HasIndex(e => e.TaxId, "ux_accounts_tax_id").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BirthDate)
                .HasColumnType("date")
                .HasColumnName("birth_date");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.Email)
                .HasMaxLength(254)
                .HasColumnName("email");
            entity.Property(e => e.EmailVerified).HasColumnName("email_verified");
            entity.Property(e => e.FailedLoginCount).HasColumnName("failed_login_count");
            entity.Property(e => e.FullName)
                .HasMaxLength(200)
                .HasColumnName("full_name");
            entity.Property(e => e.LastLoginAtUtc)
                .HasMaxLength(6)
                .HasColumnName("last_login_at_utc");
            entity.Property(e => e.LockoutEndUtc)
                .HasMaxLength(6)
                .HasColumnName("lockout_end_utc");
            entity.Property(e => e.NormalizedEmail)
                .HasMaxLength(254)
                .HasColumnName("normalized_email");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(512)
                .HasColumnName("password_hash");
            entity.Property(e => e.PhoneE164)
                .HasMaxLength(16)
                .HasColumnName("phone_e164");
            entity.Property(e => e.PhoneVerified).HasColumnName("phone_verified");
            entity.Property(e => e.PreferredRecoveryChannel)
                .HasMaxLength(10)
                .HasColumnName("preferred_recovery_channel");
            entity.Property(e => e.RoleCode)
                .HasMaxLength(20)
                .HasColumnName("role_code");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.SecurityStamp)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("security_stamp");
            entity.Property(e => e.StatusCode)
                .HasMaxLength(30)
                .HasDefaultValueSql("'pending_confirmation'")
                .HasColumnName("status_code");
            entity.Property(e => e.TaxId)
                .HasMaxLength(11)
                .IsFixedLength()
                .HasColumnName("tax_id");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.RoleCodeNavigation).WithMany(p => p.Accounts)
                .HasForeignKey(d => d.RoleCode)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_accounts_role");
        });

        modelBuilder.Entity<AccountAddress>(entity =>
        {
            entity.HasKey(e => e.AccountId).HasName("PRIMARY");

            entity.ToTable("account_addresses");

            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.City)
                .HasMaxLength(100)
                .HasColumnName("city");
            entity.Property(e => e.Complement)
                .HasMaxLength(100)
                .HasColumnName("complement");
            entity.Property(e => e.District)
                .HasMaxLength(100)
                .HasColumnName("district");
            entity.Property(e => e.Number)
                .HasMaxLength(20)
                .HasColumnName("number");
            entity.Property(e => e.PostalCode)
                .HasMaxLength(8)
                .IsFixedLength()
                .HasColumnName("postal_code");
            entity.Property(e => e.StateCode)
                .HasMaxLength(2)
                .IsFixedLength()
                .HasColumnName("state_code");
            entity.Property(e => e.Street)
                .HasMaxLength(200)
                .HasColumnName("street");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.Account).WithOne(p => p.AccountAddress)
                .HasForeignKey<AccountAddress>(d => d.AccountId)
                .HasConstraintName("fk_account_addresses_account");
        });

        modelBuilder.Entity<AccountAuthenticator>(entity =>
        {
            entity.HasKey(e => e.AccountId).HasName("PRIMARY");

            entity.ToTable("account_authenticators");

            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.EnabledAtUtc)
                .HasMaxLength(6)
                .HasColumnName("enabled_at_utc");
            entity.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            entity.Property(e => e.ProtectedKey)
                .HasMaxLength(2048)
                .HasColumnName("protected_key");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.Account).WithOne(p => p.AccountAuthenticator)
                .HasForeignKey<AccountAuthenticator>(d => d.AccountId)
                .HasConstraintName("fk_account_authenticators_account");
        });

        modelBuilder.Entity<AccountChallenge>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("account_challenges");

            entity.HasIndex(e => new { e.AccountId, e.PurposeCode, e.CreatedAtUtc }, "ix_account_challenges_account_purpose");

            entity.Property(e => e.Id)
                .HasMaxLength(16)
                .IsFixedLength()
                .HasColumnName("id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
            entity.Property(e => e.ChannelCode)
                .HasMaxLength(10)
                .HasColumnName("channel_code");
            entity.Property(e => e.ConsumedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("consumed_at_utc");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.DestinationHash)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("destination_hash");
            entity.Property(e => e.ExpiresAtUtc)
                .HasMaxLength(6)
                .HasColumnName("expires_at_utc");
            entity.Property(e => e.MaxAttempts)
                .HasDefaultValueSql("'5'")
                .HasColumnName("max_attempts");
            entity.Property(e => e.PurposeCode)
                .HasMaxLength(30)
                .HasColumnName("purpose_code");
            entity.Property(e => e.SecretHash)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("secret_hash");

            entity.HasOne(d => d.Account).WithMany(p => p.AccountChallenges)
                .HasForeignKey(d => d.AccountId)
                .HasConstraintName("fk_account_challenges_account");
        });

        modelBuilder.Entity<AccountConsent>(entity =>
        {
            entity.HasKey(e => e.AccountId).HasName("PRIMARY");

            entity.ToTable("account_consents");

            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.AcceptedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("accepted_at_utc");
            entity.Property(e => e.PrivacyVersion)
                .HasMaxLength(30)
                .HasColumnName("privacy_version");
            entity.Property(e => e.SourceCode)
                .HasMaxLength(20)
                .HasColumnName("source_code");
            entity.Property(e => e.TermsVersion)
                .HasMaxLength(30)
                .HasColumnName("terms_version");

            entity.HasOne(d => d.Account).WithOne(p => p.AccountConsent)
                .HasForeignKey<AccountConsent>(d => d.AccountId)
                .HasConstraintName("fk_account_consents_account");
        });

        modelBuilder.Entity<AccountPasskey>(entity =>
        {
            entity.HasKey(e => e.CredentialId).HasName("PRIMARY");

            entity.ToTable("account_passkeys");

            entity.HasIndex(e => new { e.AccountId, e.CreatedAtUtc }, "ix_account_passkeys_account");

            entity.Property(e => e.CredentialId)
                .HasMaxLength(1024)
                .HasColumnName("credential_id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.AttestationObject)
                .HasColumnType("blob")
                .HasColumnName("attestation_object");
            entity.Property(e => e.ClientDataJson)
                .HasColumnType("blob")
                .HasColumnName("client_data_json");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.DisplayName)
                .HasMaxLength(100)
                .HasColumnName("display_name");
            entity.Property(e => e.IsBackedUp).HasColumnName("is_backed_up");
            entity.Property(e => e.IsBackupEligible).HasColumnName("is_backup_eligible");
            entity.Property(e => e.IsUserVerified).HasColumnName("is_user_verified");
            entity.Property(e => e.PublicKey)
                .HasMaxLength(2048)
                .HasColumnName("public_key");
            entity.Property(e => e.SignCount).HasColumnName("sign_count");
            entity.Property(e => e.TransportsJson)
                .HasColumnType("json")
                .HasColumnName("transports_json");

            entity.HasOne(d => d.Account).WithMany(p => p.AccountPasskeys)
                .HasForeignKey(d => d.AccountId)
                .HasConstraintName("fk_account_passkeys_account");
        });

        modelBuilder.Entity<AccountRecoveryCode>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("account_recovery_codes");

            entity.HasIndex(e => new { e.AccountId, e.UsedAtUtc }, "ix_account_recovery_codes_available");

            entity.HasIndex(e => new { e.AccountId, e.CodeHash }, "ux_account_recovery_codes_hash").IsUnique();

            entity.Property(e => e.Id)
                .HasMaxLength(16)
                .IsFixedLength()
                .HasColumnName("id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.CodeHash)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("code_hash");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.UsedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("used_at_utc");

            entity.HasOne(d => d.Account).WithMany(p => p.AccountRecoveryCodes)
                .HasForeignKey(d => d.AccountId)
                .HasConstraintName("fk_account_recovery_codes_account");
        });

        modelBuilder.Entity<AdministratorNotification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("administrator_notifications");

            entity.HasIndex(e => new { e.AdministratorAccountId, e.DismissedAtUtc, e.CreatedAtUtc }, "ix_administrator_notifications_feed");

            entity.HasIndex(e => new { e.AdministratorAccountId, e.SourceKey }, "uq_administrator_notifications_source").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AdministratorAccountId).HasColumnName("administrator_account_id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.DismissedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("dismissed_at_utc");
            entity.Property(e => e.EntityId)
                .HasMaxLength(80)
                .HasColumnName("entity_id");
            entity.Property(e => e.EntityType)
                .HasMaxLength(40)
                .HasColumnName("entity_type");
            entity.Property(e => e.Message)
                .HasMaxLength(500)
                .HasColumnName("message");
            entity.Property(e => e.ReadAtUtc)
                .HasMaxLength(6)
                .HasColumnName("read_at_utc");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.SeverityCode)
                .HasMaxLength(16)
                .HasColumnName("severity_code");
            entity.Property(e => e.SourceKey)
                .HasMaxLength(180)
                .HasColumnName("source_key");
            entity.Property(e => e.Title)
                .HasMaxLength(160)
                .HasColumnName("title");
            entity.Property(e => e.TypeCode)
                .HasMaxLength(40)
                .HasColumnName("type_code");

            entity.HasOne(d => d.AdministratorAccount).WithMany(p => p.AdministratorNotifications)
                .HasForeignKey(d => d.AdministratorAccountId)
                .HasConstraintName("fk_administrator_notifications_account");
        });

        modelBuilder.Entity<ApplicationSetting>(entity =>
        {
            entity.HasKey(e => e.SettingKey).HasName("PRIMARY");

            entity.ToTable("application_settings");

            entity.HasIndex(e => e.UpdatedByAccountId, "fk_application_settings_updater");

            entity.Property(e => e.SettingKey)
                .HasMaxLength(100)
                .HasColumnName("setting_key");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
            entity.Property(e => e.IsSecret).HasColumnName("is_secret");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedByAccountId).HasColumnName("updated_by_account_id");
            entity.Property(e => e.ValueJson)
                .HasColumnType("json")
                .HasColumnName("value_json");

            entity.HasOne(d => d.UpdatedByAccount).WithMany(p => p.ApplicationSettings)
                .HasForeignKey(d => d.UpdatedByAccountId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_application_settings_updater");
        });

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("appointments");

            entity.HasIndex(e => e.CanceledByAccountId, "fk_appointments_canceler");

            entity.HasIndex(e => e.CompletedByAccountId, "fk_appointments_completer");

            entity.HasIndex(e => e.CreatedByAccountId, "fk_appointments_creator");

            entity.HasIndex(e => e.NoShowRecordedByAccountId, "fk_appointments_no_show_actor");

            entity.HasIndex(e => e.AppointmentTypeId, "fk_appointments_type");

            entity.HasIndex(e => new { e.DoctorAccountId, e.StatusCode, e.StartsAtUtc, e.EndsAtUtc }, "ix_appointments_doctor_period");

            entity.HasIndex(e => new { e.DoctorAccountId, e.StatusCode, e.EndsAtUtc }, "ix_appointments_doctor_status_end");

            entity.HasIndex(e => new { e.PatientAccountId, e.DoctorAccountId, e.StatusCode }, "ix_appointments_patient_doctor_status");

            entity.HasIndex(e => new { e.PatientAccountId, e.StatusCode, e.StartsAtUtc, e.EndsAtUtc }, "ix_appointments_patient_period");

            entity.HasIndex(e => new { e.PatientAccountId, e.StatusCode, e.StartsAtUtc }, "ix_appointments_patient_status");

            entity.HasIndex(e => new { e.StatusCode, e.StartsAtUtc }, "ix_appointments_status_start");

            entity.HasIndex(e => new { e.DoctorAccountId, e.StartsAtUtc }, "ux_appointments_doctor_start").IsUnique();

            entity.HasIndex(e => new { e.PatientAccountId, e.StartsAtUtc }, "ux_appointments_patient_start").IsUnique();

            entity.HasIndex(e => e.RescheduledFromAppointmentId, "ux_appointments_rescheduled_from").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppointmentTypeId).HasColumnName("appointment_type_id");
            entity.Property(e => e.BasePriceAmount)
                .HasPrecision(13)
                .HasColumnName("base_price_amount");
            entity.Property(e => e.CanceledAtUtc)
                .HasMaxLength(6)
                .HasColumnName("canceled_at_utc");
            entity.Property(e => e.CanceledByAccountId).HasColumnName("canceled_by_account_id");
            entity.Property(e => e.CancellationReason)
                .HasMaxLength(500)
                .HasColumnName("cancellation_reason");
            entity.Property(e => e.CompletedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("completed_at_utc");
            entity.Property(e => e.CompletedByAccountId).HasColumnName("completed_by_account_id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedByAccountId).HasColumnName("created_by_account_id");
            entity.Property(e => e.CurrencyCode)
                .HasMaxLength(3)
                .HasDefaultValueSql("'BRL'")
                .IsFixedLength()
                .HasColumnName("currency_code");
            entity.Property(e => e.DiscountPercent)
                .HasPrecision(5)
                .HasColumnName("discount_percent");
            entity.Property(e => e.DoctorAccountId).HasColumnName("doctor_account_id");
            entity.Property(e => e.EndsAtUtc)
                .HasMaxLength(6)
                .HasColumnName("ends_at_utc");
            entity.Property(e => e.ModalityCode)
                .HasMaxLength(20)
                .HasColumnName("modality_code");
            entity.Property(e => e.NoShowRecordedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("no_show_recorded_at_utc");
            entity.Property(e => e.NoShowRecordedByAccountId).HasColumnName("no_show_recorded_by_account_id");
            entity.Property(e => e.PatientAccountId).HasColumnName("patient_account_id");
            entity.Property(e => e.PatientNotes)
                .HasMaxLength(1000)
                .HasColumnName("patient_notes");
            entity.Property(e => e.PaymentLocationCode)
                .HasMaxLength(10)
                .HasDefaultValueSql("'web'")
                .HasColumnName("payment_location_code");
            entity.Property(e => e.PriceAmount)
                .HasPrecision(13)
                .HasColumnName("price_amount");
            entity.Property(e => e.RescheduledFromAppointmentId).HasColumnName("rescheduled_from_appointment_id");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.StartsAtUtc)
                .HasMaxLength(6)
                .HasColumnName("starts_at_utc");
            entity.Property(e => e.StatusCode)
                .HasMaxLength(20)
                .HasDefaultValueSql("'pending'")
                .HasColumnName("status_code");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.AppointmentType).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.AppointmentTypeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointments_type");

            entity.HasOne(d => d.CanceledByAccount).WithMany(p => p.AppointmentCanceledByAccounts)
                .HasForeignKey(d => d.CanceledByAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointments_canceler");

            entity.HasOne(d => d.CompletedByAccount).WithMany(p => p.AppointmentCompletedByAccounts)
                .HasForeignKey(d => d.CompletedByAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointments_completer");

            entity.HasOne(d => d.CreatedByAccount).WithMany(p => p.AppointmentCreatedByAccounts)
                .HasForeignKey(d => d.CreatedByAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointments_creator");

            entity.HasOne(d => d.DoctorAccount).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.DoctorAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointments_doctor");

            entity.HasOne(d => d.NoShowRecordedByAccount).WithMany(p => p.AppointmentNoShowRecordedByAccounts)
                .HasForeignKey(d => d.NoShowRecordedByAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointments_no_show_actor");

            entity.HasOne(d => d.PatientAccount).WithMany(p => p.AppointmentPatientAccounts)
                .HasForeignKey(d => d.PatientAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointments_patient");

            entity.HasOne(d => d.RescheduledFromAppointment).WithOne(p => p.InverseRescheduledFromAppointment)
                .HasForeignKey<Appointment>(d => d.RescheduledFromAppointmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointments_rescheduled_from");
        });

        modelBuilder.Entity<AppointmentDocument>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("appointment_documents");

            entity.HasIndex(e => e.DeletedByAccountId, "fk_appointment_documents_deleted_by");

            entity.HasIndex(e => e.UploadedByAccountId, "fk_appointment_documents_uploader");

            entity.HasIndex(e => new { e.AppointmentId, e.CreatedAtUtc }, "ix_appointment_documents_appointment");

            entity.HasIndex(e => e.ObjectKey, "ux_appointment_documents_object_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppointmentId).HasColumnName("appointment_id");
            entity.Property(e => e.AvailableAtUtc)
                .HasMaxLength(6)
                .HasColumnName("available_at_utc");
            entity.Property(e => e.CategoryCode)
                .HasMaxLength(30)
                .HasColumnName("category_code");
            entity.Property(e => e.ContentType)
                .HasMaxLength(127)
                .HasColumnName("content_type");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.DeletedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("deleted_at_utc");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.ObjectKey)
                .HasMaxLength(512)
                .HasColumnName("object_key");
            entity.Property(e => e.OriginalFileName)
                .HasMaxLength(255)
                .HasColumnName("original_file_name");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.Sha256)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("sha256");
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entity.Property(e => e.StatusCode)
                .HasMaxLength(20)
                .HasDefaultValueSql("'pending_scan'")
                .HasColumnName("status_code");
            entity.Property(e => e.UploadedByAccountId).HasColumnName("uploaded_by_account_id");

            entity.HasOne(d => d.Appointment).WithMany(p => p.AppointmentDocuments)
                .HasForeignKey(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointment_documents_appointment");

            entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.AppointmentDocumentDeletedByAccounts)
                .HasForeignKey(d => d.DeletedByAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointment_documents_deleted_by");

            entity.HasOne(d => d.UploadedByAccount).WithMany(p => p.AppointmentDocumentUploadedByAccounts)
                .HasForeignKey(d => d.UploadedByAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointment_documents_uploader");
        });

        modelBuilder.Entity<AppointmentReview>(entity =>
        {
            entity.HasKey(e => e.AppointmentId).HasName("PRIMARY");

            entity.ToTable("appointment_reviews");

            entity.Property(e => e.AppointmentId).HasColumnName("appointment_id");
            entity.Property(e => e.Comment)
                .HasMaxLength(1000)
                .HasColumnName("comment");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.Rating).HasColumnName("rating");

            entity.HasOne(d => d.Appointment).WithOne(p => p.AppointmentReview)
                .HasForeignKey<AppointmentReview>(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointment_reviews_appointment");
        });

        modelBuilder.Entity<AppointmentStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("appointment_status_history");

            entity.HasIndex(e => new { e.ActorAccountId, e.OccurredAtUtc }, "ix_appointment_status_history_actor_time");

            entity.HasIndex(e => new { e.AppointmentId, e.OccurredAtUtc }, "ix_appointment_status_history_appointment_time");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ActorAccountId).HasColumnName("actor_account_id");
            entity.Property(e => e.AppointmentId).HasColumnName("appointment_id");
            entity.Property(e => e.EndsAtUtc)
                .HasMaxLength(6)
                .HasColumnName("ends_at_utc");
            entity.Property(e => e.FromStatusCode)
                .HasMaxLength(20)
                .HasColumnName("from_status_code");
            entity.Property(e => e.OccurredAtUtc)
                .HasMaxLength(6)
                .HasColumnName("occurred_at_utc");
            entity.Property(e => e.Reason)
                .HasMaxLength(500)
                .HasColumnName("reason");
            entity.Property(e => e.StartsAtUtc)
                .HasMaxLength(6)
                .HasColumnName("starts_at_utc");
            entity.Property(e => e.ToStatusCode)
                .HasMaxLength(20)
                .HasColumnName("to_status_code");

            entity.HasOne(d => d.ActorAccount).WithMany(p => p.AppointmentStatusHistories)
                .HasForeignKey(d => d.ActorAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointment_status_history_actor");

            entity.HasOne(d => d.Appointment).WithMany(p => p.AppointmentStatusHistories)
                .HasForeignKey(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appointment_status_history_appointment");
        });

        modelBuilder.Entity<AppointmentType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("appointment_types");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CategoryCode)
                .HasMaxLength(20)
                .HasDefaultValueSql("'consultation'")
                .HasColumnName("category_code");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
            entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
            entity.Property(e => e.DurationMinutes).HasColumnName("duration_minutes");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.ModalityCode)
                .HasMaxLength(20)
                .HasColumnName("modality_code");
            entity.Property(e => e.Name)
                .HasMaxLength(120)
                .HasColumnName("name");
            entity.Property(e => e.PriceAmount)
                .HasPrecision(13)
                .HasColumnName("price_amount");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("audit_events");

            entity.HasIndex(e => new { e.ActorAccountId, e.OccurredAtUtc }, "ix_audit_events_actor_time");

            entity.HasIndex(e => e.CorrelationId, "ix_audit_events_correlation");

            entity.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredAtUtc }, "ix_audit_events_entity");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ActorAccountId).HasColumnName("actor_account_id");
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
            entity.Property(e => e.DataJson)
                .HasColumnType("json")
                .HasColumnName("data_json");
            entity.Property(e => e.EntityId)
                .HasMaxLength(100)
                .HasColumnName("entity_id");
            entity.Property(e => e.EntityType)
                .HasMaxLength(100)
                .HasColumnName("entity_type");
            entity.Property(e => e.EventCode)
                .HasMaxLength(100)
                .HasColumnName("event_code");
            entity.Property(e => e.IpAddressHash)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("ip_address_hash");
            entity.Property(e => e.OccurredAtUtc)
                .HasMaxLength(6)
                .HasColumnName("occurred_at_utc");

            entity.HasOne(d => d.ActorAccount).WithMany(p => p.AuditEvents)
                .HasForeignKey(d => d.ActorAccountId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_audit_events_actor");
        });

        modelBuilder.Entity<AuthSession>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("auth_sessions");

            entity.HasIndex(e => new { e.AccountId, e.ExpiresAtUtc }, "ix_auth_sessions_account_expiry");

            entity.HasIndex(e => e.RefreshTokenHash, "ux_auth_sessions_refresh_hash").IsUnique();

            entity.Property(e => e.Id)
                .HasMaxLength(16)
                .IsFixedLength()
                .HasColumnName("id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.AuthenticationMethod)
                .HasMaxLength(20)
                .HasDefaultValueSql("'password'")
                .HasColumnName("authentication_method");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.ExpiresAtUtc)
                .HasMaxLength(6)
                .HasColumnName("expires_at_utc");
            entity.Property(e => e.IpAddressHash)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("ip_address_hash");
            entity.Property(e => e.LastSeenAtUtc)
                .HasMaxLength(6)
                .HasColumnName("last_seen_at_utc");
            entity.Property(e => e.MfaSatisfied).HasColumnName("mfa_satisfied");
            entity.Property(e => e.RefreshTokenHash)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("refresh_token_hash");
            entity.Property(e => e.RevokeReasonCode)
                .HasMaxLength(30)
                .HasColumnName("revoke_reason_code");
            entity.Property(e => e.RevokedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("revoked_at_utc");
            entity.Property(e => e.UserAgentHash)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("user_agent_hash");

            entity.HasOne(d => d.Account).WithMany(p => p.AuthSessions)
                .HasForeignKey(d => d.AccountId)
                .HasConstraintName("fk_auth_sessions_account");
        });

        modelBuilder.Entity<Clinic>(entity =>
        {
            entity.HasKey(e => e.SingletonId).HasName("PRIMARY");

            entity.ToTable("clinic");

            entity.HasIndex(e => e.TaxId, "ux_clinic_tax_id").IsUnique();

            entity.Property(e => e.SingletonId)
                .HasDefaultValueSql("'1'")
                .HasColumnName("singleton_id");
            entity.Property(e => e.City)
                .HasMaxLength(100)
                .HasColumnName("city");
            entity.Property(e => e.Complement)
                .HasMaxLength(100)
                .HasColumnName("complement");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.DisplayName)
                .HasMaxLength(120)
                .HasColumnName("display_name");
            entity.Property(e => e.District)
                .HasMaxLength(100)
                .HasColumnName("district");
            entity.Property(e => e.Email)
                .HasMaxLength(254)
                .HasColumnName("email");
            entity.Property(e => e.LegalName)
                .HasMaxLength(200)
                .HasColumnName("legal_name");
            entity.Property(e => e.Number)
                .HasMaxLength(20)
                .HasColumnName("number");
            entity.Property(e => e.PhoneE164)
                .HasMaxLength(16)
                .HasColumnName("phone_e164");
            entity.Property(e => e.PostalCode)
                .HasMaxLength(8)
                .IsFixedLength()
                .HasColumnName("postal_code");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.StateCode)
                .HasMaxLength(2)
                .IsFixedLength()
                .HasColumnName("state_code");
            entity.Property(e => e.Street)
                .HasMaxLength(200)
                .HasColumnName("street");
            entity.Property(e => e.TaxId)
                .HasMaxLength(14)
                .IsFixedLength()
                .HasColumnName("tax_id");
            entity.Property(e => e.TimezoneName)
                .HasMaxLength(64)
                .HasDefaultValueSql("'America/Sao_Paulo'")
                .HasColumnName("timezone_name");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<ClinicWeeklyHour>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("clinic_weekly_hours");

            entity.HasIndex(e => new { e.DayOfWeek, e.StartTime, e.EndTime }, "ux_clinic_weekly_hours").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.DayOfWeek).HasColumnName("day_of_week");
            entity.Property(e => e.EndTime)
                .HasColumnType("time")
                .HasColumnName("end_time");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.StartTime)
                .HasColumnType("time")
                .HasColumnName("start_time");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<ContactChangeRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("contact_change_requests");

            entity.HasIndex(e => e.AccountId, "fk_contact_change_account");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.ChallengeId)
                .HasMaxLength(16)
                .IsFixedLength()
                .HasColumnName("challenge_id");
            entity.Property(e => e.ChannelCode)
                .HasMaxLength(10)
                .HasColumnName("channel_code");
            entity.Property(e => e.CompletedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("completed_at_utc");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.Destination)
                .HasMaxLength(254)
                .HasColumnName("destination");

            entity.HasOne(d => d.Account).WithMany(p => p.ContactChangeRequests)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_contact_change_account");
        });

        modelBuilder.Entity<DoctorAvailabilityException>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("doctor_availability_exceptions");

            entity.HasIndex(e => new { e.DoctorAccountId, e.ExceptionDate }, "ix_doctor_availability_exception_date");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.DoctorAccountId).HasColumnName("doctor_account_id");
            entity.Property(e => e.EndTime)
                .HasColumnType("time")
                .HasColumnName("end_time");
            entity.Property(e => e.ExceptionDate)
                .HasColumnType("date")
                .HasColumnName("exception_date");
            entity.Property(e => e.IsAvailable).HasColumnName("is_available");
            entity.Property(e => e.ModalityCode)
                .HasMaxLength(10)
                .HasColumnName("modality_code");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.StartTime)
                .HasColumnType("time")
                .HasColumnName("start_time");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.DoctorAccount).WithMany(p => p.DoctorAvailabilityExceptions)
                .HasForeignKey(d => d.DoctorAccountId)
                .HasConstraintName("fk_doctor_availability_exception_doctor");
        });

        modelBuilder.Entity<DoctorPatientLink>(entity =>
        {
            entity.HasKey(e => new { e.DoctorAccountId, e.PatientAccountId }).HasName("PRIMARY");

            entity.ToTable("doctor_patient_links");

            entity.HasIndex(e => e.CreatedByAccountId, "fk_doctor_patient_links_creator");

            entity.HasIndex(e => new { e.PatientAccountId, e.StatusCode }, "ix_doctor_patient_links_patient");

            entity.Property(e => e.DoctorAccountId).HasColumnName("doctor_account_id");
            entity.Property(e => e.PatientAccountId).HasColumnName("patient_account_id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedByAccountId).HasColumnName("created_by_account_id");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.StatusCode)
                .HasMaxLength(10)
                .HasColumnName("status_code");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.CreatedByAccount).WithMany(p => p.DoctorPatientLinkCreatedByAccounts)
                .HasForeignKey(d => d.CreatedByAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_doctor_patient_links_creator");

            entity.HasOne(d => d.DoctorAccount).WithMany(p => p.DoctorPatientLinks)
                .HasForeignKey(d => d.DoctorAccountId)
                .HasConstraintName("fk_doctor_patient_links_doctor");

            entity.HasOne(d => d.PatientAccount).WithMany(p => p.DoctorPatientLinkPatientAccounts)
                .HasForeignKey(d => d.PatientAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_doctor_patient_links_patient");
        });

        modelBuilder.Entity<DoctorPreference>(entity =>
        {
            entity.HasKey(e => e.DoctorAccountId).HasName("PRIMARY");

            entity.ToTable("doctor_preferences");

            entity.Property(e => e.DoctorAccountId).HasColumnName("doctor_account_id");
            entity.Property(e => e.EmailEnabled).HasColumnName("email_enabled");
            entity.Property(e => e.MaxInPersonDaily)
                .HasDefaultValueSql("'16'")
                .HasColumnName("max_in_person_daily");
            entity.Property(e => e.MaxOnlineDaily)
                .HasDefaultValueSql("'8'")
                .HasColumnName("max_online_daily");
            entity.Property(e => e.OnlineEnabled).HasColumnName("online_enabled");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.SmsEnabled).HasColumnName("sms_enabled");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.DoctorAccount).WithOne(p => p.DoctorPreference)
                .HasForeignKey<DoctorPreference>(d => d.DoctorAccountId)
                .HasConstraintName("fk_doctor_preferences_doctor");
        });

        modelBuilder.Entity<DoctorProfile>(entity =>
        {
            entity.HasKey(e => e.AccountId).HasName("PRIMARY");

            entity.ToTable("doctor_profiles");

            entity.HasIndex(e => new { e.LicenseStateCode, e.LicenseNumber }, "ux_doctor_profiles_license").IsUnique();

            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.Biography)
                .HasColumnType("text")
                .HasColumnName("biography");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.DefaultAppointmentDurationMinutes)
                .HasDefaultValueSql("'30'")
                .HasColumnName("default_appointment_duration_minutes");
            entity.Property(e => e.LicenseNumber)
                .HasMaxLength(30)
                .HasColumnName("license_number");
            entity.Property(e => e.LicenseStateCode)
                .HasMaxLength(2)
                .IsFixedLength()
                .HasColumnName("license_state_code");
            entity.Property(e => e.ProfessionalTitle)
                .HasMaxLength(4)
                .HasDefaultValueSql("'Dr.'")
                .HasColumnName("professional_title");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");
            entity.Property(e => e.YearsExperience).HasColumnName("years_experience");

            entity.HasOne(d => d.Account).WithOne(p => p.DoctorProfile)
                .HasForeignKey<DoctorProfile>(d => d.AccountId)
                .HasConstraintName("fk_doctor_profiles_account");
        });

        modelBuilder.Entity<DoctorService>(entity =>
        {
            entity.HasKey(e => new { e.DoctorAccountId, e.AppointmentTypeId }).HasName("PRIMARY");

            entity.ToTable("doctor_services");

            entity.HasIndex(e => new { e.AppointmentTypeId, e.IsActive }, "ix_doctor_services_type_active");

            entity.Property(e => e.DoctorAccountId).HasColumnName("doctor_account_id");
            entity.Property(e => e.AppointmentTypeId).HasColumnName("appointment_type_id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.AppointmentType).WithMany(p => p.DoctorServices)
                .HasForeignKey(d => d.AppointmentTypeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_doctor_services_type");

            entity.HasOne(d => d.DoctorAccount).WithMany(p => p.DoctorServices)
                .HasForeignKey(d => d.DoctorAccountId)
                .HasConstraintName("fk_doctor_services_doctor");
        });

        modelBuilder.Entity<DoctorSpecialty>(entity =>
        {
            entity.HasKey(e => new { e.DoctorAccountId, e.SpecialtyId }).HasName("PRIMARY");

            entity.ToTable("doctor_specialties");

            entity.HasIndex(e => e.SpecialtyId, "ix_doctor_specialties_specialty");

            entity.Property(e => e.DoctorAccountId).HasColumnName("doctor_account_id");
            entity.Property(e => e.SpecialtyId).HasColumnName("specialty_id");
            entity.Property(e => e.IsPrimary).HasColumnName("is_primary");

            entity.HasOne(d => d.DoctorAccount).WithMany(p => p.DoctorSpecialties)
                .HasForeignKey(d => d.DoctorAccountId)
                .HasConstraintName("fk_doctor_specialties_doctor");

            entity.HasOne(d => d.Specialty).WithMany(p => p.DoctorSpecialties)
                .HasForeignKey(d => d.SpecialtyId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_doctor_specialties_specialty");
        });

        modelBuilder.Entity<DoctorWeeklyHour>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("doctor_weekly_hours");

            entity.HasIndex(e => new { e.DoctorAccountId, e.DayOfWeek, e.ModalityCode, e.StartTime, e.EndTime, e.ValidFrom }, "ux_doctor_weekly_hours").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.DayOfWeek).HasColumnName("day_of_week");
            entity.Property(e => e.DoctorAccountId).HasColumnName("doctor_account_id");
            entity.Property(e => e.EndTime)
                .HasColumnType("time")
                .HasColumnName("end_time");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.ModalityCode)
                .HasMaxLength(10)
                .HasDefaultValueSql("'both'")
                .HasColumnName("modality_code");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.StartTime)
                .HasColumnType("time")
                .HasColumnName("start_time");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");
            entity.Property(e => e.ValidFrom)
                .HasColumnType("date")
                .HasColumnName("valid_from");
            entity.Property(e => e.ValidUntil)
                .HasColumnType("date")
                .HasColumnName("valid_until");

            entity.HasOne(d => d.DoctorAccount).WithMany(p => p.DoctorWeeklyHours)
                .HasForeignKey(d => d.DoctorAccountId)
                .HasConstraintName("fk_doctor_weekly_hours_doctor");
        });

        modelBuilder.Entity<ExternalLogin>(entity =>
        {
            entity.HasKey(e => new { e.ProviderCode, e.ProviderSubject }).HasName("PRIMARY");

            entity.ToTable("external_logins");

            entity.HasIndex(e => new { e.AccountId, e.ProviderCode }, "ux_external_logins_account_provider").IsUnique();

            entity.Property(e => e.ProviderCode)
                .HasMaxLength(32)
                .HasColumnName("provider_code");
            entity.Property(e => e.ProviderSubject).HasColumnName("provider_subject");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.LastUsedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("last_used_at_utc");
            entity.Property(e => e.LinkedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("linked_at_utc");
            entity.Property(e => e.ProviderEmail)
                .HasMaxLength(254)
                .HasColumnName("provider_email");
            entity.Property(e => e.ProviderEmailVerified).HasColumnName("provider_email_verified");

            entity.HasOne(d => d.Account).WithMany(p => p.ExternalLogins)
                .HasForeignKey(d => d.AccountId)
                .HasConstraintName("fk_external_logins_account");
        });

        modelBuilder.Entity<Holiday>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("holidays");

            entity.HasIndex(e => new { e.HolidayDate, e.Name }, "ux_holidays_date_name").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.EndTime)
                .HasColumnType("time")
                .HasColumnName("end_time");
            entity.Property(e => e.HolidayDate)
                .HasColumnType("date")
                .HasColumnName("holiday_date");
            entity.Property(e => e.IsAnnual).HasColumnName("is_annual");
            entity.Property(e => e.Name)
                .HasMaxLength(120)
                .HasColumnName("name");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.StartTime)
                .HasColumnType("time")
                .HasColumnName("start_time");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(e => new { e.ScopeCode, e.IdempotencyKey }).HasName("PRIMARY");

            entity.ToTable("idempotency_records");

            entity.HasIndex(e => e.ExpiresAtUtc, "ix_idempotency_records_expiry");

            entity.Property(e => e.ScopeCode)
                .HasMaxLength(50)
                .HasColumnName("scope_code");
            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(100)
                .HasColumnName("idempotency_key");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.ExpiresAtUtc)
                .HasMaxLength(6)
                .HasColumnName("expires_at_utc");
            entity.Property(e => e.RequestHash)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("request_hash");
            entity.Property(e => e.ResponseBodyJson)
                .HasColumnType("json")
                .HasColumnName("response_body_json");
            entity.Property(e => e.ResponseStatusCode).HasColumnName("response_status_code");
        });

        modelBuilder.Entity<ManagerPreference>(entity =>
        {
            entity.HasKey(e => e.ManagerAccountId).HasName("PRIMARY");

            entity.ToTable("manager_preferences");

            entity.Property(e => e.ManagerAccountId).HasColumnName("manager_account_id");
            entity.Property(e => e.EmailEnabled).HasColumnName("email_enabled");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.SmsEnabled).HasColumnName("sms_enabled");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.ManagerAccount).WithOne(p => p.ManagerPreference)
                .HasForeignKey<ManagerPreference>(d => d.ManagerAccountId)
                .HasConstraintName("fk_manager_preferences_account");
        });

        modelBuilder.Entity<MedicalReport>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("medical_reports");

            entity.HasIndex(e => new { e.AuthorDoctorAccountId, e.UpdatedAtUtc }, "ix_medical_reports_author_updated");

            entity.HasIndex(e => new { e.StatusCode, e.PublishedAtUtc }, "ix_medical_reports_status_published");

            entity.HasIndex(e => e.AppointmentId, "ux_medical_reports_appointment").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppointmentId).HasColumnName("appointment_id");
            entity.Property(e => e.AuthorDoctorAccountId).HasColumnName("author_doctor_account_id");
            entity.Property(e => e.ClinicalSummary)
                .HasColumnType("text")
                .HasColumnName("clinical_summary");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.PublishedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("published_at_utc");
            entity.Property(e => e.Recommendations)
                .HasColumnType("text")
                .HasColumnName("recommendations");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.StatusCode)
                .HasMaxLength(20)
                .HasDefaultValueSql("'draft'")
                .HasColumnName("status_code");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.Appointment).WithOne(p => p.MedicalReport)
                .HasForeignKey<MedicalReport>(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_medical_reports_appointment");

            entity.HasOne(d => d.AuthorDoctorAccount).WithMany(p => p.MedicalReports)
                .HasForeignKey(d => d.AuthorDoctorAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_medical_reports_author");
        });

        modelBuilder.Entity<MedicalReportVersion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("medical_report_versions");

            entity.HasIndex(e => new { e.AuthorDoctorAccountId, e.CreatedAtUtc }, "ix_medical_report_versions_author");

            entity.HasIndex(e => new { e.MedicalReportId, e.VersionNumber }, "ux_medical_report_versions_number").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AuthorDoctorAccountId).HasColumnName("author_doctor_account_id");
            entity.Property(e => e.ChangeReason)
                .HasMaxLength(1000)
                .HasColumnName("change_reason");
            entity.Property(e => e.ClinicalSummary)
                .HasColumnType("text")
                .HasColumnName("clinical_summary");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.MedicalReportId).HasColumnName("medical_report_id");
            entity.Property(e => e.Recommendations)
                .HasColumnType("text")
                .HasColumnName("recommendations");
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");

            entity.HasOne(d => d.AuthorDoctorAccount).WithMany(p => p.MedicalReportVersions)
                .HasForeignKey(d => d.AuthorDoctorAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_medical_report_versions_author");

            entity.HasOne(d => d.MedicalReport).WithMany(p => p.MedicalReportVersions)
                .HasForeignKey(d => d.MedicalReportId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_medical_report_versions_report");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("outbox_messages");

            entity.HasIndex(e => new { e.StatusCode, e.NextAttemptAtUtc, e.LeaseUntilUtc }, "ix_outbox_messages_claim");

            entity.HasIndex(e => e.IdempotencyKey, "ux_outbox_messages_idempotency").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
            entity.Property(e => e.ChannelCode)
                .HasMaxLength(10)
                .HasColumnName("channel_code");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key");
            entity.Property(e => e.LastErrorCode)
                .HasMaxLength(100)
                .HasColumnName("last_error_code");
            entity.Property(e => e.LeaseOwner)
                .HasMaxLength(100)
                .HasColumnName("lease_owner");
            entity.Property(e => e.LeaseUntilUtc)
                .HasMaxLength(6)
                .HasColumnName("lease_until_utc");
            entity.Property(e => e.MaxAttempts)
                .HasDefaultValueSql("'5'")
                .HasColumnName("max_attempts");
            entity.Property(e => e.NextAttemptAtUtc)
                .HasMaxLength(6)
                .HasColumnName("next_attempt_at_utc");
            entity.Property(e => e.PayloadJson)
                .HasColumnType("json")
                .HasColumnName("payload_json");
            entity.Property(e => e.Recipient)
                .HasMaxLength(254)
                .HasColumnName("recipient");
            entity.Property(e => e.SentAtUtc)
                .HasMaxLength(6)
                .HasColumnName("sent_at_utc");
            entity.Property(e => e.StatusCode)
                .HasMaxLength(20)
                .HasDefaultValueSql("'pending'")
                .HasColumnName("status_code");
            entity.Property(e => e.TemplateKey)
                .HasMaxLength(100)
                .HasColumnName("template_key");
        });

        modelBuilder.Entity<PatientPreference>(entity =>
        {
            entity.HasKey(e => e.AccountId).HasName("PRIMARY");

            entity.ToTable("patient_preferences");

            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.EmailEnabled).HasColumnName("email_enabled");
            entity.Property(e => e.SmsEnabled).HasColumnName("sms_enabled");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.Account).WithOne(p => p.PatientPreference)
                .HasForeignKey<PatientPreference>(d => d.AccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_patient_preferences_account");
        });

        modelBuilder.Entity<PatientProfile>(entity =>
        {
            entity.HasKey(e => e.AccountId).HasName("PRIMARY");

            entity.ToTable("patient_profiles");

            entity.HasIndex(e => e.TaxId, "ux_patient_profiles_tax_id").IsUnique();

            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.BirthDate)
                .HasColumnType("date")
                .HasColumnName("birth_date");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.PreferredName)
                .HasMaxLength(120)
                .HasColumnName("preferred_name");
            entity.Property(e => e.TaxId)
                .HasMaxLength(11)
                .IsFixedLength()
                .HasColumnName("tax_id");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.Account).WithOne(p => p.PatientProfile)
                .HasForeignKey<PatientProfile>(d => d.AccountId)
                .HasConstraintName("fk_patient_profiles_account");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("payments");

            entity.HasIndex(e => new { e.ConfirmedByAccountId, e.PaidAtUtc }, "ix_payments_confirmer");

            entity.HasIndex(e => e.ProviderReferenceAppointmentId, "ix_payments_provider_reference");

            entity.HasIndex(e => new { e.StatusCode, e.NextReconciliationAtUtc }, "ix_payments_reconciliation");

            entity.HasIndex(e => new { e.StatusCode, e.CreatedAtUtc }, "ix_payments_status_created");

            entity.HasIndex(e => e.AppointmentId, "ux_payments_appointment").IsUnique();

            entity.HasIndex(e => e.IdempotencyKey, "ux_payments_idempotency_key").IsUnique();

            entity.HasIndex(e => new { e.ProviderCode, e.ProviderCheckoutId }, "ux_payments_provider_checkout").IsUnique();

            entity.HasIndex(e => new { e.ProviderCode, e.ProviderTransactionId }, "ux_payments_provider_transaction").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(13)
                .HasColumnName("amount");
            entity.Property(e => e.AppointmentId).HasColumnName("appointment_id");
            entity.Property(e => e.AuthorizationReference)
                .HasMaxLength(100)
                .HasColumnName("authorization_reference");
            entity.Property(e => e.CanceledAtUtc)
                .HasMaxLength(6)
                .HasColumnName("canceled_at_utc");
            entity.Property(e => e.CardLastFour)
                .HasMaxLength(4)
                .IsFixedLength()
                .HasColumnName("card_last_four");
            entity.Property(e => e.CheckoutExpiresAtUtc)
                .HasMaxLength(6)
                .HasColumnName("checkout_expires_at_utc");
            entity.Property(e => e.CheckoutUrl)
                .HasMaxLength(500)
                .HasColumnName("checkout_url");
            entity.Property(e => e.ConfirmedByAccountId).HasColumnName("confirmed_by_account_id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.CurrencyCode)
                .HasMaxLength(3)
                .HasDefaultValueSql("'BRL'")
                .IsFixedLength()
                .HasColumnName("currency_code");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key");
            entity.Property(e => e.LastReconciledAtUtc)
                .HasMaxLength(6)
                .HasColumnName("last_reconciled_at_utc");
            entity.Property(e => e.MethodCode)
                .HasMaxLength(30)
                .HasColumnName("method_code");
            entity.Property(e => e.NextReconciliationAtUtc)
                .HasMaxLength(6)
                .HasColumnName("next_reconciliation_at_utc");
            entity.Property(e => e.PaidAtUtc)
                .HasMaxLength(6)
                .HasColumnName("paid_at_utc");
            entity.Property(e => e.ProviderCheckoutId)
                .HasMaxLength(100)
                .HasColumnName("provider_checkout_id");
            entity.Property(e => e.ProviderCode)
                .HasMaxLength(20)
                .HasColumnName("provider_code");
            entity.Property(e => e.ProviderEventAtUtc)
                .HasMaxLength(6)
                .HasColumnName("provider_event_at_utc");
            entity.Property(e => e.ProviderReferenceAppointmentId).HasColumnName("provider_reference_appointment_id");
            entity.Property(e => e.ProviderStatusCode)
                .HasMaxLength(50)
                .HasColumnName("provider_status_code");
            entity.Property(e => e.ProviderTransactionId)
                .HasMaxLength(100)
                .HasColumnName("provider_transaction_id");
            entity.Property(e => e.ReconciliationAttemptCount).HasColumnName("reconciliation_attempt_count");
            entity.Property(e => e.RefundAmount)
                .HasPrecision(13)
                .HasColumnName("refund_amount");
            entity.Property(e => e.RefundedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("refunded_at_utc");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.StatusCode)
                .HasMaxLength(20)
                .HasDefaultValueSql("'pending'")
                .HasColumnName("status_code");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.Appointment).WithOne(p => p.PaymentAppointment)
                .HasForeignKey<Payment>(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_payments_appointment");

            entity.HasOne(d => d.ConfirmedByAccount).WithMany(p => p.Payments)
                .HasForeignKey(d => d.ConfirmedByAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_payments_confirmer");

            entity.HasOne(d => d.ProviderReferenceAppointment).WithMany(p => p.PaymentProviderReferenceAppointments)
                .HasForeignKey(d => d.ProviderReferenceAppointmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_payments_provider_reference");
        });

        modelBuilder.Entity<PaymentEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("payment_events");

            entity.HasIndex(e => new { e.PaymentId, e.OccurredAtUtc }, "ix_payment_events_payment_time");

            entity.HasIndex(e => new { e.PaymentId, e.EventFingerprint }, "ux_payment_events_fingerprint").IsUnique();

            entity.HasIndex(e => e.WebhookReceiptId, "ux_payment_events_webhook_receipt").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EventFingerprint)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("event_fingerprint");
            entity.Property(e => e.IgnoredReasonCode)
                .HasMaxLength(100)
                .HasColumnName("ignored_reason_code");
            entity.Property(e => e.NormalizedStatusCode)
                .HasMaxLength(20)
                .HasColumnName("normalized_status_code");
            entity.Property(e => e.OccurredAtUtc)
                .HasMaxLength(6)
                .HasColumnName("occurred_at_utc");
            entity.Property(e => e.PaymentId).HasColumnName("payment_id");
            entity.Property(e => e.ProviderOccurredAtUtc)
                .HasMaxLength(6)
                .HasColumnName("provider_occurred_at_utc");
            entity.Property(e => e.ProviderResourceId)
                .HasMaxLength(100)
                .HasColumnName("provider_resource_id");
            entity.Property(e => e.ProviderStatusCode)
                .HasMaxLength(50)
                .HasColumnName("provider_status_code");
            entity.Property(e => e.SourceCode)
                .HasMaxLength(20)
                .HasColumnName("source_code");
            entity.Property(e => e.WasApplied).HasColumnName("was_applied");
            entity.Property(e => e.WebhookReceiptId).HasColumnName("webhook_receipt_id");

            entity.HasOne(d => d.Payment).WithMany(p => p.PaymentEvents)
                .HasForeignKey(d => d.PaymentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_payment_events_payment");

            entity.HasOne(d => d.WebhookReceipt).WithOne(p => p.PaymentEvent)
                .HasForeignKey<PaymentEvent>(d => d.WebhookReceiptId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_payment_events_webhook_receipt");
        });

        modelBuilder.Entity<PaymentWebhookReceipt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("payment_webhook_receipts");

            entity.HasIndex(e => new { e.ProcessingStatusCode, e.ReceivedAtUtc }, "ix_payment_webhook_receipts_status_time");

            entity.HasIndex(e => new { e.ProviderCode, e.PayloadSha256 }, "ux_payment_webhook_receipts_payload").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AuthenticitySha256)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("authenticity_sha256");
            entity.Property(e => e.PayloadSha256)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("payload_sha256");
            entity.Property(e => e.ProcessedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("processed_at_utc");
            entity.Property(e => e.ProcessingStatusCode)
                .HasMaxLength(20)
                .HasDefaultValueSql("'received'")
                .HasColumnName("processing_status_code");
            entity.Property(e => e.ProviderCode)
                .HasMaxLength(20)
                .HasColumnName("provider_code");
            entity.Property(e => e.ProviderResourceId)
                .HasMaxLength(100)
                .HasColumnName("provider_resource_id");
            entity.Property(e => e.ReceivedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("received_at_utc");
            entity.Property(e => e.ResultCode)
                .HasMaxLength(100)
                .HasColumnName("result_code");
        });

        modelBuilder.Entity<PremiumMembership>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("premium_memberships");

            entity.HasIndex(e => e.PremiumPlanId, "fk_premium_memberships_plan");

            entity.HasIndex(e => new { e.ProofDocumentId, e.AccountId }, "fk_premium_memberships_proof_owner");

            entity.HasIndex(e => new { e.AccountId, e.StatusCode, e.EndsAtUtc }, "ix_premium_memberships_account_status");

            entity.HasIndex(e => new { e.ReviewedByAccountId, e.ReviewedAtUtc }, "ix_premium_memberships_reviewer");

            entity.HasIndex(e => e.OpenAccountId, "ux_premium_memberships_open").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.EndsAtUtc)
                .HasMaxLength(6)
                .HasColumnName("ends_at_utc");
            entity.Property(e => e.OpenAccountId).HasColumnName("open_account_id");
            entity.Property(e => e.PremiumPlanId).HasColumnName("premium_plan_id");
            entity.Property(e => e.ProofDocumentId).HasColumnName("proof_document_id");
            entity.Property(e => e.RejectionReason)
                .HasMaxLength(1000)
                .HasColumnName("rejection_reason");
            entity.Property(e => e.ReviewNotes)
                .HasMaxLength(1000)
                .HasColumnName("review_notes");
            entity.Property(e => e.ReviewedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("reviewed_at_utc");
            entity.Property(e => e.ReviewedByAccountId).HasColumnName("reviewed_by_account_id");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.StartsAtUtc)
                .HasMaxLength(6)
                .HasColumnName("starts_at_utc");
            entity.Property(e => e.StatusCode)
                .HasMaxLength(20)
                .HasDefaultValueSql("'pending'")
                .HasColumnName("status_code");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");

            entity.HasOne(d => d.Account).WithMany(p => p.PremiumMembershipAccounts)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_premium_memberships_account");

            entity.HasOne(d => d.PremiumPlan).WithMany(p => p.PremiumMemberships)
                .HasForeignKey(d => d.PremiumPlanId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_premium_memberships_plan");

            entity.HasOne(d => d.ProofDocument).WithMany(p => p.PremiumMembershipProofDocuments)
                .HasForeignKey(d => d.ProofDocumentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_premium_memberships_proof");

            entity.HasOne(d => d.ReviewedByAccount).WithMany(p => p.PremiumMembershipReviewedByAccounts)
                .HasForeignKey(d => d.ReviewedByAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_premium_memberships_reviewer");

            entity.HasOne(d => d.PrivateDocument).WithMany(p => p.PremiumMembershipPrivateDocuments)
                .HasPrincipalKey(p => new { p.Id, p.OwnerAccountId })
                .HasForeignKey(d => new { d.ProofDocumentId, d.AccountId })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_premium_memberships_proof_owner");
        });

        modelBuilder.Entity<PremiumPlan>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("premium_plans");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppointmentDiscountPercent)
                .HasPrecision(5)
                .HasColumnName("appointment_discount_percent");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(120)
                .HasColumnName("name");
            entity.Property(e => e.PriceAmount)
                .HasPrecision(13)
                .HasColumnName("price_amount");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");
            entity.Property(e => e.ValidityDays).HasColumnName("validity_days");
        });

        modelBuilder.Entity<PrivateDocument>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("private_documents");

            entity.HasIndex(e => e.OwnerAccountId, "fk_private_documents_owner");

            entity.HasIndex(e => new { e.Id, e.OwnerAccountId }, "ux_private_documents_owner").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ContentType)
                .HasMaxLength(127)
                .HasColumnName("content_type");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.OriginalFileName)
                .HasMaxLength(255)
                .HasColumnName("original_file_name");
            entity.Property(e => e.OwnerAccountId).HasColumnName("owner_account_id");
            entity.Property(e => e.ProtectedContent)
                .HasColumnType("mediumblob")
                .HasColumnName("protected_content");
            entity.Property(e => e.Sha256)
                .HasMaxLength(32)
                .IsFixedLength()
                .HasColumnName("sha256");
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entity.Property(e => e.StatusCode)
                .HasMaxLength(20)
                .HasColumnName("status_code");

            entity.HasOne(d => d.OwnerAccount).WithMany(p => p.PrivateDocuments)
                .HasForeignKey(d => d.OwnerAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_private_documents_owner");
        });

        modelBuilder.Entity<ProfessionalReview>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("professional_reviews");

            entity.HasIndex(e => new { e.ProfessionalAccountId, e.OccurredAtUtc }, "ix_professional_reviews_professional_time");

            entity.HasIndex(e => new { e.ReviewerAccountId, e.OccurredAtUtc }, "ix_professional_reviews_reviewer_time");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DecisionCode)
                .HasMaxLength(20)
                .HasColumnName("decision_code");
            entity.Property(e => e.OccurredAtUtc)
                .HasMaxLength(6)
                .HasColumnName("occurred_at_utc");
            entity.Property(e => e.ProfessionalAccountId).HasColumnName("professional_account_id");
            entity.Property(e => e.Reason)
                .HasMaxLength(500)
                .HasColumnName("reason");
            entity.Property(e => e.ReviewerAccountId).HasColumnName("reviewer_account_id");

            entity.HasOne(d => d.ProfessionalAccount).WithMany(p => p.ProfessionalReviewProfessionalAccounts)
                .HasForeignKey(d => d.ProfessionalAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_professional_reviews_professional");

            entity.HasOne(d => d.ReviewerAccount).WithMany(p => p.ProfessionalReviewReviewerAccounts)
                .HasForeignKey(d => d.ReviewerAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_professional_reviews_reviewer");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("PRIMARY");

            entity.ToTable("roles");

            entity.Property(e => e.Code)
                .HasMaxLength(20)
                .HasColumnName("code");
            entity.Property(e => e.DisplayName)
                .HasMaxLength(50)
                .HasColumnName("display_name");
            entity.Property(e => e.IsPrivileged).HasColumnName("is_privileged");
        });

        modelBuilder.Entity<Specialty>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("specialties");

            entity.HasIndex(e => e.NormalizedName, "ux_specialties_normalized_name").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("created_at_utc");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(120)
                .HasColumnName("name");
            entity.Property(e => e.NormalizedName)
                .HasMaxLength(120)
                .HasColumnName("normalized_name");
            entity.Property(e => e.RowVersion)
                .HasDefaultValueSql("'1'")
                .HasColumnName("row_version");
            entity.Property(e => e.UpdatedAtUtc)
                .HasMaxLength(6)
                .HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<TeleconsultationPeer>(entity =>
        {
            entity.HasKey(e => new { e.AppointmentId, e.AccountId }).HasName("PRIMARY");

            entity.ToTable("teleconsultation_peers");

            entity.HasIndex(e => e.AccountId, "fk_teleconsultation_account");

            entity.Property(e => e.AppointmentId).HasColumnName("appointment_id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.ConnectionId)
                .HasMaxLength(128)
                .HasColumnName("connection_id");
            entity.Property(e => e.ExpiresAtUtc)
                .HasMaxLength(6)
                .HasColumnName("expires_at_utc");

            entity.HasOne(d => d.Account).WithMany(p => p.TeleconsultationPeers)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_teleconsultation_account");

            entity.HasOne(d => d.Appointment).WithMany(p => p.TeleconsultationPeers)
                .HasForeignKey(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_teleconsultation_appointment");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
