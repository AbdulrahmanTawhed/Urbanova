using System.Text.Json;

namespace Urbanova.Application.Recommendations;

/// <summary>Recommendation DTOs (Phase 10). Cost links in Phase 11 — until then an honest Unavailable.</summary>
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
    string? EvidenceSource,
    string EvidenceLevel,
    string? Feasibility,
    double? Confidence,
    RecommendationCostDto Cost);
