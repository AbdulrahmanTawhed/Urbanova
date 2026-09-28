using System.Text.Json;

namespace Urbanova.Application.Recommendations;

/// <summary>Recommendation DTOs (Phase 10). Cost is a truthful two-field summary of the
/// persisted linked estimate (Phase 11): Calculated when linked, otherwise honest Unavailable.</summary>
public sealed record RecommendationCostDto(string Status, string? Reason);

public sealed record RecommendationResponse(
    Guid Id,
    Guid ProjectId,
    Guid? AnalysisRunId,
    Guid? ScenarioId,
    int PolygonIndex,
    string Problem,
    string Cause,
    string Intervention,
    JsonElement? ExpectedImpact,
    string? RuleCode,
    string? EvidenceSource,
    string EvidenceLevel,
    string? Feasibility,
    double? Confidence,
    IReadOnlyList<string> ScientificReferences,
    RecommendationCostDto Cost);
