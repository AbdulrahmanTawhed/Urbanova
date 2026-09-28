namespace Urbanova.Domain.ValueObjects;

/// <summary>Cost line value object. Persistence stays on CostEstimate columns; this type is for domain math.</summary>
public sealed record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency = "USD") => new(0m, currency);
}
