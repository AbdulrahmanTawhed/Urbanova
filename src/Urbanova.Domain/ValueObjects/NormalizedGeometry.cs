namespace Urbanova.Domain.ValueObjects;

/// <summary>
/// Format-independent geometry (PRD §7). The environmental-analysis layer consumes
/// ONLY this type — never raw file content — so formats swap without touching analysis.
/// MVP limits (see phase-06 doc): polygons only (points/lines counted as skipped);
/// areas are planar CRS units (deg² for EPSG:4326), NOT square meters.
/// </summary>
public sealed record NormalizedPoint(double X, double Y);

public sealed record NormalizedPolygon(
    IReadOnlyList<IReadOnlyList<NormalizedPoint>> Rings,
    double Area,
    double MinX, double MinY, double MaxX, double MaxY);

public sealed record NormalizedGeometry(
    string Crs,
    string AreaUnit,
    IReadOnlyList<NormalizedPolygon> Polygons,
    double TotalArea,
    int FeatureCount,
    int SkippedNonPolygonalFeatures);
