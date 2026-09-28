using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Urbanova.Application.Costing;
using Urbanova.Application.EngineeringFiles;
using Urbanova.Domain;
using Urbanova.Domain.BusinessRules;
using Urbanova.Domain.Costing;
using Urbanova.Domain.Entities;
using Urbanova.Domain.ValueObjects;
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
    IProcessorRegistry registry,
    IFileStorage storage,
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

        // PRD v0.2 §18: quantity is traceable — user-provided, or derived from the
        // recommendation's analyzed polygon area for area-based (m²) interventions.
        // Anything else is rejected rather than invented.
        var (quantity, quantitySource) = await ResolveQuantityAsync(request, recommendation, ct);
        var estimate = new CostEstimate
        {
            ProjectId = projectId,
            RecommendationId = request.RecommendationId,
            ScenarioId = request.ScenarioId,
            Quantity = quantity,
            QuantitySource = quantitySource,
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
    private async Task<(decimal Quantity, string Source)> ResolveQuantityAsync(
        CreateCostEstimateRequest request, Recommendation? recommendation, CancellationToken ct)
    {
        if (request.Quantity is not null)
            return (request.Quantity.Value, "UserProvided");

        // Derive from geometry: re-extract the analyzed file's rings (lon/lat degrees)
        // and compute geodesic m². Never relabels the stored planar deg² area as m².
        if (recommendation is not null
            && recommendation.AnalysisRunId is not null
            && IsSquareMetres(request.Unit))
        {
            var areaM2 = await GeodesicPolygonAreaM2Async(recommendation, ct);
            if (areaM2 is not null)
            {
                var quantity = Math.Round(areaM2.Value, 2);
                // A valid non-zero polygon must never collapse to zero: rounding that
                // small must reject instead of storing a fabricated zero.
                if (quantity > 0)
                    return ((decimal)quantity, "DerivedFromGeometry");
            }
        }

        throw CostException.Invalid(
            "Quantity is required. Omit it only with a RecommendationId for area-based " +
            "interventions (unit m2, m², m^2 or sqm) backed by analyzable EPSG:4326 geometry.");
    }

    /// <summary>
    /// Re-extracts the recommendation's analyzed file and returns the geodesic m² area
    /// of the targeted polygon, or null when geometry is missing, non-WGS84, or unmatched.
    /// </summary>
    private async Task<double?> GeodesicPolygonAreaM2Async(Recommendation recommendation, CancellationToken ct)
    {
        var run = await db.AnalysisRuns.SingleOrDefaultAsync(r => r.Id == recommendation.AnalysisRunId, ct);
        var file = run?.EngineeringFileId is null
            ? null
            : await db.EngineeringFiles.SingleOrDefaultAsync(f => f.Id == run.EngineeringFileId, ct);
        if (file is null)
            return null;

        var processor = registry.FindByFormat(file.FormatDetected ?? "")
            ?? registry.Find(file.FileName, file.ContentType);
        if (processor is null)
            return null;

        NormalizedGeometry geometry;
        try
        {
            await using var stream = await storage.OpenReadAsync(file.StoragePath, ct);
            var extraction = await processor.ExtractGeometryAsync(stream, file.FileName, ct);
            if (!extraction.Success || extraction.Geometry is null)
                return null;
            geometry = await processor.NormalizeGeometryAsync(extraction.Geometry, ct);
        }
        catch (FileException)
        {
            // Stored content gone or unreadable → not derivable; caller rejects.
            return null;
        }

        // Only lon/lat-degree geometries can feed the spherical computation.
        if (!geometry.Crs.Equals("EPSG:4326", StringComparison.OrdinalIgnoreCase))
            return null;
        var polygon = geometry.Polygons
            .Select((p, i) => (Polygon: p, Index: i))
            .FirstOrDefault(t => t.Index == recommendation.PolygonIndex)
            .Polygon;
        if (polygon is null)
            return null;
        try
        {
            return GeodesicAreas.PolygonAreaM2(polygon.Rings);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
    /// <summary>Area-unit spellings accepted for geometry-derived quantities.</summary>
    private static bool IsSquareMetres(string unit)
    {
        var u = unit.Trim().ToLowerInvariant();
        return u is "m2" or "m²" or "m^2" or "sqm";
    }


    private static CostEstimateResponse ToResponse(CostEstimate e) => new(
        e.Id, e.ProjectId, e.RecommendationId, e.ScenarioId,
        e.Quantity, e.QuantitySource, e.Unit, e.UnitPrice, e.Currency, e.PriceSource,
        e.Total, e.Status.ToString(), e.CreatedAt);
}
