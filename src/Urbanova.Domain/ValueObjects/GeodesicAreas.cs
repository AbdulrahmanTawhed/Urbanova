namespace Urbanova.Domain.ValueObjects;

/// <summary>
/// Spherical-earth polygon areas for EPSG:4326 (lon/lat degrees) geometries.
/// The extraction pipeline stores planar CRS units (deg²) — meaningless as m² —
/// so cost derivation converts here instead of relabeling. Chamberlain–Duquette
/// summation (exact on a sphere; ~0.3% off the WGS84 ellipsoid at urban scales).
/// An ellipsoidal replacement can swap this single helper without touching callers.
/// </summary>
public static class GeodesicAreas
{
    /// <summary>Mean earth radius (m). Not a domain assumption — a physical constant.</summary>
    public const double EarthRadiusM = 6_371_000;

    /// <summary>Area of one closed ring in m² (absolute value; points are lon/lat degrees).</summary>
    public static double RingAreaM2(IReadOnlyList<NormalizedPoint> ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        if (ring.Count < 4)
            throw new ArgumentException("A closed ring needs at least 4 positions.", nameof(ring));

        var sum = 0.0;
        for (var i = 0; i < ring.Count - 1; i++)
        {
            var lon1 = ToRadians(ring[i].X);
            var lon2 = ToRadians(ring[i + 1].X);
            var lat1 = ToRadians(ring[i].Y);
            var lat2 = ToRadians(ring[i + 1].Y);
            if (!double.IsFinite(lon1) || !double.IsFinite(lon2) || !double.IsFinite(lat1) || !double.IsFinite(lat2))
                throw new ArgumentException("Ring positions must be finite numbers.", nameof(ring));
            sum += (lon2 - lon1) * (Math.Sin(lat1) + Math.Sin(lat2));
        }
        return Math.Abs(sum * EarthRadiusM * EarthRadiusM / 2);
    }

    /// <summary>Polygon area in m²: outer ring minus holes.</summary>
    public static double PolygonAreaM2(IReadOnlyList<IReadOnlyList<NormalizedPoint>> rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        if (rings.Count == 0)
            throw new ArgumentException("A polygon needs at least one ring.", nameof(rings));
        return Math.Max(RingAreaM2(rings[0]) - rings.Skip(1).Sum(RingAreaM2), 0);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
