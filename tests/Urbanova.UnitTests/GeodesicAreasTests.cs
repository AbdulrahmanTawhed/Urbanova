using FluentAssertions;
using Urbanova.Domain.ValueObjects;

namespace Urbanova.UnitTests;

/// <summary>Spherical-earth areas: independent known values, not a mirror of the implementation.</summary>
public sealed class GeodesicAreasTests
{
    private static IReadOnlyList<NormalizedPoint> Ring(params (double lon, double lat)[] pts) =>
        [.. pts.Select(p => new NormalizedPoint(p.lon, p.lat))];

    [Fact]
    public void OneDegreeSquare_AtEquator_IsAbout12364Km2()
    {
        // Spherical reference: R²·Δλ·(sin1°−sin0°) ≈ 1.23637e10 m².
        var area = GeodesicAreas.RingAreaM2(Ring((0, 0), (1, 0), (1, 1), (0, 1), (0, 0)));
        area.Should().BeApproximately(1.2364e10, 1e8);
    }

    [Fact]
    public void Triangle_IsRoughlyHalfTheSquare()
    {
        var area = GeodesicAreas.RingAreaM2(Ring((0, 0), (1, 0), (0, 1), (0, 0)));
        area.Should().BeApproximately(6.18e9, 2e8);
    }

    [Fact]
    public void Polygon_SubtractsHoles()
    {
        var outer = Ring((0, 0), (2, 0), (2, 2), (0, 2), (0, 0));
        var hole = Ring((0, 0), (1, 0), (1, 1), (0, 1), (0, 0));
        var withHole = GeodesicAreas.PolygonAreaM2([outer, hole]);
        var full = GeodesicAreas.PolygonAreaM2([outer]);
        withHole.Should().BeApproximately(full - GeodesicAreas.RingAreaM2(hole), 1.0);
        withHole.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Area_ShrinksWithLatitude()
    {
        var equatorial = GeodesicAreas.RingAreaM2(Ring((0, 0), (1, 0), (1, 1), (0, 1), (0, 0)));
        var cairo = GeodesicAreas.RingAreaM2(Ring((31, 30), (32, 30), (32, 31), (31, 31), (31, 30)));
        cairo.Should().BeLessThan(equatorial);
        cairo.Should().BeApproximately(equatorial * Math.Cos(30.5 * Math.PI / 180), equatorial * 0.05);
    }

    [Fact]
    public void DegenerateRings_Throw()
    {
        var act1 = () => GeodesicAreas.RingAreaM2(Ring((0, 0), (1, 1)));
        act1.Should().Throw<ArgumentException>();
        var act2 = () => GeodesicAreas.PolygonAreaM2([]);
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WindingDirection_DoesNotMatter()
    {
        var cw = GeodesicAreas.RingAreaM2(Ring((0, 0), (1, 0), (1, 1), (0, 1), (0, 0)));
        var ccw = GeodesicAreas.RingAreaM2(Ring((0, 0), (0, 1), (1, 1), (1, 0), (0, 0)));
        ccw.Should().BeApproximately(cw, 1.0);
    }
}
