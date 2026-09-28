using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>Quantity × UnitPrice = Total. No regional dynamic pricing in MVP (PRD §14).</summary>
public sealed class CostEstimate : EntityBase
{
    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public Guid? RecommendationId { get; set; }

    public Recommendation? Recommendation { get; set; }

    public Guid? ScenarioId { get; set; }

    public Scenario? Scenario { get; set; }

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }

    public string Currency { get; set; } = "USD";

    public string? PriceSource { get; set; }

    public decimal Total { get; set; }

    public CostStatus Status { get; set; } = CostStatus.PendingValidation;
}
