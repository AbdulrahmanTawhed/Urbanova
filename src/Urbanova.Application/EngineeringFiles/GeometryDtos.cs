namespace Urbanova.Application.EngineeringFiles;

/// <summary>Normalized-geometry DTOs (Phase 6). Coordinates included — MVP transparency over payload size.</summary>
public sealed record GeometryPointDto(double X, double Y);

public sealed record GeometryPolygonDto(
    IReadOnlyList<IReadOnlyList<GeometryPointDto>> Rings,
    double Area,
    double MinX, double MinY, double MaxX, double MaxY);

public sealed record GeometryResponse(
    Guid FileId,
    string FormatDetected,
    string Crs,
    string AreaUnit,
    double TotalArea,
    int PolygonCount,
    int FeatureCount,
    int SkippedNonPolygonalFeatures,
    IReadOnlyList<GeometryPolygonDto> Polygons,
    DateTimeOffset ComputedAt);
