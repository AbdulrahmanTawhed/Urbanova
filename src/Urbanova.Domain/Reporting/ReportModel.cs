namespace Urbanova.Domain.Reporting;

using System.Text.Json;

/// <summary>
/// Canonical report model (PRD §15). Both generators render THIS — adding a future
/// format (PDF, …) means one new IReportGenerator, zero model changes.
/// Sections that lack data carry Status Unavailable + Reason instead of being omitted.
/// </summary>
public sealed record ReportSectionStatus(string Status, string? Reason);

public sealed record ReportProjectSection(
    Guid ProjectId, string Name, string? Description, string Status, DateTimeOffset GeneratedAtUtc);

public sealed record ReportSiteSection(
    string? Address, double? Latitude, double? Longitude, string Crs, double? AreaM2);

public sealed record ReportAreaValue(int PolygonIndex, double Area, double Value, string Classification);

public sealed record ReportAnalysisSection(
    ReportSectionStatus Availability,
    Guid? RunId, string? EngineName, string? EngineVersion, string? ConfigVersion,
    string? Metric, string? Unit, bool IsEstimated,
    IReadOnlyList<ReportAreaValue> Values,
    IReadOnlyDictionary<string, int> ClassificationSummary);

public sealed record ReportProblemArea(int PolygonIndex, double Value, double Area, string RecommendationHint);

public sealed record ReportScenarioSection(
    Guid ScenarioId, string Kind, string Name, bool IsLocked, int Version,
    IReadOnlyDictionary<string, double> Parameters,
    Guid? LatestRunId, double? MeanValue, string? MeanClassification);

public sealed record ReportComparisonSection(
    ReportSectionStatus Availability,
    Guid? BaselineScenarioId, Guid? AlternativeScenarioId,
    double? MeanDelta, int? ImprovedCount, int? WorsenedCount,
    decimal? CostDelta, string? CostCurrency);

/// <summary>
/// Small linked-cost projection for one recommendation: only fields already stored
/// by <c>CostEstimate</c>. Present only when a valid linked estimate exists and is
/// Calculated; otherwise null and <see cref="ReportRecommendationSection.CostStatus"/>
/// stays Unavailable (never a fabricated zero).
/// </summary>
public sealed record ReportRecommendationCost(
    string Status,
    decimal? Total,
    string? Currency,
    decimal? Quantity,
    string? QuantitySource,
    string? Unit,
    decimal? UnitPrice,
    string? PriceSource);

public sealed record ReportRecommendationSection(
    int PolygonIndex,
    string Problem,
    string Cause,
    string Intervention,
    JsonElement? ExpectedImpact,
    string? RuleCode,
    string? EvidenceSource,
    string EvidenceLevel,
    IReadOnlyList<string> ScientificReferences,
    string? Feasibility,
    double? Confidence,
    string CostStatus,
    ReportRecommendationCost? Cost);

/// <summary>
/// Project-wide cost aggregate. A combined total is only reported when every
/// Calculated estimate shares one normalized currency; mixed currencies or no
/// Calculated estimates yield Status Unavailable with null total/currency
/// (never a cross-currency sum, never a 0/USD fallback).
/// </summary>
public sealed record ReportCostSection(
    int CalculatedCount,
    int UnavailableCount,
    string Status,
    string? Reason,
    decimal? CalculatedTotal,
    string? Currency);

/// <summary>
/// How an authorized client retrieves the analyzed geometry. Reference-only:
/// method plus route template; the concrete file id lives on the referencing block.
/// No host URL is stored. Retrieval requires the source file bytes to remain available.
/// </summary>
public sealed record ReportGeometryRetrieval(
    string Method,
    string RelativePath,
    bool RequiresSourceFile);

/// <summary>
/// Reference-only spatial identity for one analyzed run. No coordinates, rings,
/// bounding boxes, or images are embedded: a client resolves polygons through
/// <see cref="ReportGeometryRetrieval"/> and maps report PolygonIndex values
/// positionally from <see cref="ReportSpatialReferences.PolygonIndexBase"/>.
/// <c>SourceEngineeringFileId</c> is the historical file identity from the run's
/// snapshot and survives source deletion; <c>LiveEngineeringFileId</c> is the
/// current relationship and nulls when the file row is gone. Anything not
/// persisted stays null rather than invented.
/// </summary>
public sealed record ReportRunSpatialReference(
    Guid? ScenarioId,
    Guid AnalysisRunId,
    Guid? SourceEngineeringFileId,
    Guid? LiveEngineeringFileId,
    string InputHash,
    string? GeometryHash,
    string? FileHash,
    string? GeometryCrs,
    int PolygonCount,
    ReportGeometryRetrieval Retrieval);

/// <summary>
/// Recommendation spatial context. Under current semantics recommendations always
/// come from the displayed analysis run, so this records that identity without
/// duplicating the full geometry block. Present whenever the displayed analysis
/// exists — including evaluated runs that produced zero recommendations, where
/// <c>RecommendationCount</c> is 0 (evaluated-empty, not skipped).
/// </summary>
public sealed record ReportRecommendationSpatialContext(
    Guid RecommendationAnalysisRunId,
    bool SameAsDisplayedAnalysis,
    int RecommendationCount);

/// <summary>
/// Additive spatial-identity block. Displayed analysis, recommendation context,
/// and each comparison side are separate references: baseline and alternative may
/// come from different files/geometries (comparison checks polygon counts, not
/// identical hashes), and the displayed run may be unrelated to both.
/// Null comparison sides mean no valid comparison was produced.
/// </summary>
public sealed record ReportSpatialReferences(
    int PolygonIndexBase,
    ReportRunSpatialReference? DisplayedAnalysis,
    ReportRecommendationSpatialContext? RecommendationContext,
    ReportRunSpatialReference? ComparisonBaseline,
    ReportRunSpatialReference? ComparisonAlternative);

public sealed record ReportModel(
    ReportProjectSection Project,
    ReportSiteSection? Site,
    ReportAnalysisSection Analysis,
    IReadOnlyList<ReportProblemArea> ProblemAreas,
    IReadOnlyList<ReportScenarioSection> Scenarios,
    ReportComparisonSection Comparison,
    IReadOnlyList<ReportRecommendationSection> Recommendations,
    ReportCostSection Costs,
    ReportSpatialReferences SpatialReferences,
    IReadOnlyList<string> DecisionSummary,
    IReadOnlyList<string> Caveats);
