using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>
/// Traceable analysis execution (PRD §10). Snapshot + hash + versions make results reproducible.
/// Same inputs + config => same InputHash => consistent results (determinism test, Phase 7).
/// </summary>
public sealed class AnalysisRun : EntityBase
{
    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public Guid? EngineeringFileId { get; set; }

    public EngineeringFile? EngineeringFile { get; set; }

    public Guid? ScenarioId { get; set; }

    public Scenario? Scenario { get; set; }

    public string EngineName { get; set; } = string.Empty;

    public string EngineVersion { get; set; } = string.Empty;

    public string ConfigVersion { get; set; } = string.Empty;

    public string ConfigSnapshotJson { get; set; } = "{}";

    public string InputSnapshotJson { get; set; } = "{}";

    /// <summary>SHA-256 over canonicalized (inputs + config + engine version).</summary>
    public string InputHash { get; set; } = string.Empty;

    public AnalysisStatus Status { get; set; } = AnalysisStatus.Queued;

    public string? ErrorCode { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public AnalysisResult? Result { get; set; }
}
