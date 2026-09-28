using Microsoft.EntityFrameworkCore;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.Auth.Ownership;

/// <summary>
/// Ownership checks used by controllers/services AND the MustOwnProject authorization
/// handler. Owner-only until PRD roles are finalized (see assumptions.md #10).
/// </summary>
public interface IProjectAccessChecker
{
    Task<bool> IsOwnerAsync(Guid userId, Guid projectId, CancellationToken ct = default);
}

public sealed class ProjectAccessChecker(AppDbContext db) : IProjectAccessChecker
{
    public Task<bool> IsOwnerAsync(Guid userId, Guid projectId, CancellationToken ct = default)
        => db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId, ct);
}
