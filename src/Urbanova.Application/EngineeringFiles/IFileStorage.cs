namespace Urbanova.Application.EngineeringFiles;

/// <summary>
/// Content store port. MVP = local disk (Infrastructure.LocalFileStorage);
/// swap for blob storage later without touching services (PRD §6/§20).
/// </summary>
/// <summary>Save receipt: the opaque path plus the bytes actually written.</summary>
public sealed record StoredFile(string StoragePath, long SizeBytes);

public interface IFileStorage
{
    /// <returns>Opaque storage path (persisted on EngineeringFile.StoragePath) and actual size.</returns>
    Task<StoredFile> SaveAsync(Guid projectId, Guid fileId, string fileName, Stream content, CancellationToken ct = default);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default);

    Task DeleteAsync(string storagePath, CancellationToken ct = default);
}
