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
        Assert.Equal(24, applicationEntities.Length);
        Assert.DoesNotContain(
            applicationEntities,
            entity => string.Equals(entity.GetTableName(), "__schema_migrations", StringComparison.Ordinal));

        var account = context.Model.FindEntityType(typeof(Account));
        Assert.NotNull(account);
        Assert.False(account.FindProperty(nameof(Account.RoleCode))!.IsNullable);
        Assert.True(account.FindProperty(nameof(Account.RowVersion))!.IsConcurrencyToken);

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
        Assert.Equal("0001,0002,0003,0004", migrations);

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
