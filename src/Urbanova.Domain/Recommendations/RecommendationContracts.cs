using Urbanova.Domain.Analysis;

namespace Urbanova.Domain.Recommendations;

/// <summary>
/// Recommendation contracts (PRD §13). Structure: Problem → Cause → Intervention →
/// Expected Impact → Evidence → Cost → Feasibility. Evidence levels are mandatory;
/// MVP engines must NEVER emit Validated (no validated measurements exist yet).
/// Cost linkage arrives in Phase 11 (EstimatedCost left null until then).
/// </summary>
public sealed record RecommendationInput(
    Guid ProjectId,
    Guid AnalysisRunId,
    Guid? ScenarioId,
    IReadOnlyList<AreaValue> AreaValues,
    IReadOnlyDictionary<string, double> Parameters);

public sealed record ExpectedImpact(double PredictedDeltaC, string TargetClassification, string Basis);

public sealed record GeneratedRecommendation(
    int PolygonIndex,
    string Problem,
    string Cause,
    string Intervention,
    ExpectedImpact ExpectedImpact,
    string RuleCode,
    string EvidenceSource,
    EvidenceLevel EvidenceLevel,
    string Feasibility,
    double? Confidence);

public interface IRecommendationEngine
{
    Task<IReadOnlyList<GeneratedRecommendation>> GenerateAsync(
        RecommendationInput input, CancellationToken ct = default);
}
