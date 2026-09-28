namespace Urbanova.Application.Costing;

/// <summary>Cost DTOs (Phase 11). Provide UnitPrice directly, or an ItemCode for catalog
/// lookup — or neither is rejected (400). Unknown codes yield an Unavailable estimate (201).</summary>
public sealed record CreateCostEstimateRequest(
    decimal Quantity,
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
    string Unit,
    decimal UnitPrice,
    string Currency,
    string? PriceSource,
    decimal Total,
    string Status,
    DateTimeOffset CreatedAt);
