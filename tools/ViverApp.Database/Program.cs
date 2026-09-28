using System.Data;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using MySql.Data.MySqlClient;

namespace ViverApp.Database;

internal static partial class Program
{
    private const string LegacyDatabase = "viverappmobile";
    private const string TargetDatabase = "viverappweb";
    private const string RequiredServerVersion = "8.0.41";
    private const string HistoryTable = "__schema_migrations";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            PrintUsage();
            return 2;
        }

        try
        {
            var connectionSource = LoadConnectionSource();
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            return args[0].ToLowerInvariant() switch
            {
                "inspect-legacy" => await InspectLegacyAsync(connectionSource, cancellationSource.Token),
                "snapshot-legacy-schema" => await SnapshotLegacySchemaAsync(connectionSource, cancellationSource.Token),
                "diagnose-configuration" => DiagnoseConfiguration(connectionSource),
                "bootstrap" => await BootstrapAsync(connectionSource, cancellationSource.Token),
                "configure-target" => await ConfigureTargetAsync(connectionSource, cancellationSource.Token),
                "status" => await ShowStatusAsync(connectionSource, cancellationSource.Token),
                "analyze-hot-paths" => await AnalyzeHotPathsAsync(connectionSource, cancellationSource.Token),
                "apply" => await ApplyAsync(connectionSource, cancellationSource.Token),
                "verify" => await VerifyAsync(connectionSource, cancellationSource.Token),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("A operação excedeu o limite seguro de dois minutos.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(DescribeException(exception));
            return 1;
        }
    }

    private static MySqlConnectionStringBuilder LoadConnectionSource()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: false)
            .Build();

        var connectionString = configuration.GetConnectionString("LocalConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:LocalConnection não está configurada em user-secrets.");
        }

        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            AllowBatch = true,
        };

        if (string.IsNullOrWhiteSpace(builder.Server) || string.IsNullOrWhiteSpace(builder.UserID))
        {
            throw new InvalidOperationException("LocalConnection não contém servidor e usuário válidos.");
        }

        if (IsLoopback(builder.Server) && builder.SslMode == MySqlSslMode.Preferred)
        {
            builder.SslMode = MySqlSslMode.Disabled;
        }

        return builder;
    }

    private static async Task<int> InspectLegacyAsync(
        MySqlConnectionStringBuilder source,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(source, LegacyDatabase);
        await connection.OpenAsync(cancellationToken);
        await AssertServerAsync(connection, cancellationToken);
        await AssertDatabaseAsync(connection, LegacyDatabase, cancellationToken);

        await using (var readOnlyCommand = connection.CreateCommand())
        {
            readOnlyCommand.CommandText = "SET TRANSACTION READ ONLY";
            await readOnlyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);

        var tables = await CountAsync(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = @schema AND table_type = 'BASE TABLE'
            """,
            cancellationToken);
        var columns = await CountAsync(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = @schema
            """,
            cancellationToken);
        var foreignKeys = await CountAsync(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM information_schema.referential_constraints
            WHERE constraint_schema = @schema
            """,
            cancellationToken);
        var views = await CountAsync(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM information_schema.views
            WHERE table_schema = @schema
            """,
            cancellationToken);
        var triggers = await CountAsync(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM information_schema.triggers
            WHERE trigger_schema = @schema
            """,
            cancellationToken);
        var routines = await CountAsync(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM information_schema.routines
            WHERE routine_schema = @schema
            """,
            cancellationToken);
        var events = await CountAsync(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM information_schema.events
            WHERE event_schema = @schema
            """,
            cancellationToken);

        var tableNames = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_type = 'BASE TABLE'
                ORDER BY table_name
                """;
            command.Parameters.AddWithValue("@schema", LegacyDatabase);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        await transaction.RollbackAsync(cancellationToken);

        Console.WriteLine($"MySQL: {RequiredServerVersion}");
        Console.WriteLine($"Banco legado confirmado em modo somente leitura: {LegacyDatabase}");
        Console.WriteLine($"Tabelas: {tables}; colunas: {columns}; chaves estrangeiras: {foreignKeys}");
        Console.WriteLine($"Views: {views}; triggers: {triggers}; rotinas: {routines}; eventos: {events}");
        Console.WriteLine("Tabelas: " + string.Join(", ", tableNames));
        return 0;
    }

    private static int DiagnoseConfiguration(MySqlConnectionStringBuilder source)
    {
        Console.WriteLine($"Database configurado: {source.Database}");
        Console.WriteLine($"Servidor presente: {!string.IsNullOrWhiteSpace(source.Server)}");
        Console.WriteLine($"Servidor é loopback: {IsLoopback(source.Server)}");
        Console.WriteLine($"Usuário presente: {!string.IsNullOrWhiteSpace(source.UserID)}");
        Console.WriteLine($"Porta: {source.Port}");
        Console.WriteLine($"SSL mode: {source.SslMode}");
        Console.WriteLine($"CertificateFile presente: {!string.IsNullOrWhiteSpace(source.CertificateFile)}");
        return 0;
    }

    private static async Task<int> SnapshotLegacySchemaAsync(
        MySqlConnectionStringBuilder source,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(source, LegacyDatabase);
        await connection.OpenAsync(cancellationToken);
        await AssertServerAsync(connection, cancellationToken);
        await AssertDatabaseAsync(connection, LegacyDatabase, cancellationToken);

        await using (var readOnlyCommand = connection.CreateCommand())
        {
            readOnlyCommand.CommandText = "SET TRANSACTION READ ONLY";
            await readOnlyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await AssertNoAdditionalSchemaObjectsAsync(connection, transaction, cancellationToken);
        var tableNames = await LoadLegacyTableNamesAsync(connection, transaction, cancellationToken);

        var script = new StringBuilder(
            """
            -- Snapshot estrutural somente leitura de viverappmobile.
            -- Restaure somente em servidor isolado, após selecionar um database vazio e descartável.
            -- Nunca execute este arquivo em viverappmobile ou viverappweb.
            -- Nenhum registro do legado está incluído.
            SET FOREIGN_KEY_CHECKS = 0;

            """.ReplaceLineEndings("\n"));

        foreach (var tableName in tableNames)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SHOW CREATE TABLE `{EscapeIdentifier(tableName)}`";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"SHOW CREATE TABLE não retornou {tableName}.");
            }

            var createSql = AutoIncrementOption().Replace(reader.GetString(1), string.Empty);
            script.AppendLine($"DROP TABLE IF EXISTS `{EscapeIdentifier(tableName)}`;");
            script.AppendLine(createSql + ";");
            script.AppendLine();
        }

        var eventNames = await LoadLegacyEventNamesAsync(connection, transaction, cancellationToken);
        foreach (var eventName in eventNames)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SHOW CREATE EVENT `{EscapeIdentifier(eventName)}`";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"SHOW CREATE EVENT não retornou {eventName}.");
            }

            var createSql = reader.GetString(reader.GetOrdinal("Create Event"));
            createSql = EventDefiner().Replace(createSql, string.Empty);
            createSql = LegacyQualifiedName().Replace(
                createSql,
                string.Empty);
            if (!EventStatus().IsMatch(createSql))
            {
                throw new InvalidOperationException(
                    $"O status do evento {eventName} não pôde ser normalizado com segurança.");
            }

            createSql = EventStatus().Replace(createSql, " DISABLE DO ", count: 1);
            script.AppendLine("DELIMITER $$");
            script.AppendLine($"DROP EVENT IF EXISTS `{EscapeIdentifier(eventName)}`$$");
            script.AppendLine(createSql + "$$");
            script.AppendLine("DELIMITER ;");
            script.AppendLine();
        }

        script.AppendLine("SET FOREIGN_KEY_CHECKS = 1;");
        await transaction.RollbackAsync(cancellationToken);

        var referenceDirectory = Path.Combine(FindRepositoryRoot(), "database", "reference");
        Directory.CreateDirectory(referenceDirectory);
        var outputPath = Path.Combine(referenceDirectory, "viverappmobile-schema.sql");
        var normalized = string.Join(
            "\n",
            script.ToString()
                .ReplaceLineEndings("\n")
                .Split('\n')
                .Select(line => line.TrimEnd()));
        File.WriteAllText(outputPath, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        Console.WriteLine(
            $"Snapshot estrutural criado com {tableNames.Count} tabelas e {eventNames.Count} eventos desabilitados; SHA-256: {checksum}");
        return 0;
    }

    private static async Task<int> BootstrapAsync(
        MySqlConnectionStringBuilder source,
        CancellationToken cancellationToken)
    {
        await using var serverConnection = CreateConnection(source, string.Empty);
        await serverConnection.OpenAsync(cancellationToken);
        await AssertServerAsync(serverConnection, cancellationToken);

        var alreadyExists = await DatabaseExistsAsync(serverConnection, TargetDatabase, cancellationToken);
        await using (var command = serverConnection.CreateCommand())
        {
            command.CommandText =
                $"CREATE DATABASE IF NOT EXISTS `{TargetDatabase}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var targetConnection = CreateConnection(source, TargetDatabase);
        await targetConnection.OpenAsync(cancellationToken);
        await AssertServerAsync(targetConnection, cancellationToken);
        await AssertDatabaseAsync(targetConnection, TargetDatabase, cancellationToken);

        Console.WriteLine(alreadyExists
            ? $"Banco {TargetDatabase} já existia e foi validado."
            : $"Banco {TargetDatabase} criado e validado.");
        return 0;
    }

    private static async Task<int> ConfigureTargetAsync(
        MySqlConnectionStringBuilder source,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(source, TargetDatabase);
        await connection.OpenAsync(cancellationToken);
        await AssertServerAsync(connection, cancellationToken);
        await AssertDatabaseAsync(connection, TargetDatabase, cancellationToken);

        var targetBuilder = new MySqlConnectionStringBuilder(source.ConnectionString)
        {
            Database = TargetDatabase,
        };
        WriteLocalConnectionSecret(targetBuilder.ConnectionString);

        Console.WriteLine($"LocalConnection agora aponta para {TargetDatabase}; credenciais não foram exibidas.");
        return 0;
    }

    private static async Task<int> ShowStatusAsync(
        MySqlConnectionStringBuilder source,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(source, TargetDatabase);
        await connection.OpenAsync(cancellationToken);
        await AssertServerAsync(connection, cancellationToken);
        await AssertDatabaseAsync(connection, TargetDatabase, cancellationToken);

        var migrations = LoadMigrations();
        var applied = await LoadAppliedMigrationsAsync(connection, cancellationToken);
        ValidateAppliedChecksums(migrations, applied);

        foreach (var migration in migrations)
        {
            Console.WriteLine($"{migration.Id}: {(applied.ContainsKey(migration.Id) ? "aplicada" : "pendente")}");
        }

        if (migrations.Count == 0)
        {
            Console.WriteLine("Nenhuma migration SQL encontrada.");
        }

        return 0;
    }

    private static async Task<int> ApplyAsync(
        MySqlConnectionStringBuilder source,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(source, TargetDatabase);
        await connection.OpenAsync(cancellationToken);
        await AssertServerAsync(connection, cancellationToken);
        await AssertDatabaseAsync(connection, TargetDatabase, cancellationToken);

        var migrations = LoadMigrations();
        if (migrations.Count == 0)
        {
            throw new InvalidOperationException(
                "Apply recusado: nenhuma migration SQL foi encontrada para revisão e aplicação.");
        }

        await EnsureHistoryTableAsync(connection, cancellationToken);
        var applied = await LoadAppliedMigrationsAsync(connection, cancellationToken);
        ValidateAppliedChecksums(migrations, applied);

        var pending = migrations.Where(migration => !applied.ContainsKey(migration.Id)).ToArray();
        foreach (var migration in pending)
        {
            Console.WriteLine($"Aplicando {migration.FileName} em {TargetDatabase}...");
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            command.CommandTimeout = 60;
            await command.ExecuteNonQueryAsync(cancellationToken);

            await using var historyCommand = connection.CreateCommand();
            historyCommand.CommandText = $"""
                INSERT INTO `{HistoryTable}` (`migration_id`, `description`, `sha256`, `applied_at_utc`)
                VALUES (@id, @description, @sha256, UTC_TIMESTAMP(6))
                """;
            historyCommand.Parameters.AddWithValue("@id", migration.Id);
            historyCommand.Parameters.AddWithValue("@description", migration.Description);
            historyCommand.Parameters.AddWithValue("@sha256", migration.Sha256);
            await historyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        Console.WriteLine(pending.Length == 0
            ? "Nenhuma migration pendente."
            : $"Migrations aplicadas: {pending.Length}.");
        return 0;
    }

    private static async Task<int> VerifyAsync(
        MySqlConnectionStringBuilder source,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(source, TargetDatabase);
        await connection.OpenAsync(cancellationToken);
        await AssertServerAsync(connection, cancellationToken);
        await AssertDatabaseAsync(connection, TargetDatabase, cancellationToken);

        var migrations = LoadMigrations();
        var applied = await LoadAppliedMigrationsAsync(connection, cancellationToken);
        ValidateAppliedChecksums(migrations, applied);

        var pending = migrations.Where(migration => !applied.ContainsKey(migration.Id)).ToArray();
        if (pending.Length != 0)
        {
            throw new InvalidOperationException(
                $"Existem {pending.Length} migrations pendentes em {TargetDatabase}.");
        }

        Console.WriteLine($"MySQL {RequiredServerVersion}, database {TargetDatabase} e migrations verificados.");
        return 0;
    }

    private static MySqlConnection CreateConnection(MySqlConnectionStringBuilder source, string database)
    {
        var builder = new MySqlConnectionStringBuilder(source.ConnectionString)
        {
            Database = database,
            AllowBatch = true,
        };
        return new MySqlConnection(builder.ConnectionString);
    }

    private static bool IsLoopback(string server)
    {
        return string.Equals(server, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(server, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(server, "::1", StringComparison.Ordinal);
    }

    private static async Task AssertServerAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT VERSION()";
        var rawVersion = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);

        if (rawVersion is null || !IsRequiredServerVersion(rawVersion))
        {
            throw new InvalidOperationException(
                $"Versão MySQL recusada. Esperado {RequiredServerVersion}; recebido {SanitizeVersion(rawVersion)}.");
        }
    }

    private static async Task AssertDatabaseAsync(
        MySqlConnection connection,
        string expectedDatabase,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATABASE()";
        var currentDatabase = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);

        if (!string.Equals(currentDatabase, expectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Database recusado. Esperado {expectedDatabase}; recebido {currentDatabase ?? "<nulo>"}.");
        }
    }

    private static async Task<bool> DatabaseExistsAsync(
        MySqlConnection connection,
        string database,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.schemata
            WHERE schema_name = @schema
            """;
        command.Parameters.AddWithValue("@schema", database);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<int> CountAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@schema", LegacyDatabase);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<string>> LoadLegacyTableNamesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var tableNames = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = @schema AND table_type = 'BASE TABLE'
            ORDER BY table_name
            """;
        command.Parameters.AddWithValue("@schema", LegacyDatabase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }

    private static async Task AssertNoAdditionalSchemaObjectsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var counts = new[]
        {
            (Name: "views", Sql: "SELECT COUNT(*) FROM information_schema.views WHERE table_schema = @schema"),
            (Name: "triggers", Sql: "SELECT COUNT(*) FROM information_schema.triggers WHERE trigger_schema = @schema"),
            (Name: "rotinas", Sql: "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = @schema"),
        };

        foreach (var (name, sql) in counts)
        {
            var count = await CountAsync(connection, transaction, sql, cancellationToken);
            if (count != 0)
            {
                throw new InvalidOperationException(
                    $"Snapshot estrutural recusado: o legado possui {count} {name} ainda não suportados pelo exportador.");
            }
        }
    }

    private static async Task<IReadOnlyList<string>> LoadLegacyEventNamesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var eventNames = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT event_name
            FROM information_schema.events
            WHERE event_schema = @schema
            ORDER BY event_name
            """;
        command.Parameters.AddWithValue("@schema", LegacyDatabase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            eventNames.Add(reader.GetString(0));
        }

        return eventNames;
    }

    private static string EscapeIdentifier(string identifier)
    {
        return identifier.Replace("`", "``", StringComparison.Ordinal);
    }

    private static IReadOnlyList<SqlMigration> LoadMigrations()
    {
        var migrationDirectory = Path.Combine(FindRepositoryRoot(), "database", "migrations");
        if (!Directory.Exists(migrationDirectory))
        {
            return [];
        }

        var migrations = Directory.EnumerateFiles(migrationDirectory, "*.sql")
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                var match = MigrationFileName().Match(fileName);
                if (!match.Success)
                {
                    throw new InvalidOperationException(
                        $"Nome de migration inválido: {fileName}. Use NNNN__descricao.sql.");
                }

                var sql = File.ReadAllText(path, Encoding.UTF8).ReplaceLineEndings("\n");
                if (string.IsNullOrWhiteSpace(sql))
                {
                    throw new InvalidOperationException($"Migration vazia: {fileName}.");
                }

                return new SqlMigration(
                    match.Groups["id"].Value,
                    match.Groups["description"].Value,
                    fileName,
                    sql,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant());
            })
            .OrderBy(migration => migration.Id, StringComparer.Ordinal)
            .ToArray();

        var duplicate = migrations
            .GroupBy(migration => migration.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"ID de migration duplicado: {duplicate.Key}.");
        }

        return migrations;
    }

    private static async Task EnsureHistoryTableAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS `{HistoryTable}` (
                `migration_id` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                `description` varchar(200) NOT NULL,
                `sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                `applied_at_utc` datetime(6) NOT NULL,
                PRIMARY KEY (`migration_id`)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, string>> LoadAppliedMigrationsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await HistoryTableExistsAsync(connection, cancellationToken))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var applied = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `migration_id`, `sha256` FROM `{HistoryTable}` ORDER BY `migration_id`";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetString(0), reader.GetString(1));
        }

        return applied;
    }

    private static async Task<bool> HistoryTableExistsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = @table
            """;
        command.Parameters.AddWithValue("@schema", TargetDatabase);
        command.Parameters.AddWithValue("@table", HistoryTable);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    private static void ValidateAppliedChecksums(
        IReadOnlyList<SqlMigration> migrations,
        IReadOnlyDictionary<string, string> applied)
    {
        var known = migrations.ToDictionary(migration => migration.Id, StringComparer.Ordinal);
        foreach (var (id, checksum) in applied)
        {
            if (!known.TryGetValue(id, out var migration))
            {
                throw new InvalidOperationException($"Migration aplicada sem arquivo local: {id}.");
            }

            if (!string.Equals(checksum, migration.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Migration aplicada foi alterada: {id}.");
            }
        }
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

        throw new InvalidOperationException("A raiz do repositório ViverAppWeb não foi localizada.");
    }

    private static void WriteLocalConnectionSecret(string connectionString)
    {
        var attribute = typeof(Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()
            ?? throw new InvalidOperationException("UserSecretsId não foi encontrado no assembly.");
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var secretDirectory = Path.Combine(applicationData, "Microsoft", "UserSecrets", attribute.UserSecretsId);
        var secretPath = Path.Combine(secretDirectory, "secrets.json");
        if (!File.Exists(secretPath))
        {
            throw new InvalidOperationException("O arquivo de user-secrets não foi encontrado.");
        }

        var root = JsonNode.Parse(File.ReadAllText(secretPath, Encoding.UTF8))?.AsObject()
            ?? throw new InvalidOperationException("O arquivo de user-secrets é inválido.");
        root["ConnectionStrings:LocalConnection"] = connectionString;

        var temporaryPath = Path.Combine(secretDirectory, $"secrets.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, secretPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string SanitizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "<nula>";
        }

        return SafeVersion().Match(version).Value is { Length: > 0 } safe ? safe : "<inválida>";
    }

    private static bool IsRequiredServerVersion(string version)
    {
        return string.Equals(version, RequiredServerVersion, StringComparison.Ordinal)
            || version.StartsWith($"{RequiredServerVersion}-", StringComparison.Ordinal)
            || version.StartsWith($"{RequiredServerVersion}+", StringComparison.Ordinal);
    }

    private static string Redact(string message)
    {
        return ConnectionSecret().Replace(message, "$1=<redigido>");
    }

    private static string DescribeException(Exception exception)
    {
        var types = new List<string>();
        int? providerCode = null;
        string? sqlState = null;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            types.Add(current.GetType().Name);
            if (current is MySqlException mysqlException)
            {
                providerCode ??= mysqlException.Number;
                sqlState ??= mysqlException.SqlState;
            }
        }

        var providerDetails = providerCode is null
            ? string.Empty
            : $" Código do provedor: {providerCode}; SQLSTATE: {sqlState ?? "<nulo>"}.";
        return $"Falha de banco ({string.Join(" -> ", types)}).{providerDetails} " + Redact(exception.Message);
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Comando desconhecido: {command}.");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Uso: dotnet run --project tools/ViverApp.Database -- <diagnose-configuration|inspect-legacy|snapshot-legacy-schema|bootstrap|configure-target|status|analyze-hot-paths|apply|verify>");
    }

    [GeneratedRegex("^(?<id>[0-9]{4})__(?<description>[a-z0-9_]+)\\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationFileName();

    [GeneratedRegex("^[0-9]+(?:\\.[0-9]+){1,3}", RegexOptions.CultureInvariant)]
    private static partial Regex SafeVersion();

    [GeneratedRegex("(?i)(password|pwd|token|secret|key)\\s*=\\s*[^;\\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionSecret();

    [GeneratedRegex(" AUTO_INCREMENT=[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex AutoIncrementOption();

    [GeneratedRegex("(?i)DEFINER=`[^`]*`@`[^`]*`\\s*")]
    private static partial Regex EventDefiner();

    [GeneratedRegex("(?i)(?:`viverappmobile`\\.|(?<![a-z0-9_])viverappmobile\\.)")]
    private static partial Regex LegacyQualifiedName();

    [GeneratedRegex("(?i)\\s+(?:ENABLE|DISABLE(?: ON SLAVE)?)\\s+DO\\s+")]
    private static partial Regex EventStatus();

    private sealed record SqlMigration(
        string Id,
        string Description,
        string FileName,
        string Sql,
        string Sha256);
}
