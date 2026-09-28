namespace Urbanova.Application.Analysis;

/// <summary>
/// Analysis use cases (Phase 7). Run is idempotent: identical inputs + configuration
/// return the existing run (determinism, PRD §10) instead of duplicating rows.
/// </summary>
public interface IAnalysisService
{
    Task<(AnalysisRunResponse Response, bool IsNew)> RunAsync(
        Guid ownerId, Guid projectId, AnalyzeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Phase 8: run with a scenario's stored parameters (request params unsupported —
    /// modify the scenario first). Geometry comes from the scenario's baseline analysis
    /// file unless <paramref name="fileIdOverride"/> is given. Scenario id scopes the
    /// InputHash (direct-run hashes unchanged).
    /// </summary>
    Task<(AnalysisRunResponse Response, bool IsNew)> RunForScenarioAsync(
        Guid ownerId, Guid scenarioId, Guid? fileIdOverride, CancellationToken ct = default);

    Task<AnalysisRunResponse> GetAsync(Guid ownerId, Guid runId, CancellationToken ct = default);
}
