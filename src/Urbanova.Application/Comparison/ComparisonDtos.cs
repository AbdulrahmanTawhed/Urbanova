namespace Urbanova.Application.Comparison;

/// <summary>Comparison DTOs (Phase 9). Computed on demand — never stored.</summary>
public sealed record ComparisonSideDto(Guid ScenarioId, Guid RunId, string ScenarioName);

public sealed record AreaDeltaDto(
    int PolygonIndex,
    double BaselineValue,
    string BaselineClassification,
    double AlternativeValue,
    string AlternativeClassification,
    double Delta,
    bool Improved);

public sealed record EnvironmentalDeltaDto(
    IReadOnlyList<AreaDeltaDto> Areas,
    double MeanBaseline,
    double MeanAlternative,
    double MeanDelta,
    int ImprovedCount,
    int WorsenedCount,
    int ImprovedClassTransitions,
    int NewProblemAreas);

public sealed record ParameterChangeDto(string Parameter, double Baseline, double Alternative);

public sealed record DimensionStatusDto(string Status, string? Reason);

/// <summary>Phase 11: totals over Calculated estimates linked to each scenario (null when unavailable).</summary>
public sealed record CostDeltaDto(
    string Status,
    string? Reason,
    decimal? BaselineTotal,
    decimal? AlternativeTotal,
    decimal? Delta,
    string? Currency);

public sealed record ComparisonResponse(
    ComparisonSideDto Baseline,
    ComparisonSideDto Alternative,
    IReadOnlyList<ParameterChangeDto> WhatChanged,
    EnvironmentalDeltaDto Environmental,
    CostDeltaDto Cost,
    DimensionStatusDto Feasibility,
    IReadOnlyList<string> Tradeoffs);
