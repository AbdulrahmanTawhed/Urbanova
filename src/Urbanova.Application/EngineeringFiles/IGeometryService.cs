namespace Urbanova.Application.EngineeringFiles;

/// <summary>
/// Geometry use cases (Phase 6). Requires a Valid file; extraction failures throw
/// <see cref="FileException"/> GEOMETRY_EXTRACTION_FAILED so analysis never runs
/// on incomplete geometry (PRD §19). Result is stateless — Phase 7 persists the
/// snapshot on AnalysisRun.
/// </summary>
public interface IGeometryService
{
    Task<GeometryResponse> ExtractAsync(Guid ownerId, Guid fileId, CancellationToken ct = default);
}
