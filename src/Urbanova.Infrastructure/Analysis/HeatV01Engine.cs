using Microsoft.Extensions.Options;
using Urbanova.Domain.Analysis;

namespace Urbanova.Infrastructure.Analysis;

/// <summary>
/// MVP heat engine (method v0.1-weighted-area). Pure + deterministic:
/// value = base − veg·kVeg − shade·kShade + (albedoRef − albedo)·kAlbedo.
/// All constants from <see cref="HeatAnalysisOptions"/>; result always estimated.
/// </summary>
public sealed class HeatV01Engine(IOptions<HeatAnalysisOptions> options) : IEnvironmentalAnalysisEngine
{
    public string EngineName => "HeatV01";

    public string EngineVersion => _options.MethodVersion;

    private readonly HeatAnalysisOptions _options = options.Value;

    public Task<EngineAnalysisResult> AnalyzeAsync(AnalysisInput input, CancellationToken ct = default)
    {
        var veg = Param(input, "vegetationCoverPct", _options.DefaultVegetationCoverPct);
        var albedo = Param(input, "albedo", _options.DefaultAlbedo);
        var shade = Param(input, "shadingPct", _options.DefaultShadingPct);

        var raw = _options.BaseTemperatureC
            - veg * _options.VegCoolingPerPct
            - shade * _options.ShadingCoolingPerPct
            + (_options.AlbedoReference - albedo) * _options.AlbedoSensitivity;
        var value = Math.Round(Math.Clamp(raw, -50, 60), 2);

        var values = input.Geometry.Polygons
            .Select((p, i) => new RawAreaValue(i, Math.Round(p.Area, 6), value))
            .ToList();

        return Task.FromResult(new EngineAnalysisResult(
            _options.Metric, _options.Unit, values,
            $"{_options.Method} (engine {EngineName} {_options.MethodVersion}, config {_options.ConfigVersion})",
            IsEstimated: true));
    }

    private static double Param(AnalysisInput input, string key, double fallback) =>
        input.Parameters.TryGetValue(key, out var v) ? v : fallback;
}
