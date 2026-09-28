using Microsoft.EntityFrameworkCore;
using Urbanova.Application.EngineeringFiles;
using Urbanova.Domain;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.FileProcessing;

/// <summary>
/// Geometry use cases (Phase 6). Only Valid files extract; failures throw
/// GEOMETRY_EXTRACTION_FAILED so downstream analysis never runs (PRD §19).
/// Stateless — the geometry snapshot is persisted by analysis runs (Phase 7).
/// </summary>
public sealed class GeometryService(
    AppDbContext db,
    IProcessorRegistry registry,
    IFileStorage storage) : IGeometryService
{
    public async Task<GeometryResponse> ExtractAsync(Guid ownerId, Guid fileId, CancellationToken ct = default)
    {
        var file = await db.EngineeringFiles.SingleOrDefaultAsync(f => f.Id == fileId, ct)
            ?? throw FileException.NotFound(fileId);
        var owner = await db.Projects
            .Where(p => p.Id == file.ProjectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct);
        if (owner is null || owner != ownerId)
            throw owner is null ? FileException.NotFound(fileId) : FileException.Forbidden();
        if (file.ValidationStatus != FileValidationStatus.Valid)
            throw FileException.InvalidFile(["File must validate successfully before geometry extraction."]);

        var processor = registry.FindByFormat(file.FormatDetected ?? "")
            ?? registry.Find(file.FileName, file.ContentType)
            ?? throw FileException.UnsupportedFormat(file.FileName, string.Join("; ", registry.SupportedFormats));

        await using var stream = await storage.OpenReadAsync(file.StoragePath, ct);
        var extraction = await processor.ExtractGeometryAsync(stream, file.FileName, ct);
        if (!extraction.Success || extraction.Geometry is null)
            throw FileException.ExtractionFailed(extraction.Errors);

        var normalized = await processor.NormalizeGeometryAsync(extraction.Geometry, ct);
        return new GeometryResponse(
            file.Id, file.FormatDetected ?? processor.FormatName,
            normalized.Crs, normalized.AreaUnit, normalized.TotalArea,
            normalized.Polygons.Count, normalized.FeatureCount, normalized.SkippedNonPolygonalFeatures,
            [.. normalized.Polygons.Select(p => new GeometryPolygonDto(
                [.. p.Rings.Select(r => (IReadOnlyList<GeometryPointDto>)[.. r.Select(pt => new GeometryPointDto(pt.X, pt.Y))])],
                p.Area, p.MinX, p.MinY, p.MaxX, p.MaxY))],
            DateTimeOffset.UtcNow);
    }
}
