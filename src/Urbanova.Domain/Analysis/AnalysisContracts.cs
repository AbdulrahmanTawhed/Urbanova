using Urbanova.Domain.ValueObjects;

namespace Urbanova.Domain.Analysis;

/// <summary>
/// Engine input: normalized geometry (never raw files) + named numeric parameters
/// (MVP: vegetationCoverPct, albedo, shadingPct — see assumptions.md §6).
/// </summary>
public sealed record AnalysisInput(
    Guid ProjectId,
    NormalizedGeometry Geometry,
    IReadOnlyDictionary<string, double> Parameters);

/// <summary>Raw per-polygon engine output (no classification — that is a separate step).</summary>
public sealed record RawAreaValue(int PolygonIndex, double Area, double Value);

/// <summary>Classified design-area value (engine output + classification).</summary>
public sealed record AreaValue(
    int PolygonIndex,
    double Area,
    double Value,
    ProblemClass Classification);

/// <summary>
/// Engine output. IsEstimated stays true until validated measurements replace
/// MVP estimates (PRD §8) — engines must never claim validation.
/// </summary>
public sealed record EngineAnalysisResult(
    string Metric,
    string Unit,
    IReadOnlyList<RawAreaValue> Values,
    string MethodProvenance,
    bool IsEstimated);

/// <summary>Heat engine port (PRD §8). Implementations must be pure + deterministic.</summary>
public interface IEnvironmentalAnalysisEngine
{
    string EngineName { get; }

    string EngineVersion { get; }

    Task<EngineAnalysisResult> AnalyzeAsync(
        AnalysisInput input, CancellationToken ct = default);
}

/// <summary>Classification port (PRD §9). Thresholds come from configuration, never code.</summary>
public interface IEnvironmentalClassificationService
{
    ProblemClass Classify(double value);

    IReadOnlyDictionary<ProblemClass, int> Summarize(IEnumerable<double> values);
}
