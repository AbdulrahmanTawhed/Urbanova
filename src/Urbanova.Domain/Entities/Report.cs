using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>Persisted report artifact. ContentJson is canonical; file render (HTML) cached at StoragePath.</summary>
public sealed class Report : EntityBase
{
    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public ReportFormat Format { get; set; } = ReportFormat.Json;

    public string? StoragePath { get; set; }

    public string? ContentJson { get; set; }

    public string? PayloadHash { get; set; }

    public int Version { get; set; } = 1;

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
