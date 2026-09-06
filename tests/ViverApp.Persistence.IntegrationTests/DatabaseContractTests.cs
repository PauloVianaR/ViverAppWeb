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
        Assert.Equal(38, applicationEntities.Length);
        Assert.DoesNotContain(
            applicationEntities,
            entity => string.Equals(entity.GetTableName(), "__schema_migrations", StringComparison.Ordinal));

        var account = context.Model.FindEntityType(typeof(Account));
        Assert.NotNull(account);
        Assert.False(account.FindProperty(nameof(Account.RoleCode))!.IsNullable);
        Assert.True(account.FindProperty(nameof(Account.RowVersion))!.IsConcurrencyToken);
        var medicalReport = context.Model.FindEntityType(typeof(MedicalReport));
        Assert.NotNull(medicalReport);
        Assert.True(medicalReport.FindProperty(nameof(MedicalReport.RowVersion))!.IsConcurrencyToken);
        var payment = context.Model.FindEntityType(typeof(Payment));
        Assert.NotNull(payment);
        Assert.True(payment.FindProperty(nameof(Payment.RowVersion))!.IsConcurrencyToken);
        var statusHistory = context.Model.FindEntityType(typeof(AppointmentStatusHistory));
        Assert.NotNull(statusHistory);
        Assert.True(statusHistory.FindProperty(nameof(AppointmentStatusHistory.ActorAccountId))!.IsNullable);

        var expectedRoles = new[] { "administrator", "doctor", "manager", "patient" };
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
        Assert.Equal("0001,0002,0003,0004,0005,0006,0007,0008,0009,0010,0011,0012,0013,0014", migrations);

        var recognizedSpecialties = await ExecuteScalarAsync(
            connection,
            "SELECT COUNT(*) FROM specialties WHERE is_active = 1");
        Assert.Equal("55", recognizedSpecialties);

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
                OR (table_name = 'doctor_profiles' AND column_name IN ('professional_title', 'years_experience')))
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
        Assert.Equal("7", schedulingSettings);

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
