using Urbanova.Domain.Analysis;

namespace Urbanova.Domain.Comparison;

/// <summary>
/// Comparison contracts (PRD §12). Methodology is intentionally replaceable:
/// this port consumes classified runs + scenario parameters and returns deltas,
/// without prescribing how future methods weigh dimensions.
/// Cost/feasibility dimensions resolve to Unavailable until Phases 10–11 provide data.
/// </summary>
public sealed record ComparisonInput(
    Guid BaselineScenarioId,
    IReadOnlyList<AreaValue> BaselineValues,
    IReadOnlyDictionary<string, double> BaselineParameters,
    Guid AlternativeScenarioId,
    IReadOnlyList<AreaValue> AlternativeValues,
    IReadOnlyDictionary<string, double> AlternativeParameters);

public sealed record AreaDelta(
    int PolygonIndex,
    double BaselineValue,
    string BaselineClassification,
    double AlternativeValue,
    string AlternativeClassification,
    double Delta,
    bool Improved);

public sealed record EnvironmentalDelta(
    IReadOnlyList<AreaDelta> Areas,
    double MeanBaseline,
    double MeanAlternative,
    double MeanDelta,
    int ImprovedCount,
    int WorsenedCount,
    int ImprovedClassTransitions,
    int NewProblemAreas);

public sealed record ParameterChange(string Parameter, double Baseline, double Alternative);

public sealed record DimensionStatus(string Status, string? Reason);

public sealed record ScenarioComparison(
    IReadOnlyList<ParameterChange> WhatChanged,
    EnvironmentalDelta Environmental,
    DimensionStatus Cost,
    DimensionStatus Feasibility,
    IReadOnlyList<string> Tradeoffs);

public interface IScenarioComparisonService
{
    Task<ScenarioComparison> CompareAsync(ComparisonInput input, CancellationToken ct = default);
}
