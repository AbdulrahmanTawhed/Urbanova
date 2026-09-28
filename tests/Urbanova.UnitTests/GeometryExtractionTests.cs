using System.Text;
using FluentAssertions;
using Urbanova.Domain.ValueObjects;
using Urbanova.Infrastructure.FileProcessing;

namespace Urbanova.UnitTests;

/// <summary>Phase 6: extraction math + normalization. Pure streams — no DB.</summary>
public sealed class GeometryExtractionTests
{
    private readonly GeoJsonFileProcessor _processor = new();

    private static Stream Bytes(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    private const string UnitSquare = """
        { "type": "FeatureCollection", "features": [
          { "type": "Feature", "properties": {},
            "geometry": { "type": "Polygon",
              "coordinates": [[[0,0],[1,0],[1,1],[0,1],[0,0]]] } } ] }
        """;

    private async Task<NormalizedGeometry> ExtractNormalizeAsync(string json)
    {
        var extraction = await _processor.ExtractGeometryAsync(Bytes(json), "g.geojson");
        extraction.Success.Should().BeTrue($"extraction failed: {string.Join("; ", extraction.Errors)}");
        return await _processor.NormalizeGeometryAsync(extraction.Geometry!, CancellationToken.None);
    }

    [Fact]
    public async Task UnitSquare_AreaIsOne_WithBboxAndCrs()
    {
        var g = await ExtractNormalizeAsync(UnitSquare);
        g.Crs.Should().Be("EPSG:4326");
        g.AreaUnit.Should().Be("deg²");
        g.TotalArea.Should().BeApproximately(1.0, 1e-9);
        g.Polygons.Should().HaveCount(1);
        var p = g.Polygons[0];
        (p.MinX, p.MinY, p.MaxX, p.MaxY).Should().Be((0, 0, 1, 1));
    }

    [Fact]
    public async Task Triangle_AreaIsCorrect()
    {
        const string triangle = """
            { "type": "Feature", "properties": {},
              "geometry": { "type": "Polygon",
                "coordinates": [[[0,0],[4,0],[0,3],[0,0]]] } }
            """;
        (await ExtractNormalizeAsync(triangle)).TotalArea.Should().BeApproximately(6.0, 1e-9);
    }

    [Fact]
    public async Task UnclosedRing_GetsClosed()
    {
        const string open = """
            { "type": "Feature", "properties": {},
              "geometry": { "type": "Polygon",
                "coordinates": [[[0,0],[2,0],[2,2],[0,2]]] } }
            """;
        var g = await ExtractNormalizeAsync(open);
        g.TotalArea.Should().BeApproximately(4.0, 1e-9);
        var ring = g.Polygons[0].Rings[0];
        ring.Should().HaveCount(5);
        ring[0].Should().Be(ring[^1]);
    }

    [Fact]
    public async Task PolygonWithHole_SubtractsHole()
    {
        const string withHole = """
            { "type": "Feature", "properties": {},
              "geometry": { "type": "Polygon", "coordinates": [
                [[0,0],[4,0],[4,4],[0,4],[0,0]],
                [[1,1],[2,1],[2,2],[1,2],[1,1]] ] } }
            """;
        // Outer 16 minus hole 1.
        (await ExtractNormalizeAsync(withHole)).TotalArea.Should().BeApproximately(15.0, 1e-9);
    }

    [Fact]
    public async Task MultiPolygon_SumsAreas_AndSkipsPoints()
    {
        const string mixed = """
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": {},
                "geometry": { "type": "Polygon",
                  "coordinates": [[[0,0],[1,0],[1,1],[0,1],[0,0]]] } },
              { "type": "Feature", "properties": {},
                "geometry": { "type": "MultiPolygon", "coordinates": [
                  [ [[10,10],[11,10],[11,11],[10,10]] ],
                  [ [[20,20],[22,20],[22,22],[20,20]] ]
                ] } },
              { "type": "Feature", "properties": {},
                "geometry": { "type": "Point", "coordinates": [5,5] } } ] }
            """;
        var g = await ExtractNormalizeAsync(mixed);
        g.Polygons.Should().HaveCount(3);
        g.TotalArea.Should().BeApproximately(1.0 + 0.5 + 2.0, 1e-9);
        g.SkippedNonPolygonalFeatures.Should().Be(1);
        g.FeatureCount.Should().Be(3);
    }

    [Fact]
    public async Task ZCoordinates_Ignored()
    {
        const string withZ = """
            { "type": "Feature", "properties": {},
              "geometry": { "type": "Polygon",
                "coordinates": [[[0,0,9],[1,0,9],[1,1,9],[0,0,9]]] } }
            """;
        (await ExtractNormalizeAsync(withZ)).TotalArea.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public async Task PointOnlyCollection_FailsWithExplanation()
    {
        const string points = """
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": {},
                "geometry": { "type": "Point", "coordinates": [1,2] } } ] }
            """;
        var result = await _processor.ExtractGeometryAsync(Bytes(points), "p.geojson");
        result.Success.Should().BeFalse();
        result.Geometry.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Contains("No polygonal geometries"));
    }

    [Fact]
    public async Task Normalize_PreservesExplicitCrs()
    {
        var raw = new NormalizedGeometry("EPSG:3857", "", [], 0, 0, 0);
        var normalized = await _processor.NormalizeGeometryAsync(raw, CancellationToken.None);
        normalized.Crs.Should().Be("EPSG:3857");
        normalized.AreaUnit.Should().Be("crs-units²");
    }
}
