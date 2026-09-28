using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>Explainable recommendation: Problem → Cause → Intervention → Impact → Evidence → Cost → Feasibility.</summary>
public sealed class Recommendation : EntityBase
{
    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public Guid? AnalysisRunId { get; set; }

    public AnalysisRun? AnalysisRun { get; set; }

    public Guid? ScenarioId { get; set; }

    public Scenario? Scenario { get; set; }

    public Guid? CostEstimateId { get; set; }

    public CostEstimate? CostEstimate { get; set; }

    /// <summary>Evidence rule backing this recommendation (PRD v0.2 §5). Required.</summary>
    public Guid? RecommendationRuleId { get; set; }

    public RecommendationRule? RecommendationRule { get; set; }

    /// <summary>Design-area index this recommendation targets (Phase 10).</summary>
    public int PolygonIndex { get; set; }

    public string Problem { get; set; } = string.Empty;

    public string Cause { get; set; } = string.Empty;

    public string Intervention { get; set; } = string.Empty;

    public string? ExpectedImpactJson { get; set; }

    public string? EvidenceSource { get; set; }

    public EvidenceLevel EvidenceLevel { get; set; } = EvidenceLevel.PendingValidation;

    public string? Feasibility { get; set; }

    /// <summary>Null until a defined confidence model exists (PRD unresolved — see assumptions.md).</summary>
    public double? Confidence { get; set; }
}
