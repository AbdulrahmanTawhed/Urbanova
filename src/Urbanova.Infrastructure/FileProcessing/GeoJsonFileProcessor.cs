using System.Text.Json;
using Urbanova.Domain.Interfaces;
using Urbanova.Domain.ValueObjects;

namespace Urbanova.Infrastructure.FileProcessing;

/// <summary>
/// MVP GeoJSON processor (.geojson/.json). Validates structure (types, features,
/// geometries, coordinate arrays) and extracts counts/types/CRS hints.
/// MVP limits (see phase-05 doc): FeatureCollection + Feature only; null geometries
/// rejected (analysis needs geometry); ring-closure/deep CRS validation deferred to Phase 6.
/// </summary>
public sealed class GeoJsonFileProcessor : IEngineeringFileProcessor
{
    public string FormatName => "GeoJSON";

    public IReadOnlyList<string> SupportedExtensions => [".geojson", ".json"];

    public IReadOnlyList<string> SupportedContentTypes => ["application/geo+json", "application/json"];

    private static readonly HashSet<string> RootTypes = new(StringComparer.Ordinal)
        { "FeatureCollection", "Feature" };

    private static readonly HashSet<string> GeometryTypes = new(StringComparer.Ordinal)
        { "Point", "MultiPoint", "LineString", "MultiLineString", "Polygon", "MultiPolygon" };

    public bool CanProcess(string fileName, string contentType)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        var ext = Path.GetExtension(fileName);
        if (SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(contentType))
            return false;
        return SupportedContentTypes.Contains(contentType.Split(';')[0].Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<FileValidationResult> ValidateAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        JsonDocument doc;
        try
        {
            doc = await ParseAsync(content, ct);
        }
        catch (JsonException)
        {
            return FileValidationResult.Invalid(FormatName, "Content is not valid JSON.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("empty", StringComparison.OrdinalIgnoreCase))
        {
            return FileValidationResult.Invalid(FormatName, "File is empty.");
        }
        using (doc)
        {
            var errors = new List<string>();
            ValidateRoot(doc.RootElement, errors);
            return errors.Count == 0
                ? FileValidationResult.Valid(FormatName)
                : FileValidationResult.Invalid(FormatName, [.. errors]);
        }
    }

    public async Task<EngineeringFileMetadata> GetMetadataAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        using var doc = await ParseAsync(content, ct);
        var root = doc.RootElement;

        var types = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        if (IsType(root, "FeatureCollection") && root.TryGetProperty("features", out var features)
            && features.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in features.EnumerateArray())
            {
                count++;
                var g = GeometryOf(f);
                if (g is { } geom && geom.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                    types.Add(t.GetString()!);
            }
        }
        else if (IsType(root, "Feature"))
        {
            count = 1;
            var g = GeometryOf(root);
            if (g is { } geom && geom.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                types.Add(t.GetString()!);
        }

        return new EngineeringFileMetadata(
            FeatureCount: count,
            GeometryTypes: [.. types],
            Crs: ExtractCrs(root),
            HasBoundingBox: root.TryGetProperty("bbox", out _),
            ContentBytes: content.CanSeek ? content.Length : -1);
    }

    public async Task<GeometryExtractionResult> ExtractGeometryAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        JsonDocument doc;
        try
        {
            doc = await ParseAsync(content, ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return GeometryExtractionResult.Fail($"Content cannot be parsed as GeoJSON: {ex.Message}");
        }
        using (doc)
        {
            var root = doc.RootElement;
            var polygons = new List<NormalizedPolygon>();
            var errors = new List<string>();
            var features = 0;
            var skipped = 0;

            void AddFeature(JsonElement feature, string path)
            {
                features++;
                var g = GeometryOf(feature);
                if (g is null)
                {
                    errors.Add($"{path}.geometry is missing.");
                    return;
                }
                var type = g.Value.GetProperty("type").GetString()!;
                if (type is not ("Polygon" or "MultiPolygon"))
                {
                    skipped++; // points/lines carry no area for heat analysis
                    return;
                }
                if (!g.Value.TryGetProperty("coordinates", out var coords))
                {
                    errors.Add($"{path}.geometry.coordinates is missing.");
                    return;
                }
                try
                {
                    if (type == "Polygon")
                        polygons.Add(ReadPolygon(coords, path));
                    else
                        foreach (var poly in coords.EnumerateArray())
                            polygons.Add(ReadPolygon(poly, path));
                }
                catch (FormatException ex)
                {
                    errors.Add($"{path}.geometry.coordinates are malformed: {ex.Message}");
                }
            }

            if (IsType(root, "FeatureCollection") && root.TryGetProperty("features", out var list)
                && list.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var f in list.EnumerateArray())
                    AddFeature(f, $"features[{i++}]");
            }
            else if (IsType(root, "Feature"))
            {
                AddFeature(root, "feature");
            }
            else
            {
                return GeometryExtractionResult.Fail("root.type must be 'FeatureCollection' or 'Feature'.");
            }

            if (errors.Count > 0)
                return GeometryExtractionResult.Fail([.. errors]);
            if (polygons.Count == 0)
                return GeometryExtractionResult.Fail(
                    "No polygonal geometries found; heat analysis requires at least one Polygon/MultiPolygon.");

            // Raw: areas/bboxes recomputed by NormalizeGeometryAsync (totals zeroed here by design).
            var raw = new NormalizedGeometry(
                Crs: ExtractCrs(root) ?? string.Empty,
                AreaUnit: string.Empty,
                Polygons: [.. polygons],
                TotalArea: 0,
                FeatureCount: features,
                SkippedNonPolygonalFeatures: skipped);
            return GeometryExtractionResult.Ok(raw);
        }
    }

    public Task<NormalizedGeometry> NormalizeGeometryAsync(NormalizedGeometry raw, CancellationToken ct = default)
    {
        var crs = string.IsNullOrWhiteSpace(raw.Crs) ? "EPSG:4326" : raw.Crs;
        var unit = crs == "EPSG:4326" ? "deg²" : "crs-units²";

        var polygons = raw.Polygons.Select(p =>
        {
            var rings = p.Rings.Select(CloseRing).ToList();
            var area = Math.Abs(RingArea(rings[0])) - rings.Skip(1).Sum(r => Math.Abs(RingArea(r)));
            var xs = rings.SelectMany(r => r).Select(pt => pt.X).ToList();
            var ys = rings.SelectMany(r => r).Select(pt => pt.Y).ToList();
            return new NormalizedPolygon(
                [.. rings], Math.Max(area, 0),
                xs.Min(), ys.Min(), xs.Max(), ys.Max());
        }).ToList();

        return Task.FromResult(new NormalizedGeometry(
            crs, unit, polygons, polygons.Sum(p => p.Area),
            raw.FeatureCount, raw.SkippedNonPolygonalFeatures));
    }

    private static NormalizedPolygon ReadPolygon(JsonElement coords, string path)
    {
        if (coords.ValueKind != JsonValueKind.Array || coords.GetArrayLength() == 0)
            throw new FormatException("polygon has no rings.");
        var rings = new List<IReadOnlyList<NormalizedPoint>>();
        foreach (var ring in coords.EnumerateArray())
        {
            if (ring.ValueKind != JsonValueKind.Array || ring.GetArrayLength() < 4)
                throw new FormatException("each ring needs at least 4 positions.");
            var pts = new List<NormalizedPoint>();
            foreach (var pos in ring.EnumerateArray())
            {
                if (pos.ValueKind != JsonValueKind.Array || pos.GetArrayLength() < 2)
                    throw new FormatException("each position needs at least 2 numbers.");
                var nums = pos.EnumerateArray().Take(2).ToList();
                if (nums.Any(n => n.ValueKind != JsonValueKind.Number))
                    throw new FormatException("positions must be numbers.");
                var x = nums[0].GetDouble();
                var y = nums[1].GetDouble();
                if (!double.IsFinite(x) || !double.IsFinite(y))
                    throw new FormatException("positions must be finite numbers.");
                pts.Add(new NormalizedPoint(x, y));
            }
            rings.Add(pts);
        }
        // Placeholder zeros — NormalizeGeometryAsync closes rings and recomputes.
        return new NormalizedPolygon(rings, 0, 0, 0, 0, 0);
    }

    private static IReadOnlyList<NormalizedPoint> CloseRing(IReadOnlyList<NormalizedPoint> ring)
    {
        var first = ring[0];
        var last = ring[^1];
        if (Math.Abs(first.X - last.X) < 1e-12 && Math.Abs(first.Y - last.Y) < 1e-12)
            return ring;
        return [.. ring, first];
    }

    /// <summary>Planar shoelace area in CRS units (signed; caller takes abs).</summary>
    private static double RingArea(IReadOnlyList<NormalizedPoint> ring)
    {
        var sum = 0.0;
        for (var i = 0; i < ring.Count - 1; i++)
            sum += ring[i].X * ring[i + 1].Y - ring[i + 1].X * ring[i].Y;
        return sum / 2;
    }

    private static async Task<JsonDocument> ParseAsync(Stream content, CancellationToken ct)
    {
        if (content.CanSeek)
            content.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(content, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("File is empty.");
        return JsonDocument.Parse(text);
    }

    private static void ValidateRoot(JsonElement root, List<string> errors)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Root must be a JSON object.");
            return;
        }
        if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
            || !RootTypes.Contains(type.GetString()!))
        {
            errors.Add("root.type must be 'FeatureCollection' or 'Feature'.");
            return;
        }
        if (type.GetString() == "FeatureCollection")
            ValidateCollection(root, errors);
        else
            ValidateFeature(root, "feature", errors);
    }

    private static void ValidateCollection(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            errors.Add("FeatureCollection.features must be an array.");
            return;
        }
        var count = 0;
        foreach (var f in features.EnumerateArray())
        {
            ValidateFeature(f, $"features[{count}]", errors);
            count++;
            if (count >= 100 && errors.Count > 20)
            {
                errors.Add("Too many invalid features; stopping after 100.");
                break;
            }
        }
        if (count == 0)
            errors.Add("FeatureCollection.features must not be empty.");
    }

    private static void ValidateFeature(JsonElement feature, string path, List<string> errors)
    {
        if (feature.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path} must be an object.");
            return;
        }
        if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind == JsonValueKind.Null)
        {
            errors.Add($"{path}.geometry is required (null geometries need analysis input).");
            return;
        }
        if (geometry.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}.geometry must be an object.");
            return;
        }
        if (!geometry.TryGetProperty("type", out var gtype) || gtype.ValueKind != JsonValueKind.String
            || !GeometryTypes.Contains(gtype.GetString()!))
        {
            errors.Add($"{path}.geometry.type must be one of: {string.Join(", ", GeometryTypes)}.");
            return;
        }
        if (!geometry.TryGetProperty("coordinates", out var coords) || !CoordinatesValid(coords))
            errors.Add($"{path}.geometry.coordinates must be a non-empty array of numbers.");
    }

    private static bool CoordinatesValid(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number)
            return true;
        if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() == 0)
            return false;
        foreach (var child in el.EnumerateArray())
        {
            if (child.ValueKind == JsonValueKind.Number)
                continue;
            if (child.ValueKind == JsonValueKind.Array)
            {
                if (!CoordinatesValid(child))
                    return false;
                continue;
            }
            return false;
        }
        return true;
    }

    private static bool IsType(JsonElement el, string type) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty("type", out var t)
        && t.ValueKind == JsonValueKind.String
        && t.GetString() == type;

    private static JsonElement? GeometryOf(JsonElement feature) =>
        feature.ValueKind == JsonValueKind.Object
        && feature.TryGetProperty("geometry", out var g)
        && g.ValueKind == JsonValueKind.Object
            ? g
            : null;

    private static string? ExtractCrs(JsonElement root)
    {
        // Legacy GeoJSON "crs": {"type":"name","properties":{"name":"..."}} — passthrough only.
        if (root.TryGetProperty("crs", out var crs) && crs.ValueKind == JsonValueKind.Object
            && crs.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object
            && props.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            return name.GetString();
        return null;
    }
}
