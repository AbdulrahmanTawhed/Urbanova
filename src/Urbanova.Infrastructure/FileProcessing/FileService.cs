using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Urbanova.Application.EngineeringFiles;
using Urbanova.Domain;
using Urbanova.Domain.Entities;
using Urbanova.Domain.ValueObjects;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.FileProcessing;

/// <summary>
/// File use cases. Ownership enforced per call (defense in depth alongside controller policies).
/// Upload validates eagerly (synchronous MVP); invalid content is still persisted with
/// status Invalid for traceability, but the request fails with INVALID_FILE (422).
/// </summary>
public sealed class FileService(
    AppDbContext db,
    IProcessorRegistry registry,
    IFileStorage storage,
    IOptions<FileStorageOptions> options) : IFileService
{
    private readonly FileStorageOptions _options = options.Value;

    /// <summary>Stored JSON uses camelCase to match API response conventions.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<FileResponse> UploadAsync(
        Guid ownerId, Guid projectId,
        string fileName, string contentType,
        Stream content, long length,
        CancellationToken ct = default)
    {
        var owner = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct)
            ?? throw FileException.NotFound(projectId);
        if (owner != ownerId)
            throw FileException.Forbidden();

        if (length <= 0)
            throw FileException.InvalidFile(["File is empty."]);
        if (length > _options.MaxBytes)
            throw FileException.TooLarge(_options.MaxBytes);

        fileName = Path.GetFileName(fileName);
        contentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
        var processor = registry.Find(fileName, contentType)
            ?? throw FileException.UnsupportedFormat(fileName, string.Join("; ", registry.SupportedFormats));

        var fileId = Guid.NewGuid();
        var saved = await storage.SaveAsync(projectId, fileId, fileName, content, ct);
        var storagePath = saved.StoragePath;
        // Trust measured bytes, not the client-supplied length header.
        if (saved.SizeBytes <= 0)
        {
            await storage.DeleteAsync(storagePath, ct);
            throw FileException.InvalidFile(["File is empty."]);
        }
        if (saved.SizeBytes > _options.MaxBytes)
        {
            await storage.DeleteAsync(storagePath, ct);
            throw FileException.TooLarge(_options.MaxBytes);
        }
        var hash = await Sha256HexAsync(storagePath, ct);

        var duplicateId = await db.EngineeringFiles
            .Where(f => f.ProjectId == projectId && f.HashSha256 == hash)
            .Select(f => (Guid?)f.Id)
            .SingleOrDefaultAsync(ct);
        if (duplicateId is not null)
        {
            await storage.DeleteAsync(storagePath, ct);
            throw FileException.Duplicate(duplicateId.Value);
        }

        await using var stream = await storage.OpenReadAsync(storagePath, ct);
        var validation = await processor.ValidateAsync(stream, fileName, ct);
        EngineeringFileMetadata? metadata = null;
        if (validation.IsValid)
        {
            await using var metaStream = await storage.OpenReadAsync(storagePath, ct);
            metadata = await processor.GetMetadataAsync(metaStream, fileName, ct);
        }

        var entity = new EngineeringFile
        {
            Id = fileId,
            ProjectId = projectId,
            UploadedBy = ownerId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = saved.SizeBytes,
            StoragePath = storagePath,
            FormatDetected = validation.FormatDetected,
            HashSha256 = hash,
            ValidationStatus = validation.IsValid ? FileValidationStatus.Valid : FileValidationStatus.Invalid,
            ValidationErrorsJson = validation.IsValid ? null : JsonSerializer.Serialize(validation.Errors, JsonOptions),
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions),
            CreatedBy = ownerId,
        };
        db.EngineeringFiles.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the dedupe race: a concurrent identical upload won. Clean up our
            // stored copy and report the winner as a duplicate instead of a 500.
            await storage.DeleteAsync(storagePath, ct);
            db.ChangeTracker.Clear();
            var winnerId = await db.EngineeringFiles
                .Where(f => f.ProjectId == projectId && f.HashSha256 == hash)
                .Select(f => (Guid?)f.Id)
                .SingleOrDefaultAsync(ct);
            if (winnerId is not null)
                throw FileException.Duplicate(winnerId.Value);
            throw;
        }

        if (!validation.IsValid)
            throw FileException.InvalidFile(validation.Errors);

        return ToResponse(entity);
    }

    public async Task<Urbanova.Application.Projects.PagedResult<FileResponse>> ListAsync(
        Guid ownerId, Guid projectId, CancellationToken ct = default)
    {
        var owner = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct)
            ?? throw FileException.NotFound(projectId);
        if (owner != ownerId)
            throw FileException.Forbidden();

        var files = await db.EngineeringFiles
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);
        var items = files.Select(ToResponse).ToList();
        return new Urbanova.Application.Projects.PagedResult<FileResponse>(items, 1, items.Count, items.Count, 1);
    }

    public async Task<FileResponse> GetAsync(Guid ownerId, Guid fileId, CancellationToken ct = default)
    {
        var file = await LoadOwnedAsync(ownerId, fileId, ct);
        return ToResponse(file);
    }

    public async Task<FileResponse> ValidateAsync(Guid ownerId, Guid fileId, CancellationToken ct = default)
    {
        var file = await LoadOwnedAsync(ownerId, fileId, ct);
        var processor = registry.FindByFormat(file.FormatDetected ?? "")
            ?? registry.Find(file.FileName, file.ContentType)
            ?? throw FileException.UnsupportedFormat(file.FileName, string.Join("; ", registry.SupportedFormats));

        await using var stream = await storage.OpenReadAsync(file.StoragePath, ct);
        var validation = await processor.ValidateAsync(stream, file.FileName, ct);
        EngineeringFileMetadata? metadata = null;
        if (validation.IsValid)
        {
            await using var metaStream = await storage.OpenReadAsync(file.StoragePath, ct);
            metadata = await processor.GetMetadataAsync(metaStream, file.FileName, ct);
        }

        file.ValidationStatus = validation.IsValid ? FileValidationStatus.Valid : FileValidationStatus.Invalid;
        file.ValidationErrorsJson = validation.IsValid ? null : JsonSerializer.Serialize(validation.Errors, JsonOptions);
        file.MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions);
        await db.SaveChangesAsync(ct);

        if (!validation.IsValid)
            throw FileException.InvalidFile(validation.Errors);

        db.ChangeTracker.Clear();
        var reloaded = await db.EngineeringFiles.SingleAsync(f => f.Id == fileId, ct);
        return ToResponse(reloaded);
    }

    public async Task DeleteAsync(Guid ownerId, Guid fileId, CancellationToken ct = default)
    {
        var file = await LoadOwnedAsync(ownerId, fileId, ct);
        // DB row first: if SaveChanges fails the file remains and the row still points at it.
        // The content delete is best-effort (stranded files are logged, never fatal).
        var storagePath = file.StoragePath;
        db.EngineeringFiles.Remove(file);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(storagePath, ct);
    }

    private async Task<EngineeringFile> LoadOwnedAsync(Guid ownerId, Guid fileId, CancellationToken ct)
    {
        var file = await db.EngineeringFiles.SingleOrDefaultAsync(f => f.Id == fileId, ct)
            ?? throw FileException.NotFound(fileId);
        var owner = await db.Projects
            .Where(p => p.Id == file.ProjectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct);
        if (owner is null || owner != ownerId)
            throw owner is null ? FileException.NotFound(fileId) : FileException.Forbidden();
        return file;
    }

    private async Task<string> Sha256HexAsync(string storagePath, CancellationToken ct)
    {
        await using var stream = await storage.OpenReadAsync(storagePath, ct);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static FileResponse ToResponse(EngineeringFile f)
    {
        IReadOnlyList<string> errors = string.IsNullOrWhiteSpace(f.ValidationErrorsJson)
            ? []
            : JsonSerializer.Deserialize<string[]>(f.ValidationErrorsJson!) ?? [];
        JsonElement? metadata = null;
        if (!string.IsNullOrWhiteSpace(f.MetadataJson))
            metadata = JsonDocument.Parse(f.MetadataJson!).RootElement.Clone();
        return new FileResponse(
            f.Id, f.ProjectId, f.FileName, f.ContentType, f.SizeBytes,
            f.FormatDetected, f.HashSha256, f.ValidationStatus.ToString(),
            errors, metadata, f.CreatedAt, f.UpdatedAt);
    }
}
