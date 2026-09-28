using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>
/// Baseline = locked original state; Alternative inherits via ParentScenarioId.
/// Modifiable params are a validated JSON dict (schema finalized later) — see ScenarioRules.
/// </summary>
public sealed class Scenario : EntityBase
{
    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public Guid? ParentScenarioId { get; set; }

    public Scenario? ParentScenario { get; set; }

    public ICollection<Scenario> ChildScenarios { get; set; } = new List<Scenario>();

    public Guid? BaseAnalysisRunId { get; set; }

    public AnalysisRun? BaseAnalysisRun { get; set; }

    public ScenarioKind Kind { get; set; } = ScenarioKind.Baseline;

    public string Name { get; set; } = string.Empty;

    /// <summary>Configurable params, e.g. {vegetationCoverPct, albedo, shadingPct}. Validated JSON.</summary>
    public string ParametersJson { get; set; } = "{}";

    public bool IsLocked { get; set; }

    public int Version { get; set; } = 1;
}
