using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Urbanova.Application.Costing;
using Urbanova.Domain;
using Urbanova.Domain.BusinessRules;
using Urbanova.Domain.Costing;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.Costing;

/// <summary>
/// Cost use cases (Phase 11). Direct price → Calculated via <see cref="CostRules"/>;
/// catalog code → catalog price or stored Unavailable row (never invented);
/// neither → 400. Linked recommendations get CostEstimateId back-filled.
/// </summary>
public sealed class CostService(
    AppDbContext db,
    IPriceCatalog catalog,
    IValidator<CreateCostEstimateRequest> validator) : ICostService
{
    public async Task<CostEstimateResponse> CreateAsync(
        Guid ownerId, Guid projectId, CreateCostEstimateRequest request, CancellationToken ct = default)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            throw CostException.Invalid(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

        var owner = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct)
            ?? throw CostException.NotFound(projectId);
        if (owner != ownerId)
            throw CostException.Forbidden();

        if (request is { UnitPrice: null, ItemCode: null })
            throw CostException.Invalid("Provide UnitPrice directly or an ItemCode for catalog lookup.");

        Recommendation? recommendation = null;
        if (request.RecommendationId is not null)
        {
            recommendation = await db.Recommendations.SingleOrDefaultAsync(r => r.Id == request.RecommendationId, ct)
                ?? throw CostException.NotFound(request.RecommendationId.Value);
            if (recommendation.ProjectId != projectId)
                throw CostException.NotFound(request.RecommendationId.Value);
            // Reject re-linking: silently replacing CostEstimateId would orphan the prior
            // estimate while it still counts toward comparison/report totals.
            if (recommendation.CostEstimateId is not null)
                throw CostException.Invalid(
                    $"Recommendation '{recommendation.Id}' already links cost estimate " +
                    $"'{recommendation.CostEstimateId}'. Delete or supersede it explicitly instead.");
        }
        if (request.ScenarioId is not null)
        {
            var scenarioProject = await db.Scenarios
                .Where(s => s.Id == request.ScenarioId)
                .Select(s => (Guid?)s.ProjectId)
                .SingleOrDefaultAsync(ct)
                ?? throw CostException.NotFound(request.ScenarioId.Value);
            if (scenarioProject != projectId)
                throw CostException.NotFound(request.ScenarioId.Value);
        }

        var estimate = new CostEstimate
        {
            ProjectId = projectId,
            RecommendationId = request.RecommendationId,
            ScenarioId = request.ScenarioId,
            Quantity = request.Quantity,
            Unit = request.Unit.Trim(),
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency!.Trim().ToUpperInvariant(),
            CreatedBy = ownerId,
        };

        if (request.UnitPrice is not null)
        {
            estimate.UnitPrice = request.UnitPrice.Value;
            estimate.PriceSource = "user-provided";
            CostRules.ApplyCalculation(estimate);
        }
        else
        {
            var lookup = await catalog.TryGetPriceAsync(request.ItemCode!.Trim(), ct);
            if (!lookup.Found || lookup.Price is null)
            {
                estimate.UnitPrice = 0;
                estimate.PriceSource = $"catalog-miss:{request.ItemCode!.Trim()}";
                estimate.Total = 0;
                estimate.Status = CostStatus.Unavailable;
            }
            else
            {
                estimate.UnitPrice = lookup.Price.UnitPrice;
                estimate.PriceSource = lookup.Price.Source;
                CostRules.ApplyCalculation(estimate);
            }
        }

        db.CostEstimates.Add(estimate);
        if (recommendation is not null)
            recommendation.CostEstimateId = estimate.Id;
        await db.SaveChangesAsync(ct);

        db.ChangeTracker.Clear();
        var row = await db.CostEstimates.SingleAsync(e => e.Id == estimate.Id, ct);
        return ToResponse(row);
    }

    public async Task<IReadOnlyList<CostEstimateResponse>> ListAsync(
        Guid ownerId, Guid projectId, CancellationToken ct = default)
    {
        var owner = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct)
            ?? throw CostException.NotFound(projectId);
        if (owner != ownerId)
            throw CostException.Forbidden();

        var rows = await db.CostEstimates
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
        return [.. rows.Select(ToResponse)];
    }

    public async Task<CostEstimateResponse> GetAsync(Guid ownerId, Guid estimateId, CancellationToken ct = default)
    {
        var row = await db.CostEstimates.SingleOrDefaultAsync(e => e.Id == estimateId, ct)
            ?? throw CostException.NotFound(estimateId);
        var owner = await db.Projects
            .Where(p => p.Id == row.ProjectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct);
        if (owner is null || owner != ownerId)
            throw owner is null ? CostException.NotFound(estimateId) : CostException.Forbidden();
        return ToResponse(row);
    }

    private static CostEstimateResponse ToResponse(CostEstimate e) => new(
        e.Id, e.ProjectId, e.RecommendationId, e.ScenarioId,
        e.Quantity, e.Unit, e.UnitPrice, e.Currency, e.PriceSource,
        e.Total, e.Status.ToString(), e.CreatedAt);
}
