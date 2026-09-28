namespace Urbanova.Application.Reporting;

/// <summary>Reporting DTOs (Phase 12). Format defaults to Json; comparison sections need both scenario ids.</summary>
public sealed record CreateReportRequest(
    string? Format,
    Guid? BaselineScenarioId,
    Guid? AlternativeScenarioId);

public sealed record ReportResponse(
    Guid Id,
    Guid ProjectId,
    string Format,
    int Version,
    string PayloadHash,
    bool HasFile,
    string? Content,
    DateTimeOffset GeneratedAt);
