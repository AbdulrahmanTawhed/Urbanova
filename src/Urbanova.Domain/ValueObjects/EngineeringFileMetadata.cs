namespace Urbanova.Domain.ValueObjects;

/// <summary>
/// Format metadata persisted as JSON on EngineeringFile.MetadataJson.
/// All members optional — processors fill what the format actually provides.
/// </summary>
public sealed record EngineeringFileMetadata(
    int? FeatureCount,
    IReadOnlyList<string>? GeometryTypes,
    string? Crs,
    bool HasBoundingBox,
    long ContentBytes);
