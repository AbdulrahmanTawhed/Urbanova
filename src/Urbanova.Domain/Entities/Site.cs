using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>1:1 with Project. Boundary stored as GeoJSON (nvarchar(max)); CRS defaults to EPSG:4326.</summary>
public sealed class Site : EntityBase
{
    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public string? Address { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>GeoJSON geometry string. Flexible by design — validated in Phase 6.</summary>
    public string? BoundaryGeoJson { get; set; }

    public string Crs { get; set; } = "EPSG:4326";

    public double? AreaM2 { get; set; }
}
