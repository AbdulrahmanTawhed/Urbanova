namespace Urbanova.Application.Costing;

/// <summary>Cost DTOs (Phase 11, PRD v0.2 §18). Provide UnitPrice directly, or an ItemCode
/// for catalog lookup — or neither is rejected (400). Unknown codes yield an Unavailable
/// estimate (201). Quantity may be omitted when RecommendationId is given: for area-based
/// (m²) interventions it is derived from the recommendation's analyzed polygon area and
/// reported as DerivedFromGeometry; otherwise quantity is required (400).</summary>
public sealed record CreateCostEstimateRequest(
    decimal? Quantity,
    string Unit,
    decimal? UnitPrice,
    string? ItemCode,
    string? Currency,
    Guid? RecommendationId,
    Guid? ScenarioId);

public sealed record CostEstimateResponse(
    Guid Id,
    Guid ProjectId,
    Guid? RecommendationId,
    Guid? ScenarioId,
    decimal Quantity,
    string QuantitySource,
    string Unit,
    decimal UnitPrice,
    string Currency,
    string? PriceSource,
    decimal Total,
    string Status,
    DateTimeOffset CreatedAt);
