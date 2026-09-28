namespace Urbanova.Domain.ValueObjects;

/// <summary>
/// Raw extraction outcome. Failure carries explanations (PRD §19: stop analysis + explain)
/// instead of throwing — the service maps it to GEOMETRY_EXTRACTION_FAILED (422).
/// </summary>
public sealed record GeometryExtractionResult(
    bool Success,
    NormalizedGeometry? Geometry,
    IReadOnlyList<string> Errors)
{
    public static GeometryExtractionResult Ok(NormalizedGeometry geometry) => new(true, geometry, []);

    public static GeometryExtractionResult Fail(params string[] errors) => new(false, null, errors);
}
