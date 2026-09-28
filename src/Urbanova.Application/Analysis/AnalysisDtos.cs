namespace Urbanova.Application.Analysis;

/// <summary>Analysis DTOs (Phase 7). Entities never leave the service layer.</summary>
public sealed record AnalyzeRequest(
    Guid FileId,
    Dictionary<string, double>? Parameters);

public sealed record AreaValueDto(
    int PolygonIndex,
    double Area,
    double Value,
    string Classification);

public sealed record AnalysisRunResponse(
    Guid RunId,
    Guid ProjectId,
    Guid? FileId,
    Guid? ScenarioId,
    string Status,
    string EngineName,
    string EngineVersion,
    string ConfigVersion,
    string InputHash,
    string Metric,
    string Unit,
    bool IsEstimated,
    IReadOnlyList<AreaValueDto> Values,
    IReadOnlyDictionary<string, int> ClassificationSummary,
    DateTimeOffset CreatedAt);
