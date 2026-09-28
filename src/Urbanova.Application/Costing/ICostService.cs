namespace Urbanova.Application.Costing;

/// <summary>Cost use cases (Phase 11). Owner-scoped; throws <see cref="CostException"/>.</summary>
public interface ICostService
{
    Task<CostEstimateResponse> CreateAsync(
        Guid ownerId, Guid projectId, CreateCostEstimateRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<CostEstimateResponse>> ListAsync(
        Guid ownerId, Guid projectId, CancellationToken ct = default);

    Task<CostEstimateResponse> GetAsync(Guid ownerId, Guid estimateId, CancellationToken ct = default);
}
