using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.Persistence.IntegrationTests;

public sealed class DatabaseContractTests
{
    private const string ExpectedDatabase = "viverappweb";
    private const string ExpectedVersion = "8.0.41";

    [Fact]
    public async Task Connects_only_to_expected_mysql_database()
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync();

        var database = await ExecuteScalarAsync(context.Database.GetDbConnection(), "SELECT DATABASE()");
        var version = await ExecuteScalarAsync(context.Database.GetDbConnection(), "SELECT VERSION()");

        Assert.Equal(ExpectedDatabase, database);
        Assert.True(IsExpectedServerVersion(version), "O servidor conectado não é MySQL 8.0.41.");
    }

    [Fact]
    public async Task Scaffold_matches_single_role_and_single_clinic_contract()
    {
        await using var context = CreateContext();

        var applicationEntities = context.Model.GetEntityTypes().ToArray();
        Assert.Equal(66, applicationEntities.Length);
        Assert.DoesNotContain(
            applicationEntities,
            entity => string.Equals(entity.GetTableName(), "__schema_migrations", StringComparison.Ordinal));

        var account = context.Model.FindEntityType(typeof(Account));
        Assert.NotNull(account);
        Assert.False(account.FindProperty(nameof(Account.RoleCode))!.IsNullable);
        Assert.False(account.FindProperty(nameof(Account.PortalAccessEnabled))!.IsNullable);
        Assert.True(account.FindProperty(nameof(Account.RowVersion))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(ProfessionalPreference))!
            .FindProperty(nameof(ProfessionalPreference.RowVersion))!.IsConcurrencyToken);
        Assert.NotNull(context.Model.FindEntityType(typeof(ProfessionalVariableHour)));
        var medicalReport = context.Model.FindEntityType(typeof(MedicalReport));
        Assert.NotNull(medicalReport);
        Assert.True(medicalReport.FindProperty(nameof(MedicalReport.RowVersion))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(ElectronicHealthRecord))!.FindProperty(nameof(ElectronicHealthRecord.RowVersion))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(MedicalRecordDraft))!.FindProperty(nameof(MedicalRecordDraft.RowVersion))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(MedicalRecordDocument))!.FindProperty(nameof(MedicalRecordDocument.RowVersion))!.IsConcurrencyToken);
        Assert.NotNull(context.Model.FindEntityType(typeof(MedicalRecordEntry)));
        Assert.NotNull(context.Model.FindEntityType(typeof(MedicalRecordVersion)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ClinicalAccessEvent)));
        var payment = context.Model.FindEntityType(typeof(Payment));
        Assert.NotNull(payment);
        Assert.True(payment.FindProperty(nameof(Payment.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate,
            payment.FindProperty(nameof(Payment.ActiveAppointmentId))!.ValueGenerated);
        Assert.True(context.Model.FindEntityType(typeof(PaymentReversal))!.FindProperty(nameof(PaymentReversal.RowVersion))!.IsConcurrencyToken);
        Assert.NotNull(context.Model.FindEntityType(typeof(CashMovement)));
        Assert.NotNull(context.Model.FindEntityType(typeof(CashClosure)));
        Assert.NotNull(context.Model.FindEntityType(typeof(PaymentReversalEvent)));
        var statusHistory = context.Model.FindEntityType(typeof(AppointmentStatusHistory));
        Assert.NotNull(statusHistory);
        Assert.True(statusHistory.FindProperty(nameof(AppointmentStatusHistory.ActorAccountId))!.IsNullable);
        var administratorNotification = context.Model.FindEntityType(typeof(AdministratorNotification));
        Assert.NotNull(administratorNotification);
        Assert.True(administratorNotification.FindProperty(nameof(AdministratorNotification.RowVersion))!.IsConcurrencyToken);
        var doctorNotification = context.Model.FindEntityType(typeof(ProfessionalNotification));
        Assert.NotNull(doctorNotification);
        Assert.True(doctorNotification.FindProperty(nameof(ProfessionalNotification.RowVersion))!.IsConcurrencyToken);
        Assert.NotNull(context.Model.FindEntityType(typeof(AppointmentNumberSequence)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ArrivalQueueSequence)));
        Assert.NotNull(context.Model.FindEntityType(typeof(AppointmentRescheduleHistory)));
        Assert.True(context.Model.FindEntityType(typeof(AccountUiPreference))!.FindProperty(nameof(AccountUiPreference.RowVersion))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(ApplicationSetting))!.FindProperty(nameof(ApplicationSetting.RowVersion))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(PremiumPlan))!.FindProperty(nameof(PremiumPlan.RowVersion))!.IsConcurrencyToken);
        Assert.NotNull(context.Model.FindEntityType(typeof(Holiday))!.FindProperty(nameof(Holiday.IsAnnual)));
        Assert.NotNull(context.Model.FindEntityType(typeof(NotificationPreference)));
        Assert.NotNull(context.Model.FindEntityType(typeof(NotificationSuppression)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ScheduledJob)));
        Assert.NotNull(context.Model.FindEntityType(typeof(TeleconsultationGuestLink)));
        var videoPeer = context.Model.FindEntityType(typeof(TeleconsultationPeer))!;
        Assert.True(videoPeer.FindProperty(nameof(TeleconsultationPeer.AccountId))!.IsNullable);
        Assert.True(videoPeer.FindProperty(nameof(TeleconsultationPeer.GuestId))!.IsNullable);

        var expectedRoles = new[] { "administrator", "doctor", "manager", "patient", "psychologist" };
        var roles = await context.Roles
            .AsNoTracking()
            .OrderBy(role => role.Code)
            .Select(role => role.Code)
            .ToArrayAsync();
        Assert.Equal(expectedRoles, roles);

        Assert.InRange(await context.Clinics.CountAsync(), 0, 1);
    }

    [Fact]
    public async Task All_sql_migrations_are_recorded_and_push_schema_is_absent()
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();

        var migrations = await ExecuteScalarAsync(
            connection,
            "SELECT GROUP_CONCAT(migration_id ORDER BY migration_id SEPARATOR ',') FROM __schema_migrations");
        Assert.Equal("0001,0002,0003,0004,0005,0006,0007,0008,0009,0010,0011,0012,0013,0014,0015,0016,0017,0018,0019,0020,0021,0022,0023,0024,0025,0026,0027,0028,0029,0030,0031,0032,0033,0034,0035,0036,0037,0038,0039,0040,0041,0042,0043,0044,0045,0046", migrations);

        var guestLinks = await ExecuteScalarAsync(connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='viverappweb' AND table_name='teleconsultation_guest_links'");
        Assert.Equal("1", guestLinks);

        var portalAccessColumn = await ExecuteScalarAsync(connection,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'viverappweb' AND table_name = 'accounts' AND column_name = 'portal_access_enabled' AND is_nullable = 'NO'");
        Assert.Equal("1", portalAccessColumn);

        var optionalAddressColumns = await ExecuteScalarAsync(connection,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'viverappweb' AND table_name = 'account_addresses' AND column_name IN ('postal_code','street','number','district','city','state_code') AND is_nullable = 'YES'");
        Assert.Equal("6", optionalAddressColumns);

        var operationalSettings = await ExecuteScalarAsync(connection,
            "SELECT GROUP_CONCAT(CONCAT(setting_key, '=', value_json) ORDER BY setting_key SEPARATOR ',') FROM application_settings WHERE setting_key IN ('cash.manager_can_reopen','cash.manager_can_view_cumulative_totals','professional.patient_scheduling_enabled','manager.medical_records_write_enabled','premium.manager_can_manage')");
        Assert.Equal("cash.manager_can_reopen=false,cash.manager_can_view_cumulative_totals=true,manager.medical_records_write_enabled=true,premium.manager_can_manage=true,professional.patient_scheduling_enabled=true", operationalSettings);

        var professionalDurationDefault = await ExecuteScalarAsync(connection,
            "SELECT column_default FROM information_schema.columns WHERE table_schema = 'viverappweb' AND table_name = 'professional_profiles' AND column_name = 'default_appointment_duration_minutes'");
        Assert.Equal("10", professionalDurationDefault);

        var clinicalAuthors = await ExecuteScalarAsync(connection,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'viverappweb' AND table_name IN ('medical_record_drafts','medical_record_entries','medical_record_versions') AND column_name = 'author_account_id' AND is_nullable = 'NO'");
        Assert.Equal("3", clinicalAuthors);

        var arrivalColumns = await ExecuteScalarAsync(connection,
            """
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = 'viverappweb' AND table_name = 'appointments'
              AND column_name IN ('appointment_number','arrived_at_utc','arrival_business_date','arrival_queue_number','arrival_recorded_by_account_id')
            """);
        Assert.Equal("5", arrivalColumns);

        var privateDocument = context.Model.FindEntityType(typeof(PrivateDocument))!;
        Assert.NotNull(privateDocument.FindProperty(nameof(PrivateDocument.StorageProviderCode)));
        Assert.NotNull(privateDocument.FindProperty(nameof(PrivateDocument.ObjectKey)));
        Assert.True(privateDocument.FindProperty(nameof(PrivateDocument.ProtectedContent))!.IsNullable);

        Assert.NotNull(context.Model.FindEntityType(typeof(ProfessionalService)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ProfessionalPreference)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ProfessionalAvailabilityException)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ProfessionalPatientLink)));
        Assert.NotNull(context.Model.FindEntityType(typeof(MedicalReportVersion)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ManagerPreference)));

        var recognizedSpecialties = await ExecuteScalarAsync(
            connection,
            "SELECT COUNT(*) FROM specialties WHERE is_active = 1");
        Assert.True(int.Parse(recognizedSpecialties, System.Globalization.CultureInfo.InvariantCulture) >= 55,
            "As especialidades oficiais devem permanecer disponíveis; registros locais adicionais são permitidos.");

        var ophthalmology = await ExecuteScalarAsync(
            connection,
            "SELECT COUNT(*) FROM specialties WHERE normalized_name = 'OFTALMOLOGIA' AND is_active = 1");
        Assert.Equal("1", ophthalmology);

        var accessFoundation = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'viverappweb'
              AND ((table_name = 'accounts' AND column_name IN ('tax_id', 'birth_date'))
                OR (table_name = 'professional_profiles' AND column_name IN ('professional_title', 'years_experience')))
            """);
        Assert.Equal("4", accessFoundation);

        var consentTable = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'viverappweb' AND table_name = 'account_consents'
            """);
        Assert.Equal("1", consentTable);

        var forbiddenColumns = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'viverappweb'
              AND (column_name LIKE '%firebase%' OR column_name LIKE '%push_token%')
            """);
        Assert.Equal("0", forbiddenColumns);

        var singletonConstraint = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE constraint_schema = 'viverappweb'
              AND table_name = 'clinic'
              AND constraint_name = 'ck_clinic_singleton'
              AND constraint_type = 'CHECK'
              AND enforced = 'YES'
            """);
        Assert.Equal("1", singletonConstraint);

        var roleConstraint = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE constraint_schema = 'viverappweb'
              AND table_name = 'roles'
              AND constraint_name = 'ck_roles_code'
              AND constraint_type = 'CHECK'
              AND enforced = 'YES'
            """);
        Assert.Equal("1", roleConstraint);

        var auditProtectionTriggers = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.triggers
            WHERE trigger_schema = 'viverappweb'
              AND trigger_name IN (
                  'trg_audit_events_block_update',
                  'trg_audit_events_block_delete')
            """);
        Assert.Equal("2", auditProtectionTriggers);

        var identityTables = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'viverappweb'
              AND table_name IN (
                  'account_authenticators',
                  'account_recovery_codes',
                  'account_passkeys')
            """);
        Assert.Equal("3", identityTables);

        var reversiblePasswordColumns = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'viverappweb'
              AND column_name IN ('password', 'encrypted_password', 'password_ciphertext')
            """);
        Assert.Equal("0", reversiblePasswordColumns);

        var roleColumns = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'viverappweb'
              AND table_name = 'accounts'
              AND column_name = 'role_code'
              AND is_nullable = 'NO'
            """);
        Assert.Equal("1", roleColumns);

        var phaseFiveTables = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'viverappweb'
              AND table_name = 'professional_reviews'
            """);
        Assert.Equal("1", phaseFiveTables);

        var clinicAddressColumns = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'viverappweb'
              AND table_name = 'clinic'
              AND column_name IN ('postal_code', 'street', 'number', 'district', 'city', 'state_code')
            """);
        Assert.Equal("6", clinicAddressColumns);

        var schedulingHistoryTable = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'viverappweb'
              AND table_name = 'appointment_status_history'
            """);
        Assert.Equal("1", schedulingHistoryTable);

        var schedulingColumns = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'viverappweb'
              AND table_name = 'appointments'
              AND column_name IN ('patient_notes', 'rescheduled_from_appointment_id')
            """);
        Assert.Equal("2", schedulingColumns);

        var schedulingSettings = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM application_settings
            WHERE setting_key LIKE 'appointments.%'
            """);
        Assert.Equal("22", schedulingSettings);

        var administratorSchema = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'viverappweb'
              AND ((table_name = 'administrator_notifications' AND column_name IN ('source_key', 'read_at_utc', 'dismissed_at_utc', 'row_version'))
                OR (table_name = 'application_settings' AND column_name = 'row_version')
                OR (table_name = 'premium_plans' AND column_name = 'row_version')
                OR (table_name = 'holidays' AND column_name = 'is_annual'))
            """);
        Assert.Equal("7", administratorSchema);

        var clinicalReportTable = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'viverappweb'
              AND table_name = 'medical_reports'
            """);
        Assert.Equal("1", clinicalReportTable);

        var clinicalLifecycleColumns = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'viverappweb'
              AND table_name = 'appointments'
              AND column_name IN (
                  'completed_by_account_id',
                  'completed_at_utc',
                  'no_show_recorded_by_account_id',
                  'no_show_recorded_at_utc')
            """);
        Assert.Equal("4", clinicalLifecycleColumns);

        var paymentTables = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'viverappweb'
              AND table_name IN ('payment_events', 'payment_webhook_receipts')
            """);
        Assert.Equal("2", paymentTables);

        var paymentOperationalColumns = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'viverappweb'
              AND table_name = 'payments'
              AND column_name IN (
                  'checkout_url',
                  'checkout_expires_at_utc',
                  'provider_event_at_utc',
                  'last_reconciled_at_utc',
                  'next_reconciliation_at_utc',
                  'reconciliation_attempt_count',
                  'refund_amount',
                  'refunded_at_utc')
            """);
        Assert.Equal("8", paymentOperationalColumns);

        var managerExperienceColumns = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'viverappweb'
              AND ((table_name = 'payments' AND column_name IN (
                    'confirmed_by_account_id', 'card_last_four', 'authorization_reference'))
                OR (table_name = 'premium_memberships' AND column_name IN (
                    'reviewed_by_account_id', 'review_notes')))
            """);
        Assert.Equal("5", managerExperienceColumns);

        var cashTables = await ExecuteScalarAsync(
            connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'viverappweb' AND table_name IN ('cash_movements','cash_closures','cash_reopenings','payment_reversals','payment_reversal_events')");
        Assert.Equal("5", cashTables);

        var appendOnlyTriggers = await ExecuteScalarAsync(
            connection,
            "SELECT COUNT(*) FROM information_schema.triggers WHERE trigger_schema = 'viverappweb' AND trigger_name IN ('trg_cash_movements_block_update','trg_cash_movements_block_delete','trg_cash_closures_block_update','trg_cash_closures_block_delete','trg_cash_reopenings_block_update','trg_cash_reopenings_block_delete','trg_payment_reversal_events_block_update','trg_payment_reversal_events_block_delete')");
        Assert.Equal("8", appendOnlyTriggers);

        var medicalRecordTables = await ExecuteScalarAsync(
            connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'viverappweb' AND table_name IN ('electronic_health_records','medical_record_drafts','medical_record_entries','medical_record_versions','medical_record_documents','clinical_access_events')");
        Assert.Equal("6", medicalRecordTables);

        var medicalRecordTriggers = await ExecuteScalarAsync(
            connection,
            "SELECT COUNT(*) FROM information_schema.triggers WHERE trigger_schema = 'viverappweb' AND trigger_name IN ('trg_medical_record_versions_block_update','trg_medical_record_versions_block_delete','trg_clinical_access_events_block_update','trg_clinical_access_events_block_delete')");
        Assert.Equal("4", medicalRecordTriggers);

        var requiredClinicalContentConstraint = await ExecuteScalarAsync(
            connection,
            "SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_schema = 'viverappweb' AND table_name = 'medical_record_versions' AND constraint_name = 'ck_medical_record_versions_content'");
        Assert.Equal("0", requiredClinicalContentConstraint);

        var managerPreferences = await ExecuteScalarAsync(
            connection,
            """
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'viverappweb' AND table_name = 'manager_preferences'
            """);
        Assert.Equal("1", managerPreferences);

        var accountUiPreferences = await ExecuteScalarAsync(
            connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'viverappweb' AND table_name = 'account_ui_preferences'");
        Assert.Equal("1", accountUiPreferences);
    }

    private static ViverAppDbContext CreateContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(DatabaseContractTests).Assembly, optional: false)
            .Build();
        var connectionString = configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("LocalConnection não configurada para os testes.");
        var connectionBuilder = new MySqlConnectionStringBuilder(connectionString);
        if (!string.Equals(connectionBuilder.Database, ExpectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Database de teste recusado; esperado {ExpectedDatabase}.");
        }

        var options = new DbContextOptionsBuilder<ViverAppDbContext>()
            .UseMySQL(connectionBuilder.ConnectionString)
            .Options;
        return new ViverAppDbContext(options);
    }

    private static bool IsExpectedServerVersion(string version)
    {
        return string.Equals(version, ExpectedVersion, StringComparison.Ordinal)
            || version.StartsWith($"{ExpectedVersion}-", StringComparison.Ordinal)
            || version.StartsWith($"{ExpectedVersion}+", StringComparison.Ordinal);
    }

    private static async Task<string> ExecuteScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
    }
}
