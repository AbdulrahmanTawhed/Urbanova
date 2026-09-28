namespace Urbanova.Application.Projects;

/// <summary>
/// Project use cases. Every method is owner-scoped: pass the caller's user id and
/// the service throws <see cref="ProjectException"/> (NOT_FOUND / FORBIDDEN) as appropriate.
/// </summary>
public interface IProjectService
{
    Task<ProjectResponse> CreateAsync(Guid ownerId, CreateProjectRequest request, CancellationToken ct = default);

    Task<PagedResult<ProjectResponse>> ListAsync(Guid ownerId, int page, int pageSize, CancellationToken ct = default);

    Task<ProjectResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    Task<ProjectResponse> UpdateAsync(Guid ownerId, Guid id, UpdateProjectRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default);
}
