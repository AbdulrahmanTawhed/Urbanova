using Urbanova.Application.Analysis;

namespace Urbanova.Application.Scenarios;

/// <summary>
/// Scenario use cases (Phase 8). Owner-scoped; throws <see cref="ScenarioException"/>.
/// No ParentScenarioId → locked Baseline; with ParentScenarioId → Alternative inheriting
/// the baseline (params merged, request wins). Alternatives stay mutable with Version++.
/// </summary>
public interface IScenarioService
{
    Task<ScenarioResponse> CreateAsync(Guid ownerId, Guid projectId, CreateScenarioRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ScenarioResponse>> ListAsync(Guid ownerId, Guid projectId, CancellationToken ct = default);

    Task<ScenarioResponse> GetAsync(Guid ownerId, Guid scenarioId, CancellationToken ct = default);

    Task<ScenarioResponse> UpdateAsync(Guid ownerId, Guid scenarioId, UpdateScenarioRequest request, CancellationToken ct = default);

    Task<(AnalysisRunResponse Response, bool IsNew)> AnalyzeAsync(
        Guid ownerId, Guid scenarioId, AnalyzeScenarioRequest request, CancellationToken ct = default);
}
