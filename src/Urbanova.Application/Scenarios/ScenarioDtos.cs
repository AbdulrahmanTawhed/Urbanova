namespace Urbanova.Application.Scenarios;

/// <summary>Scenario DTOs (Phase 8). Entities never leave the service layer.</summary>
public sealed record CreateScenarioRequest(
    string Name,
    Guid? BaseAnalysisRunId,
    Guid? ParentScenarioId,
    Dictionary<string, double>? Parameters);

public sealed record UpdateScenarioRequest(
    string? Name,
    Dictionary<string, double>? Parameters,
    byte[]? RowVersion);

public sealed record AnalyzeScenarioRequest(Guid? FileId);

public sealed record ScenarioResponse(
    Guid Id,
    Guid ProjectId,
    string Kind,
    string Name,
    Dictionary<string, double> Parameters,
    bool IsLocked,
    int Version,
    Guid? ParentScenarioId,
    Guid? BaseAnalysisRunId,
    byte[]? RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
