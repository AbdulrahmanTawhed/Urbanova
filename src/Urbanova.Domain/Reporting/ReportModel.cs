namespace Urbanova.Domain.Reporting;

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

public sealed record ReportRecommendationSection(
    int PolygonIndex, string Problem, string Intervention,
    string EvidenceLevel, string? Feasibility, double? Confidence, string CostStatus);

public sealed record ReportCostSection(
    int CalculatedCount, int UnavailableCount, decimal CalculatedTotal, string Currency);

public sealed record ReportModel(
    ReportProjectSection Project,
    ReportSiteSection? Site,
    ReportAnalysisSection Analysis,
    IReadOnlyList<ReportProblemArea> ProblemAreas,
    IReadOnlyList<ReportScenarioSection> Scenarios,
    ReportComparisonSection Comparison,
    IReadOnlyList<ReportRecommendationSection> Recommendations,
    ReportCostSection Costs,
    IReadOnlyList<string> DecisionSummary,
    IReadOnlyList<string> Caveats);
