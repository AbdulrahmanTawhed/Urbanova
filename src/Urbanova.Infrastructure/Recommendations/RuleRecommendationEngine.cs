using Microsoft.Extensions.Options;
using Urbanova.Domain;
using Urbanova.Domain.Analysis;
using Urbanova.Domain.Recommendations;
using Urbanova.Infrastructure.Analysis;

namespace Urbanova.Infrastructure.Recommendations;

/// <summary>
/// MVP rule engine (replaceable via <see cref="IRecommendationEngine"/>).
/// Problem areas → vegetation intervention sized by HeatV01 sensitivities (Calculated);
/// Moderate areas → preventive advice (Estimated). Acceptable areas → no action.
/// Confidence is always null (no confidence model exists — assumptions.md §7).
/// Feasibility bands are a documented heuristic, not engineering judgment.
/// </summary>
public sealed class RuleRecommendationEngine(
    IOptions<HeatAnalysisOptions> heatOptions,
    IOptions<ClassificationOptions> classOptions) : IRecommendationEngine
{
    private readonly HeatAnalysisOptions _heat = heatOptions.Value;
    private readonly ClassificationOptions _classes = classOptions.Value;

    public Task<IReadOnlyList<GeneratedRecommendation>> GenerateAsync(
        RecommendationInput input, CancellationToken ct = default)
    {
        var currentVeg = input.Parameters.TryGetValue("vegetationCoverPct", out var v) ? v : _heat.DefaultVegetationCoverPct;
        var source = $"HeatV01 {_heat.MethodVersion} sensitivities + classification bands {_heat.ConfigVersion}";

        var recs = new List<GeneratedRecommendation>();
        foreach (var area in input.AreaValues.OrderBy(a => a.PolygonIndex))
        {
            if (area.Classification == ProblemClass.ProblemArea)
                recs.Add(Intervention(area, currentVeg, source));
            else if (area.Classification == ProblemClass.Moderate)
                recs.Add(Preventive(area, currentVeg, source));
        }
        return Task.FromResult<IReadOnlyList<GeneratedRecommendation>>(recs);
    }

    private GeneratedRecommendation Intervention(AreaValue area, double currentVeg, string source)
    {
        // A zeroed cooling rate is a configuration error, not a big number: fail loudly
        // instead of dividing by zero (Infinity → overflow → 500).
        if (_heat.VegCoolingPerPct <= 0)
            throw new InvalidOperationException(
                "HeatAnalysis:VegCoolingPerPct must be positive; check configuration.");
        // Strictly below the band edge: needed > gap/rate, i.e. floor + 1.
        var needed = (int)Math.Floor((area.Value - _classes.ModerateBelow) / _heat.VegCoolingPerPct) + 1;
        needed = Math.Max(needed, 1);
        var targetVeg = Math.Min(currentVeg + needed, 100);
        var applied = targetVeg - currentVeg;
        var predicted = Math.Round(area.Value - applied * _heat.VegCoolingPerPct, 2);
        var sufficient = targetVeg < 100 || predicted < _classes.ModerateBelow;
        var feasibility = needed <= 20 ? "High" : needed <= 50 ? "Medium" : "Low";

        return new GeneratedRecommendation(
            area.PolygonIndex,
            $"Area {area.PolygonIndex} is a heat problem area ({area.Value} {_heat.Unit}).",
            $"Modeled value {area.Value} {_heat.Unit} reaches the problem band (≥ {_classes.ModerateBelow} {_heat.Unit}) " +
            $"at {currentVeg}% vegetation cover.",
            $"Increase vegetation cover from {currentVeg}% to {targetVeg}% in area {area.PolygonIndex}" +
            (sufficient ? "." : " (may be insufficient alone — combine with shading/albedo measures)."),
            new ExpectedImpact(
                Math.Round(predicted - area.Value, 2),
                predicted < _classes.ModerateBelow ? nameof(ProblemClass.Moderate) : nameof(ProblemClass.ProblemArea),
                $"HeatV01 sensitivity: −{_heat.VegCoolingPerPct} {_heat.Unit} per vegetation %."),
            source,
            EvidenceLevel.Calculated,
            $"{feasibility} (heuristic: +{needed}% vegetation needed).",
            Confidence: null);
    }

    private GeneratedRecommendation Preventive(AreaValue area, double currentVeg, string source) =>
        new(
            area.PolygonIndex,
            $"Area {area.PolygonIndex} is moderately warm ({area.Value} {_heat.Unit}).",
            $"Modeled value {area.Value} {_heat.Unit} sits in the moderate band " +
            $"({_classes.AcceptableBelow}–{_classes.ModerateBelow} {_heat.Unit}).",
            $"Maintain at least {currentVeg}% vegetation cover in area {area.PolygonIndex} and monitor; " +
            "expand cover if surrounding development intensifies.",
            new ExpectedImpact(0, nameof(ProblemClass.Moderate), "Preventive — no change modeled."),
            source,
            EvidenceLevel.Estimated,
            "High (no construction required).",
            Confidence: null);
}
