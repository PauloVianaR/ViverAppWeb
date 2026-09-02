using System.Text.RegularExpressions;
using Xunit;

namespace ViverApp.Persistence.IntegrationTests;

public sealed partial class MigrationScriptContractTests
{
    [Fact]
    public void Every_migration_has_a_matching_rollback_script()
    {
        var repositoryRoot = FindRepositoryRoot();
        var migrationIds = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "database", "migrations"), "*.sql")
            .Select(path => Path.GetFileName(path)[..4])
            .Order(StringComparer.Ordinal)
            .ToArray();
        var rollbackIds = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "database", "rollbacks"), "*.down.sql")
            .Select(path => Path.GetFileName(path)[..4])
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(migrationIds, rollbackIds);
    }

    [Fact]
    public void Baseline_rollback_covers_every_created_application_table()
    {
        var repositoryRoot = FindRepositoryRoot();
        var baseline = File.ReadAllText(
            Path.Combine(repositoryRoot, "database", "migrations", "0001__baseline.sql"));
        var rollback = File.ReadAllText(
            Path.Combine(repositoryRoot, "database", "rollbacks", "0001__baseline.down.sql"));

        var createdTables = CreateTable()
            .Matches(baseline)
            .Select(match => match.Groups["table"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var droppedTables = DropTable()
            .Matches(rollback)
            .Select(match => match.Groups["table"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(24, createdTables.Length);
        Assert.Equal(createdTables, droppedTables);
    }

    [Fact]
    public void Boolean_default_rollback_is_symmetric()
    {
        var repositoryRoot = FindRepositoryRoot();
        var migration = File.ReadAllText(
            Path.Combine(repositoryRoot, "database", "migrations", "0002__remove_ambiguous_boolean_defaults.sql"));
        var rollback = File.ReadAllText(
            Path.Combine(repositoryRoot, "database", "rollbacks", "0002__remove_ambiguous_boolean_defaults.down.sql"));

        var removedDefaults = DropDefault()
            .Matches(migration)
            .Select(ToColumnKey)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var restoredDefaults = SetDefault()
            .Matches(rollback)
            .Select(ToColumnKey)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(5, removedDefaults.Length);
        Assert.Equal(removedDefaults, restoredDefaults);
    }

    private static string ToColumnKey(Match match)
    {
        return $"{match.Groups["table"].Value}.{match.Groups["column"].Value}";
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ViverApp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("A raiz do repositório não foi localizada.");
    }

    [GeneratedRegex("CREATE TABLE `(?<table>[^`]+)`", RegexOptions.CultureInvariant)]
    private static partial Regex CreateTable();

    [GeneratedRegex("DROP TABLE IF EXISTS `(?<table>[^`]+)`", RegexOptions.CultureInvariant)]
    private static partial Regex DropTable();

    [GeneratedRegex(
        "ALTER TABLE `(?<table>[^`]+)` ALTER COLUMN `(?<column>[^`]+)` DROP DEFAULT",
        RegexOptions.CultureInvariant)]
    private static partial Regex DropDefault();

    [GeneratedRegex(
        "ALTER TABLE `(?<table>[^`]+)` ALTER COLUMN `(?<column>[^`]+)` SET DEFAULT",
        RegexOptions.CultureInvariant)]
    private static partial Regex SetDefault();
}
