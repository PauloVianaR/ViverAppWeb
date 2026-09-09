using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace ViverApp.Api.Features.PatientExperience;

public sealed class PrivateStorageOptions
{
    public const string SectionName = "Storage:Private";
    public string Provider { get; set; } = "Database";
}

public sealed class R2StorageOptions
{
    public const string SectionName = "Storage:R2";
    public string? AccountId { get; set; }
    public string? BucketName { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public int PresignedUrlLifetimeSeconds { get; set; } = 60;
    public int RetainDatabaseFallbackDays { get; set; } = 30;
}

public sealed record StoredPrivateObject(string Key, string? ETag);

public interface IPrivateObjectStorage
{
    Task<StoredPrivateObject> PutAsync(byte[] content, string contentType, byte[] sha256, CancellationToken ct);
    Task<byte[]> GetAsync(string key, int expectedBytes, CancellationToken ct);
    Task<bool> ExistsAsync(string key, byte[] expectedSha256, CancellationToken ct);
    Uri CreateShortLivedDownloadUri(string key);
}

public sealed class PrivateObjectStorageUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

internal sealed class UnavailablePrivateObjectStorage : IPrivateObjectStorage
{
    private static PrivateObjectStorageUnavailableException Error() =>
        new("O armazenamento privado R2 não está configurado.");

    public Task<StoredPrivateObject> PutAsync(byte[] content, string contentType, byte[] sha256, CancellationToken ct) => throw Error();
    public Task<byte[]> GetAsync(string key, int expectedBytes, CancellationToken ct) => throw Error();
    public Task<bool> ExistsAsync(string key, byte[] expectedSha256, CancellationToken ct) => throw Error();
    public Uri CreateShortLivedDownloadUri(string key) => throw Error();
}

public sealed class R2PrivateObjectStorage(
    IAmazonS3 client,
    IOptions<R2StorageOptions> options,
    IWebHostEnvironment environment) : IPrivateObjectStorage
{
    private readonly R2StorageOptions settings = options.Value;

    public async Task<StoredPrivateObject> PutAsync(
        byte[] content,
        string contentType,
        byte[] sha256,
        CancellationToken ct)
    {
        var key = CreateOpaqueKey(environment.EnvironmentName, DateTime.UtcNow);
        try
        {
            await using var stream = new MemoryStream(content, writable: false);
            var request = new PutObjectRequest
            {
                BucketName = settings.BucketName,
                Key = key,
                InputStream = stream,
                ContentType = contentType,
                AutoCloseStream = false,
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true,
            };
            request.Metadata["sha256"] = Convert.ToHexString(sha256).ToLowerInvariant();
            var response = await client.PutObjectAsync(request, ct);
            if (response.HttpStatusCode is not HttpStatusCode.OK)
                throw new PrivateObjectStorageUnavailableException("O R2 não confirmou o armazenamento do documento.");
            return new(key, NormalizeEtag(response.ETag));
        }
        catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException or TaskCanceledException)
        {
            throw new PrivateObjectStorageUnavailableException("O armazenamento privado está temporariamente indisponível.", exception);
        }
    }

    public async Task<byte[]> GetAsync(string key, int expectedBytes, CancellationToken ct)
    {
        if (expectedBytes is <= 0 or > PrivateDocumentStore.MaximumClinicalBytes)
            throw new PrivateObjectStorageUnavailableException("O tamanho do objeto privado é inválido.");
        try
        {
            using var response = await client.GetObjectAsync(settings.BucketName, key, ct);
            if (response.HttpStatusCode is not HttpStatusCode.OK || response.ContentLength != expectedBytes)
                throw new PrivateObjectStorageUnavailableException("O objeto privado não passou pela verificação de tamanho.");
            using var output = new MemoryStream(expectedBytes);
            var buffer = new byte[81920];
            int read;
            while ((read = await response.ResponseStream.ReadAsync(buffer, ct)) > 0)
            {
                if (output.Length + read > expectedBytes)
                    throw new PrivateObjectStorageUnavailableException("O objeto privado excedeu o tamanho registrado.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            if (output.Length != expectedBytes)
                throw new PrivateObjectStorageUnavailableException("O objeto privado está incompleto.");
            return output.ToArray();
        }
        catch (PrivateObjectStorageUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException or TaskCanceledException)
        {
            throw new PrivateObjectStorageUnavailableException("O armazenamento privado está temporariamente indisponível.", exception);
        }
    }

    public async Task<bool> ExistsAsync(string key, byte[] expectedSha256, CancellationToken ct)
    {
        try
        {
            var response = await client.GetObjectMetadataAsync(settings.BucketName, key, ct);
            var actual = response.Metadata["x-amz-meta-sha256"] ?? response.Metadata["sha256"];
            return response.HttpStatusCode is HttpStatusCode.OK
                && string.Equals(actual, Convert.ToHexString(expectedSha256), StringComparison.OrdinalIgnoreCase);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException or TaskCanceledException)
        {
            throw new PrivateObjectStorageUnavailableException("O armazenamento privado está temporariamente indisponível.", exception);
        }
    }

    public Uri CreateShortLivedDownloadUri(string key)
    {
        var lifetime = Math.Clamp(settings.PresignedUrlLifetimeSeconds, 1, 300);
        var url = client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = settings.BucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddSeconds(lifetime),
            Protocol = Protocol.HTTPS,
        });
        return new(url, UriKind.Absolute);
    }

    internal static string CreateOpaqueKey(string environmentName, DateTime now) =>
        $"private/{NormalizeEnvironment(environmentName)}/{now:yyyy/MM}/{Guid.NewGuid():N}";

    private static string NormalizeEnvironment(string value) =>
        value.Equals("Production", StringComparison.OrdinalIgnoreCase) ? "production" : "development";

    private static string? NormalizeEtag(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim('"');
}

public static class PrivateObjectStorageRegistration
{
    public static IServiceCollection AddViverAppPrivateStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var provider = configuration[$"{PrivateStorageOptions.SectionName}:Provider"] ?? "Database";
        if (!provider.Equals("Database", StringComparison.OrdinalIgnoreCase)
            && !provider.Equals("R2", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Storage:Private:Provider deve ser Database ou R2.");
        if (!environment.IsDevelopment() && !provider.Equals("R2", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ambientes publicados exigem o armazenamento privado R2.");

        services.AddOptions<PrivateStorageOptions>()
            .Bind(configuration.GetSection(PrivateStorageOptions.SectionName))
            .Validate(x => x.Provider.Equals("Database", StringComparison.OrdinalIgnoreCase)
                || x.Provider.Equals("R2", StringComparison.OrdinalIgnoreCase), "Provider privado inválido.")
            .ValidateOnStart();
        services.AddOptions<R2StorageOptions>()
            .Bind(configuration.GetSection(R2StorageOptions.SectionName))
            .Validate(x => x.PresignedUrlLifetimeSeconds is >= 1 and <= 300, "A URL assinada deve expirar entre 1 e 300 segundos.")
            .Validate(x => x.RetainDatabaseFallbackDays is >= 1 and <= 365, "A retenção de transição deve ficar entre 1 e 365 dias.")
            .ValidateOnStart();

        if (!provider.Equals("R2", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPrivateObjectStorage, UnavailablePrivateObjectStorage>();
            return services;
        }

        var r2 = configuration.GetSection(R2StorageOptions.SectionName).Get<R2StorageOptions>() ?? new();
        if (new[] { r2.AccountId, r2.BucketName, r2.AccessKeyId, r2.SecretAccessKey }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("As credenciais e o bucket privado do R2 devem estar em user-secrets.");
        if (!IsSafeBucket(r2.BucketName!))
            throw new InvalidOperationException("O nome do bucket R2 privado é inválido.");

        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(r2.AccessKeyId, r2.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = $"https://{r2.AccountId}.r2.cloudflarestorage.com",
                AuthenticationRegion = "auto",
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            }));
        services.AddSingleton<IPrivateObjectStorage, R2PrivateObjectStorage>();
        return services;
    }

    private static bool IsSafeBucket(string value) => value.Length is >= 3 and <= 63
        && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '.');
}
