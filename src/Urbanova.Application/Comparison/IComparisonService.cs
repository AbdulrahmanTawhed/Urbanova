namespace Urbanova.Application.Comparison;

/// <summary>
/// Comparison use cases (Phase 9). Compares the latest succeeded run of each scenario;
/// scenarios without analysis are rejected (400) rather than compared on emptiness.
/// </summary>
public interface IComparisonService
{
    Task<ComparisonResponse> CompareAsync(
        Guid ownerId, Guid projectId, Guid baselineId, Guid alternativeId,
        CancellationToken ct = default);
}
