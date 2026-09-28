namespace Urbanova.Infrastructure.Analysis;

/// <summary>
/// Binds to "HeatAnalysis". Every value is a TEMPORARY MVP default pending engineering
/// validation (assumptions.md §§2–4) — the engine reads only this, never literals.
/// </summary>
public sealed class HeatAnalysisOptions
{
    public const string SectionName = "HeatAnalysis";

    public string Metric { get; set; } = "LandSurfaceTempProxy";

    public string Unit { get; set; } = "Celsius";

    public string Method { get; set; } = "v0.1-weighted-area";

    public string MethodVersion { get; set; } = "0.1.0-mvp";

    public string ConfigVersion { get; set; } = "mvp-001";

    public double BaseTemperatureC { get; set; } = 32.0;

    public double VegCoolingPerPct { get; set; } = 0.05;

    public double ShadingCoolingPerPct { get; set; } = 0.03;

    public double AlbedoReference { get; set; } = 0.3;

    public double AlbedoSensitivity { get; set; } = 8.0;

    public double DefaultVegetationCoverPct { get; set; } = 0;

    public double DefaultAlbedo { get; set; } = 0.3;

    public double DefaultShadingPct { get; set; } = 0;
}
