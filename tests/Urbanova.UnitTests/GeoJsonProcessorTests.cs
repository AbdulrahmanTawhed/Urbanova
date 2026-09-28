using System.Text;
using FluentAssertions;
using Urbanova.Infrastructure.FileProcessing;

namespace Urbanova.UnitTests;

/// <summary>Phase 5: GeoJSON processor + registry. No DB — stream in, result out.</summary>
public sealed class GeoJsonProcessorTests
{
    private const string ValidCollection = """
        {
          "type": "FeatureCollection",
          "bbox": [13.0, 52.0, 14.0, 53.0],
          "features": [
            { "type": "Feature",
              "properties": { "name": "Block A" },
              "geometry": { "type": "Polygon",
                "coordinates": [[[13.0, 52.0], [14.0, 52.0], [14.0, 53.0], [13.0, 52.0]]] } },
            { "type": "Feature",
              "properties": { "name": "Block B" },
              "geometry": { "type": "MultiPolygon",
                "coordinates": [[[[13.1, 52.1], [13.2, 52.1], [13.2, 52.2], [13.1, 52.1]]]] } }
          ]
        }
        """;

    private const string ValidFeature = """
        { "type": "Feature",
          "properties": {},
          "geometry": { "type": "Point", "coordinates": [13.4, 52.5] } }
        """;

    private static Stream Bytes(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    private readonly GeoJsonFileProcessor _processor = new();

    [Fact]
    public async Task ValidCollection_Passes_WithMetadata()
    {
        var result = await _processor.ValidateAsync(Bytes(ValidCollection), "site.geojson");
        result.IsValid.Should().BeTrue();
        result.FormatDetected.Should().Be("GeoJSON");
        result.Errors.Should().BeEmpty();

        var meta = await _processor.GetMetadataAsync(Bytes(ValidCollection), "site.geojson");
        meta.FeatureCount.Should().Be(2);
        meta.GeometryTypes.Should().BeEquivalentTo(["Polygon", "MultiPolygon"]);
        meta.HasBoundingBox.Should().BeTrue();
    }

    [Fact]
    public async Task ValidFeature_Passes()
    {
        (await _processor.ValidateAsync(Bytes(ValidFeature), "point.json")).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("not json at all", "Content is not valid JSON.")]
    [InlineData("", "File is empty.")]
    [InlineData("""{"type":"Topology"}""", "root.type must be 'FeatureCollection' or 'Feature'.")]
    [InlineData("""{"type":"FeatureCollection"}""", "FeatureCollection.features must be an array.")]
    [InlineData("""{"type":"FeatureCollection","features":[]}""", "must not be empty")]
    [InlineData("""{"type":"Feature","properties":{},"geometry":null}""", "geometry is required")]
    [InlineData("""{"type":"Feature","properties":{},"geometry":{"type":"Circle","coordinates":[1,2]}}""", "geometry.type must be one of")]
    [InlineData("""{"type":"Feature","properties":{},"geometry":{"type":"Point","coordinates":["a","b"]}}""", "coordinates must be a non-empty array")]
    [InlineData("""{"type":"Feature","properties":{},"geometry":{"type":"Point","coordinates":[]}}""", "coordinates must be a non-empty array")]
    public async Task InvalidContent_FailsWithExplanation(string content, string expectedFragment)
    {
        var result = await _processor.ValidateAsync(Bytes(content), "site.geojson");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains(expectedFragment));
    }

    [Theory]
    [InlineData("site.geojson", "application/octet-stream", true)]   // by extension
    [InlineData("upload.bin", "application/geo+json", true)]         // by content type
    [InlineData("upload.bin", "application/json", true)]             // by content type
    [InlineData("plan.exe", "application/octet-stream", false)]
    [InlineData("plan.dxf", "application/dxf", false)]
    public void CanProcess_MatchesExtensionOrContentType(string name, string contentType, bool expected)
    {
        _processor.CanProcess(name, contentType).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "application/geo+json")]
    [InlineData("", "application/geo+json")]
    [InlineData("plan.bin", null)]
    [InlineData("plan.bin", "")]
    public void CanProcess_NullInputs_ReturnsFalse(string? name, string? contentType)
    {
        // Regression: contentType.Split threw NullReferenceException (500) on null.
        // (A matching .geojson extension still wins regardless of content type.)
        _processor.CanProcess(name!, contentType!).Should().BeFalse();
    }

    [Fact]
    public void Registry_FindsGeoJson_AndRejectsUnknown_WithSupportedList()
    {
        var registry = new ProcessorRegistry([new GeoJsonFileProcessor()]);
        registry.Find("site.geojson", "application/octet-stream")!.FormatName.Should().Be("GeoJSON");
        registry.Find("plan.dxf", "application/dxf").Should().BeNull();
        registry.FindByFormat("geojson")!.FormatName.Should().Be("GeoJSON");
        registry.SupportedFormats.Should().ContainSingle(s => s.Contains("GeoJSON"));
    }
}
