using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.PatientExperience;

public interface IDocumentMalwareScanner
{
    Task<bool> IsCleanAsync(byte[] content, CancellationToken ct);
}

// MpCmdRun deve retornar limpo; falha, ausência ou timeout recusam o arquivo.
public sealed class WindowsDocumentMalwareScanner(IWebHostEnvironment environment, IConfiguration configuration) : IDocumentMalwareScanner
{
    internal static void AssertProductionReady(IConfiguration configuration, string contentRootPath)
    {
        var executable = configuration["Security:MalwareScan:ExecutablePath"];
        var workDirectory = configuration["Security:MalwareScan:WorkDirectory"];
        if (!OperatingSystem.IsWindows()
            || string.IsNullOrWhiteSpace(executable)
            || !Path.IsPathFullyQualified(executable)
            || !string.Equals(Path.GetFileName(executable), "MpCmdRun.exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(executable)
            || string.IsNullOrWhiteSpace(workDirectory)
            || !Path.IsPathFullyQualified(workDirectory)
            || !Directory.Exists(workDirectory)
            || IsWithinContentRoot(workDirectory, contentRootPath))
        {
            throw new InvalidOperationException(
                "A verificação de anexos exige MpCmdRun.exe e diretório de trabalho privado, existente e fora da publicação.");
        }
    }

    private static bool IsWithinContentRoot(string directory, string contentRootPath)
    {
        var root = Path.GetFullPath(contentRootPath).TrimEnd(Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> IsCleanAsync(byte[] content, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var scanner = environment.IsDevelopment()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe")
            : configuration["Security:MalwareScan:ExecutablePath"];
        var root = environment.IsDevelopment()
            ? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", ".local", "document-scan"))
            : configuration["Security:MalwareScan:WorkDirectory"];
        if (string.IsNullOrWhiteSpace(scanner) || string.IsNullOrWhiteSpace(root)
            || !Path.IsPathFullyQualified(scanner) || !Path.IsPathFullyQualified(root)
            || !File.Exists(scanner)) return false;
        if (environment.IsDevelopment()) Directory.CreateDirectory(root);
        else if (!Directory.Exists(root) || IsWithinContentRoot(root, environment.ContentRootPath)) return false;
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".scan");
        try
        {
            await File.WriteAllBytesAsync(path, content, ct);
            using var process = new Process { StartInfo = new ProcessStartInfo(scanner) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            foreach (var argument in new[] { "-Scan", "-ScanType", "3", "-File", path, "-DisableRemediation" }) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); return false; }
            await Task.WhenAll(stdout, stderr);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return false; }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public sealed class PrivateDocumentStore
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public const int MaximumClinicalBytes = 10 * 1024 * 1024;
    private readonly ViverAppDbContext database;
    private readonly IDataProtector protector;
    private readonly IDocumentMalwareScanner scanner;
    private readonly IWebHostEnvironment environment;
    private readonly IPrivateObjectStorage objectStorage;
    private readonly PrivateStorageOptions storageOptions;

    public PrivateDocumentStore(
        ViverAppDbContext database,
        IDataProtectionProvider protection,
        IDocumentMalwareScanner scanner,
        IWebHostEnvironment environment,
        IPrivateObjectStorage objectStorage,
        IOptions<PrivateStorageOptions> storageOptions)
    {
        this.database = database;
        protector = protection.CreateProtector("ViverApp.PrivateDocuments.v1");
        this.scanner = scanner;
        this.environment = environment;
        this.objectStorage = objectStorage;
        this.storageOptions = storageOptions.Value;
    }

    public PrivateDocumentStore(
        ViverAppDbContext database,
        IDataProtectionProvider protection,
        IDocumentMalwareScanner scanner,
        IWebHostEnvironment environment)
        : this(database, protection, scanner, environment, new UnavailablePrivateObjectStorage(),
            Options.Create(new PrivateStorageOptions { Provider = "Database" }))
    {
    }

    public static string ValidateContent(string name, string mime, byte[] content, int maximumBytes = MaximumBytes)
    {
        if (name.Length is < 1 or > 200 || name.Any(c => char.IsControl(c) || c is '/' or '\\' or ':') || name.Contains("..", StringComparison.Ordinal))
            throw PatientExperienceService.Invalid("Nome de arquivo inválido.");
        if (content.Length == 0 || content.Length > maximumBytes) throw PatientExperienceService.Invalid($"Envie um arquivo de até {maximumBytes / 1024 / 1024} MB.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var valid = extension switch
        {
            ".png" => mime == "image/png" && ValidPng(content),
            ".jpg" or ".jpeg" => mime == "image/jpeg" && content.Length > 4 && content[0] == 255 && content[1] == 216 && content[2] == 255 && content[^2] == 255 && content[^1] == 217,
            ".pdf" => mime == "application/pdf" && content.AsSpan().StartsWith("%PDF-"u8) && Encoding.Latin1.GetString(content.AsSpan(Math.Max(0, content.Length - 20))).TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal),
            _ => false,
        };
        if (!valid) throw PatientExperienceService.Invalid("Envie PDF, PNG ou JPEG válido, com extensão e conteúdo correspondentes.");
        var text = Encoding.Latin1.GetString(content);
        if (new[] { "<script", "<?php", "<html", "/JavaScript", "/JS", "/Launch", "/EmbeddedFile", "/OpenAction", "/RichMedia", "/XFA", "/AA", "EICAR-STANDARD-ANTIVIRUS-TEST-FILE" }.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)))
            throw PatientExperienceService.Invalid("O arquivo contém conteúdo ativo ou não permitido. Exporte uma cópia simples.");
        return mime;
    }

    private static bool ValidPng(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!bytes.AsSpan().StartsWith(signature)) return false;
        var offset = 8; var header = false; var data = false;
        while (offset <= bytes.Length - 12)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > MaximumClinicalBytes || offset + 12L + length > bytes.Length) return false;
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            var crc = uint.MaxValue;
            foreach (var value in bytes.AsSpan(offset + 4, checked((int)length + 4)))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u);
            }
            if (~crc != System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + (int)length, 4))) return false;
            if (!header)
            {
                if (type != "IHDR" || length != 13) return false;
                var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8, 4));
                var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12, 4));
                if (width == 0 || height == 0 || (ulong)width * height > 20_000_000) return false;
                header = true;
            }
            else if (type == "IHDR") return false;
            if (type == "IDAT") data = true;
            offset += checked((int)length + 12);
            if (type == "IEND") return data && length == 0 && offset == bytes.Length;
        }
        return false;
    }

    public Task<PrivateDocument> PrepareAsync(ulong owner, IFormFile file, CancellationToken ct) =>
        PrepareAsync(owner, file, MaximumBytes, ct);

    public Task<PrivateDocument> PrepareClinicalAsync(ulong patientOwner, IFormFile file, CancellationToken ct) =>
        PrepareAsync(patientOwner, file, MaximumClinicalBytes, ct);

    private async Task<PrivateDocument> PrepareAsync(ulong owner, IFormFile file, int maximumBytes, CancellationToken ct)
    {
        if (!environment.IsDevelopment() && !UsesR2()) throw StorageUnavailable();
        if (file.Length <= 0 || file.Length > maximumBytes) throw PatientExperienceService.Invalid($"Envie um arquivo de até {maximumBytes / 1024 / 1024} MB.");
        using var content = new MemoryStream();
        await using var stream = file.OpenReadStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (content.Length + read > maximumBytes) throw PatientExperienceService.Invalid($"O arquivo excede {maximumBytes / 1024 / 1024} MB.");
            await content.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var bytes = content.ToArray();
        var mime = ValidateContent(file.FileName, file.ContentType, bytes, maximumBytes);
        if (!await scanner.IsCleanAsync(bytes, ct)) throw new PatientExperienceException(422, "O arquivo não pôde ser aprovado pela verificação de segurança. Tente outro documento ou entre em contato com a clínica.");
        var sha256 = SHA256.HashData(bytes);
        var stored = UsesR2()
            ? await StoreInR2Async(bytes, mime, sha256, ct)
            : null;
        return new()
        {
            Id = Guid.NewGuid(),
            OwnerAccountId = owner,
            OriginalFileName = file.FileName,
            ContentType = mime,
            SizeBytes = (uint)bytes.Length,
            Sha256 = sha256,
            StorageProviderCode = stored is null ? "database" : "r2",
            ObjectKey = stored?.Key,
            StorageEtag = stored?.ETag,
            LastVerifiedAtUtc = stored is null ? null : DateTime.UtcNow,
            ProtectedContent = stored is null ? protector.Protect(bytes) : null,
            RowVersion = 1,
            StatusCode = "available",
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    public async Task<(byte[] Content, string Mime, string Name)> DownloadAsync(ulong actor, Guid id, CancellationToken ct)
    {
        var document = await database.PrivateDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerAccountId == actor && x.StatusCode == "available", ct) ?? throw PatientExperienceService.Missing();
        var key = id.ToString("D");
        var premium = await database.PremiumMemberships.AnyAsync(x => x.AccountId == actor && x.ProofDocumentId == id, ct);
        var clinical = await database.AppointmentDocuments.AnyAsync(x => x.ObjectKey == key && x.StatusCode == "available" && x.Appointment.PatientAccountId == actor
            && x.Appointment.MedicalReport != null && x.Appointment.MedicalReport.StatusCode == "published", ct);
        if (!premium && !clinical) throw PatientExperienceService.Missing();
        var bytes = await LoadContentAsync(document, ct);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), document.Sha256)) throw PatientExperienceService.Missing();
        return (bytes, document.ContentType, document.OriginalFileName);
    }

    public async Task<(byte[] Content, string Mime, string Name)> DownloadForDoctorAsync(ulong doctor, ulong documentId, CancellationToken ct)
    {
        var link = await database.AppointmentDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId
            && x.StatusCode == "available" && x.Appointment.ProfessionalAccountId == doctor, ct) ?? throw PatientExperienceService.Missing();
        if (!Guid.TryParse(link.ObjectKey, out var privateId)) throw PatientExperienceService.Missing();
        var document = await database.PrivateDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == privateId && x.StatusCode == "available", ct)
            ?? throw PatientExperienceService.Missing();
        var bytes = await LoadContentAsync(document, ct);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), document.Sha256)) throw PatientExperienceService.Missing();
        return (bytes, document.ContentType, document.OriginalFileName);
    }

    public async Task<(byte[] Content, string Mime, string Name)> DownloadAuthorizedClinicalAsync(Guid id, CancellationToken ct)
    {
        var document = await database.PrivateDocuments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.StatusCode == "available", ct)
            ?? throw PatientExperienceService.Missing();
        var bytes = await LoadContentAsync(document, ct);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), document.Sha256))
            throw PatientExperienceService.Missing();
        return (bytes, document.ContentType, document.OriginalFileName);
    }

    public async Task<(byte[] Content, string Mime, string Name, Guid DocumentId)> DownloadPremiumForManagerAsync(
        ulong manager,
        ulong membershipId,
        CancellationToken ct)
    {
        if (!await database.Accounts.AsNoTracking().AnyAsync(x => x.Id == manager
            && (x.RoleCode == "manager" || x.RoleCode == "administrator") && x.StatusCode == "active", ct))
            throw PatientExperienceService.Missing();
        var document = await database.PremiumMemberships.AsNoTracking()
            .Where(x => x.Id == membershipId && x.ProofDocumentId != null && x.ProofDocument!.StatusCode == "available")
            .Select(x => x.ProofDocument!)
            .SingleOrDefaultAsync(ct) ?? throw PatientExperienceService.Missing();
        var bytes = await LoadContentAsync(document, ct);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), document.Sha256)) throw PatientExperienceService.Missing();
        return (bytes, document.ContentType, document.OriginalFileName, document.Id);
    }

    public async Task<PatientScheduling.SchedulingPage<PatientDocumentResponse>> ListAsync(ulong actor, ulong appointment, int page, int pageSize, CancellationToken ct)
    {
        PatientExperienceService.ValidatePage(page, pageSize);
        if (!await database.Appointments.AnyAsync(x => x.Id == appointment && x.PatientAccountId == actor && x.MedicalReport != null && x.MedicalReport.StatusCode == "published", ct)) throw PatientExperienceService.Missing();
        var query = database.PrivateDocuments.FromSqlInterpolated($"SELECT pd.* FROM private_documents pd WHERE pd.owner_account_id={actor} AND pd.status_code='available' AND EXISTS (SELECT 1 FROM appointment_documents ad WHERE ad.appointment_id={appointment} AND ad.status_code='available' AND ad.object_key=pd.id)").AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PatientDocumentResponse(x.Id, x.OriginalFileName, x.ContentType, x.SizeBytes)).ToArrayAsync(ct);
        return new(items, page, pageSize, total);
    }

    private bool UsesR2() => storageOptions.Provider.Equals("R2", StringComparison.OrdinalIgnoreCase);

    private async Task<StoredPrivateObject?> StoreInR2Async(byte[] bytes, string mime, byte[] sha256, CancellationToken ct)
    {
        try
        {
            return await objectStorage.PutAsync(bytes, mime, sha256, ct);
        }
        catch (PrivateObjectStorageUnavailableException)
        {
            throw StorageUnavailable();
        }
    }

    private async Task<byte[]> LoadContentAsync(PrivateDocument document, CancellationToken ct)
    {
        if (document.StorageProviderCode.Equals("r2", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(document.ObjectKey))
        {
            try
            {
                return await objectStorage.GetAsync(document.ObjectKey, checked((int)document.SizeBytes), ct);
            }
            catch (PrivateObjectStorageUnavailableException) when (document.ProtectedContent is not null)
            {
                return protector.Unprotect(document.ProtectedContent);
            }
            catch (PrivateObjectStorageUnavailableException)
            {
                throw StorageUnavailable();
            }
        }
        if (document.ProtectedContent is null) throw StorageUnavailable();
        return protector.Unprotect(document.ProtectedContent);
    }

    private static PatientExperienceException StorageUnavailable() =>
        new(503, "O armazenamento privado está temporariamente indisponível. Tente novamente em instantes.");
}
