using System.ComponentModel.DataAnnotations;

namespace Urbanova.Domain.Common;

/// <summary>
/// Base for all persistable entities: Guid PK, audit stamps, optimistic concurrency.
/// RowVersion maps to SQL Server rowversion (Phase 2: Project, Scenario, AnalysisRun).
/// </summary>
public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Owner/creator user id (Identity Guid). Null for system-seeded rows.</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>Optimistic concurrency token. Null where not required.</summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
