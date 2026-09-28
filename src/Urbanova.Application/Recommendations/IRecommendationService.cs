namespace Urbanova.Application.Recommendations;

/// <summary>
/// Recommendation use cases (Phase 10). Generation is deterministic per run:
/// stored rows are refreshed idempotently, so repeated calls return the same set.
/// </summary>
public interface IRecommendationService
{
    Task<IReadOnlyList<RecommendationResponse>> GetForProjectAsync(
        Guid ownerId, Guid projectId, Guid? runId, CancellationToken ct = default);
}
