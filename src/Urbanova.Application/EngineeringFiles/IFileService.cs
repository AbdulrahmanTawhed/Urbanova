using Urbanova.Application.Projects;

namespace Urbanova.Application.EngineeringFiles;

/// <summary>
/// File use cases. Owner-scoped like <see cref="IProjectService"/>; throws
/// <see cref="FileException"/> on expected failures. Upload validates eagerly
/// (synchronous MVP — background processing deferred, see phase-05 doc).
/// </summary>
public interface IFileService
{
    Task<FileResponse> UploadAsync(
        Guid ownerId, Guid projectId,
        string fileName, string contentType,
        Stream content, long length,
        CancellationToken ct = default);

    Task<PagedResult<FileResponse>> ListAsync(Guid ownerId, Guid projectId, CancellationToken ct = default);

    Task<FileResponse> GetAsync(Guid ownerId, Guid fileId, CancellationToken ct = default);

    Task<FileResponse> ValidateAsync(Guid ownerId, Guid fileId, CancellationToken ct = default);

    Task DeleteAsync(Guid ownerId, Guid fileId, CancellationToken ct = default);
}
