using Urbanova.Domain.Entities;

namespace Urbanova.Domain.BusinessRules;

/// <summary>Core formula: Quantity × UnitPrice = Total. Unavailable price → Status.Unavailable, Total 0.</summary>
public static class CostRules
{
    public static decimal CalculateTotal(decimal quantity, decimal unitPrice)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        return quantity * unitPrice;
    }

    public static void ApplyCalculation(CostEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        if (estimate.Status == CostStatus.Unavailable)
        {
            estimate.Total = 0m;
            return;
        }
        estimate.Total = CalculateTotal(estimate.Quantity, estimate.UnitPrice);
        estimate.Status = CostStatus.Calculated;
    }
}
