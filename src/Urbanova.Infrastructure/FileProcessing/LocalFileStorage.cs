using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbanova.Application.EngineeringFiles;

namespace Urbanova.Infrastructure.FileProcessing;

/// <summary>
/// Local-disk IFileStorage. Layout: {Root}/{projectId}/{fileId}_{sanitizedName}.
/// Filenames are sanitized (no traversal); missing content surfaces as FILE_STORAGE_ERROR.
/// </summary>
public sealed class LocalFileStorage(IOptions<FileStorageOptions> options, ILogger<LocalFileStorage> logger) : IFileStorage
{
    private readonly FileStorageOptions _options = options.Value;

    public string Root => Path.IsPathRooted(_options.RootPath)
        ? _options.RootPath
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.RootPath));

    public async Task<StoredFile> SaveAsync(Guid projectId, Guid fileId, string fileName, Stream content, CancellationToken ct = default)
    {
        var dir = Path.Combine(Root, projectId.ToString());
        Directory.CreateDirectory(dir);

        var safe = string.Join("_", Path.GetFileName(fileName).Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "upload.bin";
        var fileName2 = $"{fileId}_{safe}";
        var full = Path.Combine(dir, fileName2);
        if (!IsWithin(dir, full))
            throw new InvalidOperationException("Storage path escaped project directory.");

        await using var dest = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(dest, ct);
        await dest.FlushAsync(ct);
        // Opaque path uses forward slashes; always relative to Root. Size is measured,
        // never trusted from the caller.
        return new StoredFile($"{projectId}/{fileName2}", new FileInfo(full).Length);
    }

    /// <summary>Separator-suffixed prefix check so sibling prefixes (proj vs proj_evil) cannot pass.</summary>
    private static bool IsWithin(string dir, string full) =>
        full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default)
    {
        var full = Resolve(storagePath);
        if (!File.Exists(full))
            throw FileException.StorageError("stored content is missing.");
        Stream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        try
        {
            var full = Resolve(storagePath);
            if (File.Exists(full))
                File.Delete(full);
        }
        catch (Exception ex)
        {
            // Best-effort: the DB row is the source of truth; a stranded file is logged, not fatal.
            logger.LogWarning(ex, "Failed to delete stored file {StoragePath}", storagePath);
        }
        return Task.CompletedTask;
    }

    private string Resolve(string storagePath)
    {
        var relative = storagePath.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!IsWithin(Root, full))
            throw FileException.StorageError("invalid storage path.");
        return full;
    }
}
