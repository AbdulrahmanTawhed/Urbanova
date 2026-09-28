namespace Urbanova.Domain.ValueObjects;

/// <summary>
/// Single environmental reading. Metric + Unit stay configurable until validation
/// (MVP defaults in appsettings HeatAnalysis). IsEstimated distinguishes estimates
/// from validated measurements (PRD §8).
/// </summary>
public sealed record HeatValue(double Value, string Unit, bool IsEstimated = true);
