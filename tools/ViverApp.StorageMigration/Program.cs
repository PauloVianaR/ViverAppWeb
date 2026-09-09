using System.Globalization;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace ViverApp.StorageMigration;

internal static class Program
{
    private const string DatabaseName = "viverappweb";
    private const string RequiredServerVersion = "8.0.41";
    private const int MaximumLegacyObjectBytes = 20 * 1024 * 1024;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || args[0] is not ("stage-current" or "migrate-current" or "verify-current" or "migrate-legacy" or "verify-legacy"))
        {
            Console.Error.WriteLine("Uso: dotnet run --project tools/ViverApp.StorageMigration -- <stage-current|migrate-current|verify-current|migrate-legacy|verify-legacy>");
            return 2;
        }

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets(typeof(Program).Assembly, optional: false)
                .Build();
            var connectionString = configuration.GetConnectionString("LocalConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:LocalConnection não está configurada.");
            var connectionBuilder = new MySqlConnectionStringBuilder(connectionString)
            {
                Database = DatabaseName,
                SslMode = MySqlSslMode.Disabled,
            };
            var r2 = LoadR2(configuration);

            await using var database = new MySqlConnection(connectionBuilder.ConnectionString);
            await database.OpenAsync();
            await AssertDatabaseAsync(database);
            using var storage = new AmazonS3Client(
                new BasicAWSCredentials(r2.AccessKeyId, r2.SecretAccessKey),
                new AmazonS3Config
                {
                    ServiceURL = $"https://{r2.AccountId}.r2.cloudflarestorage.com",
                    AuthenticationRegion = "auto",
                    ForcePathStyle = true,
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                });

            return args[0] switch
            {
                "stage-current" => await StageCurrentAsync(database),
                "migrate-current" => await MigrateCurrentAsync(database, storage, r2),
                "verify-current" => await VerifyCurrentAsync(database, storage, r2),
                "migrate-legacy" => await MigrateLegacyAsync(storage, r2, configuration),
                "verify-legacy" => await VerifyLegacyAsync(storage, r2, configuration),
                _ => 2,
            };
        }
        catch (Exception exception)
        {
            var diagnostic = exception is AmazonS3Exception storageException
                ? $"status={(int)storageException.StatusCode}; codigo={SafeErrorCode(storageException.ErrorCode)}"
                : "sem diagnóstico externo seguro";
            Console.Error.WriteLine(
                $"Falha segura da migração: {exception.GetType().Name}; {diagnostic}. Nenhum segredo, identificador ou nome de arquivo foi exibido.");
            return 1;
        }
    }

    private static async Task<int> StageCurrentAsync(MySqlConnection database)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("A preparação local exige o DPAPI do Windows.");
        return await StageCurrentOnWindowsAsync(database);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> StageCurrentOnWindowsAsync(MySqlConnection database)
    {
        var rows = await LoadDocumentsAsync(database);
        var protector = CreateProtector();
        var staged = 0;
        foreach (var row in rows.Where(document => document.Provider == "database"))
        {
            if (row.ProtectedContent is null)
                throw new InvalidOperationException("Um documento atual não contém o blob protegido esperado.");
            var plain = protector.Unprotect(row.ProtectedContent);
            try
            {
                if (plain.Length != row.SizeBytes
                    || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(plain), row.Sha256))
                    throw new InvalidOperationException("Um blob atual não passou pela verificação de integridade.");
                var envelope = ProtectedData.Protect(plain, StageEntropy, DataProtectionScope.LocalMachine);
                try
                {
                    var path = StagePath(row.Id);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllBytesAsync(path, envelope);
                    staged++;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(envelope);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        Console.WriteLine($"Preparação local concluída: envelopes_criptografados={staged}.");
        return 0;
    }

    private static async Task<int> MigrateLegacyAsync(
        IAmazonS3 destination,
        R2Settings destinationSettings,
        IConfiguration configuration)
    {
        var sourceSettings = LoadLegacyB2(configuration);
        using var source = CreateLegacyClient(sourceSettings);
        var objects = await ListLegacyObjectsAsync(source, sourceSettings.BucketName);
        Console.WriteLine($"Inventário legado carregado: total={objects.Count}.");
        var migrated = 0;
        var alreadyPresent = 0;

        foreach (var sourceObject in objects)
        {
            var destinationKey = LegacyDestinationKey(sourceObject.Key);
            using var response = await source.GetObjectAsync(sourceSettings.BucketName, sourceObject.Key);
            if (response.ContentLength is <= 0 or > MaximumLegacyObjectBytes)
                throw new InvalidOperationException("Um objeto legado excede o limite seguro da migração.");
            using var content = new MemoryStream(checked((int)response.ContentLength));
            var buffer = new byte[81920];
            int read;
            while ((read = await response.ResponseStream.ReadAsync(buffer)) > 0)
            {
                if (content.Length + read > MaximumLegacyObjectBytes)
                    throw new InvalidOperationException("Um objeto legado excede o limite seguro da migração.");
                await content.WriteAsync(buffer.AsMemory(0, read));
            }
            if (content.Length != response.ContentLength)
                throw new InvalidOperationException("Um objeto legado foi recebido com tamanho divergente.");
            content.Position = 0;
            var digest = await SHA256.HashDataAsync(content);
            content.Position = 0;

            if (await DestinationMatchesAsync(
                    destination,
                    destinationSettings.BucketName,
                    destinationKey,
                    content.Length,
                    digest))
            {
                alreadyPresent++;
                continue;
            }

            var put = new PutObjectRequest
            {
                BucketName = destinationSettings.BucketName,
                Key = destinationKey,
                InputStream = content,
                ContentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                    ? "application/octet-stream"
                    : response.Headers.ContentType,
                AutoCloseStream = false,
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true,
            };
            put.Metadata["sha256"] = Convert.ToHexString(digest).ToLowerInvariant();
            var uploaded = await destination.PutObjectAsync(put);
            if (uploaded.HttpStatusCode is not HttpStatusCode.OK
                || !await DestinationMatchesAsync(
                    destination,
                    destinationSettings.BucketName,
                    destinationKey,
                    content.Length,
                    digest))
                throw new InvalidOperationException("Um objeto legado não foi confirmado no destino privado.");
            migrated++;
        }

        Console.WriteLine(
            $"Migração legada concluída: migrados={migrated}; já_no_r2={alreadyPresent}; total={objects.Count}.");
        Console.WriteLine("Nenhum objeto do storage legado foi alterado ou removido.");
        return 0;
    }

    private static async Task<int> VerifyLegacyAsync(
        IAmazonS3 destination,
        R2Settings destinationSettings,
        IConfiguration configuration)
    {
        var sourceSettings = LoadLegacyB2(configuration);
        using var source = CreateLegacyClient(sourceSettings);
        var objects = await ListLegacyObjectsAsync(source, sourceSettings.BucketName);
        var verified = 0;
        var pending = 0;

        foreach (var sourceObject in objects)
        {
            using var response = await source.GetObjectAsync(sourceSettings.BucketName, sourceObject.Key);
            if (response.ContentLength is <= 0 or > MaximumLegacyObjectBytes)
                throw new InvalidOperationException("Um objeto legado excede o limite seguro da reconciliação.");
            var digest = await SHA256.HashDataAsync(response.ResponseStream);
            if (await DestinationMatchesAsync(
                    destination,
                    destinationSettings.BucketName,
                    LegacyDestinationKey(sourceObject.Key),
                    response.ContentLength,
                    digest))
                verified++;
            else
                pending++;
        }

        Console.WriteLine($"Reconciliação legada: verificados={verified}; pendentes={pending}; total={objects.Count}.");
        return pending == 0 ? 0 : 1;
    }

    private static AmazonS3Client CreateLegacyClient(LegacyB2Settings settings) => new(
        new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey),
        new AmazonS3Config
        {
            ServiceURL = settings.ServiceUrl,
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });

    private static async Task<List<S3Object>> ListLegacyObjectsAsync(IAmazonS3 source, string bucket)
    {
        var objects = new List<S3Object>();
        string? continuationToken = null;
        do
        {
            var page = await source.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket,
                ContinuationToken = continuationToken,
            });
            objects.AddRange(page.S3Objects.Where(item => !item.Key.EndsWith("/", StringComparison.Ordinal)));
            continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (continuationToken is not null);
        return objects;
    }

    private static string LegacyDestinationKey(string sourceKey)
    {
        var keyDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey))).ToLowerInvariant();
        return $"legacy-quarantine/development/{keyDigest[..2]}/{keyDigest}";
    }

    private static async Task<bool> DestinationMatchesAsync(
        IAmazonS3 destination,
        string bucket,
        string key,
        long length,
        byte[] digest)
    {
        try
        {
            var metadata = await destination.GetObjectMetadataAsync(bucket, key);
            var storedHash = metadata.Metadata["x-amz-meta-sha256"] ?? metadata.Metadata["sha256"];
            return metadata.HttpStatusCode is HttpStatusCode.OK
                && metadata.ContentLength == length
                && string.Equals(storedHash, Convert.ToHexString(digest), StringComparison.OrdinalIgnoreCase);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static async Task<int> MigrateCurrentAsync(MySqlConnection database, IAmazonS3 storage, R2Settings settings)
    {
        var rows = await LoadDocumentsAsync(database);
        var protector = CreateProtector();
        var migrated = 0;
        var alreadyR2 = 0;

        foreach (var currentRow in rows)
        {
            var row = currentRow;
            if (row.Provider == "r2")
            {
                if (!await VerifyObjectAsync(storage, settings.BucketName, row))
                    throw new InvalidOperationException("Um objeto já migrado diverge do registro local.");
                alreadyR2++;
                continue;
            }
            if (row.ProtectedContent is null)
                throw new InvalidOperationException("Um documento no banco não contém o blob protegido esperado.");

            var key = row.ObjectKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                key = $"private/development/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}";
                await using var reserve = database.CreateCommand();
                reserve.CommandText = "UPDATE private_documents SET object_key=@key, row_version=row_version+1 WHERE id=@id AND storage_provider_code='database' AND object_key IS NULL AND row_version=@version";
                reserve.Parameters.AddWithValue("@key", key);
                reserve.Parameters.AddWithValue("@id", row.Id);
                reserve.Parameters.AddWithValue("@version", row.RowVersion);
                if (await reserve.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException("Concorrência detectada ao reservar a chave de migração.");
                row = row with { ObjectKey = key, RowVersion = row.RowVersion + 1 };
            }

            var plain = LoadCurrentPlain(row, protector);
            try
            {
                var digest = SHA256.HashData(plain);
                if (plain.Length != row.SizeBytes || !CryptographicOperations.FixedTimeEquals(digest, row.Sha256))
                    throw new InvalidOperationException("Um blob local não passou pela verificação SHA-256/tamanho.");

                await using var stream = new MemoryStream(plain, writable: false);
                var put = new PutObjectRequest
                {
                    BucketName = settings.BucketName,
                    Key = key,
                    InputStream = stream,
                    ContentType = row.ContentType,
                    AutoCloseStream = false,
                    DisablePayloadSigning = true,
                    DisableDefaultChecksumValidation = true,
                };
                put.Metadata["sha256"] = Convert.ToHexString(digest).ToLowerInvariant();
                Console.WriteLine("Objeto atual preparado; iniciando envio privado.");
                var response = await storage.PutObjectAsync(put);
                Console.WriteLine("Envio privado recebido; iniciando verificação remota.");
                if (response.HttpStatusCode is not HttpStatusCode.OK)
                    throw new InvalidOperationException("O R2 não confirmou o upload.");
                if (!await VerifyObjectAsync(storage, settings.BucketName, row))
                    throw new InvalidOperationException("O objeto enviado ao R2 diverge do registro local.");

                await using var update = database.CreateCommand();
                update.CommandText = """
                    UPDATE private_documents
                    SET storage_provider_code='r2', storage_etag=@etag, migrated_at_utc=UTC_TIMESTAMP(6),
                        last_verified_at_utc=UTC_TIMESTAMP(6),
                        legacy_content_retained_until_utc=DATE_ADD(UTC_TIMESTAMP(6), INTERVAL @days DAY),
                        row_version=row_version+1
                    WHERE id=@id AND storage_provider_code='database' AND object_key=@key AND row_version=@version
                    """;
                update.Parameters.AddWithValue("@etag", response.ETag?.Trim('"'));
                update.Parameters.AddWithValue("@days", settings.RetainDatabaseFallbackDays);
                update.Parameters.AddWithValue("@id", row.Id);
                update.Parameters.AddWithValue("@key", key);
                update.Parameters.AddWithValue("@version", row.RowVersion);
                if (await update.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException("Concorrência detectada ao confirmar a migração.");
                var stagePath = StagePath(row.Id);
                if (File.Exists(stagePath)) File.Delete(stagePath);
                migrated++;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        Console.WriteLine($"Migração atual concluída: migrados={migrated}; já_no_r2={alreadyR2}; total={rows.Count}.");
        Console.WriteLine("Nenhum blob legado foi removido; a retenção permanece ativa.");
        return 0;
    }

    private static async Task<int> VerifyCurrentAsync(MySqlConnection database, IAmazonS3 storage, R2Settings settings)
    {
        var rows = await LoadDocumentsAsync(database);
        var verified = 0;
        var pending = 0;
        foreach (var row in rows)
        {
            if (row.Provider != "r2")
            {
                pending++;
                continue;
            }
            if (!await VerifyObjectAsync(storage, settings.BucketName, row))
                throw new InvalidOperationException("Um objeto R2 diverge do registro local.");
            verified++;
        }
        Console.WriteLine($"Reconciliação atual: verificados={verified}; pendentes={pending}; total={rows.Count}.");
        return pending == 0 ? 0 : 1;
    }

    private static IDataProtector CreateProtector()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("A migração local usa o key ring DPAPI criado no Windows.");
        return CreateWindowsProtector();
    }

    private static byte[] LoadCurrentPlain(DocumentRow row, IDataProtector protector)
    {
        var stagePath = StagePath(row.Id);
        if (!File.Exists(stagePath)) return protector.Unprotect(row.ProtectedContent!);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("O envelope local exige o DPAPI do Windows.");
        return UnprotectStageOnWindows(stagePath);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectStageOnWindows(string path)
    {
        var envelope = File.ReadAllBytes(path);
        try
        {
            return ProtectedData.Unprotect(envelope, StageEntropy, DataProtectionScope.LocalMachine);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelope);
        }
    }

    private static string StagePath(Guid id) =>
        Path.Combine(FindRepositoryRoot(), ".local", "storage-migration", "current", $"{id:N}.stage");

    private static readonly byte[] StageEntropy = "ViverApp.StorageMigration.Current.v1"u8.ToArray();

    [SupportedOSPlatform("windows")]
    private static IDataProtector CreateWindowsProtector()
    {
        var repository = FindRepositoryRoot();
        var keys = Path.Combine(repository, ".local", "data-protection", "api");
        var provider = DataProtectionProvider.Create(
            new DirectoryInfo(keys),
            builder => builder.SetApplicationName("ViverApp.Api").ProtectKeysWithDpapi());
        return provider.CreateProtector("ViverApp.PrivateDocuments.v1");
    }

    private static async Task<List<DocumentRow>> LoadDocumentsAsync(MySqlConnection database)
    {
        var rows = new List<DocumentRow>();
        await using var command = database.CreateCommand();
        command.CommandText = """
            SELECT id, content_type, size_bytes, sha256, storage_provider_code, object_key,
                   protected_content, row_version
            FROM private_documents
            WHERE status_code <> 'deleted'
            ORDER BY id
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                checked((int)Convert.ToUInt32(reader.GetValue(2), CultureInfo.InvariantCulture)),
                (byte[])reader[3],
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : (byte[])reader[6],
                Convert.ToUInt64(reader.GetValue(7), CultureInfo.InvariantCulture)));
        }
        return rows;
    }

    private static async Task<bool> VerifyObjectAsync(IAmazonS3 storage, string bucket, DocumentRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ObjectKey)) return false;
        var response = await storage.GetObjectMetadataAsync(bucket, row.ObjectKey);
        var hash = response.Metadata["x-amz-meta-sha256"] ?? response.Metadata["sha256"];
        return response.HttpStatusCode is HttpStatusCode.OK
            && response.ContentLength == row.SizeBytes
            && string.Equals(hash, Convert.ToHexString(row.Sha256), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertDatabaseAsync(MySqlConnection database)
    {
        await using var command = database.CreateCommand();
        command.CommandText = "SELECT DATABASE(), VERSION()";
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()
            || reader.GetString(0) != DatabaseName
            || !reader.GetString(1).StartsWith(RequiredServerVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("A ferramenta aceita somente viverappweb no MySQL 8.0.41.");
    }

    private static string SafeErrorCode(string? value) => string.IsNullOrWhiteSpace(value)
        || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            ? "indisponivel"
            : value;

    private static R2Settings LoadR2(IConfiguration configuration)
    {
        string Required(string key) => configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} não está configurado em user-secrets.");
        var days = int.TryParse(configuration["Storage:R2:RetainDatabaseFallbackDays"], CultureInfo.InvariantCulture, out var configured)
            ? configured
            : 30;
        if (days is < 1 or > 365) throw new InvalidOperationException("RetainDatabaseFallbackDays deve ficar entre 1 e 365.");
        return new(
            Required("Storage:R2:AccountId"),
            Required("Storage:R2:BucketName"),
            Required("Storage:R2:AccessKeyId"),
            Required("Storage:R2:SecretAccessKey"),
            days);
    }

    private static LegacyB2Settings LoadLegacyB2(IConfiguration configuration)
    {
        string Required(string key) => configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} não está configurado em user-secrets.");
        return new(
            Required("StorageMigration:LegacyB2:ServiceUrl"),
            Required("StorageMigration:LegacyB2:BucketName"),
            Required("StorageMigration:LegacyB2:AccessKeyId"),
            Required("StorageMigration:LegacyB2:SecretAccessKey"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ViverApp.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("A raiz de ViverAppWeb não foi localizada.");
    }

    private sealed record R2Settings(
        string AccountId,
        string BucketName,
        string AccessKeyId,
        string SecretAccessKey,
        int RetainDatabaseFallbackDays);

    private sealed record LegacyB2Settings(
        string ServiceUrl,
        string BucketName,
        string AccessKeyId,
        string SecretAccessKey);

    private sealed record DocumentRow(
        Guid Id,
        string ContentType,
        int SizeBytes,
        byte[] Sha256,
        string Provider,
        string? ObjectKey,
        byte[]? ProtectedContent,
        ulong RowVersion);
}
