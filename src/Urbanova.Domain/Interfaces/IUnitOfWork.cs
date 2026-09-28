namespace Urbanova.Domain.Interfaces;

/// <summary>Persistence port. Implemented by Infrastructure.AppDbContext (Phase 2).</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
