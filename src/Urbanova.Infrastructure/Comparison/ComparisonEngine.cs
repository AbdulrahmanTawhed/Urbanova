using Urbanova.Domain;
using Urbanova.Domain.Analysis;
using Urbanova.Domain.Comparison;

namespace Urbanova.Infrastructure.Comparison;

/// <summary>
/// MVP comparison methodology (replaceable via <see cref="IScenarioComparisonService"/>).
/// Heat semantics: lower value = improvement. Polygons match by index; mismatched
/// geometry counts reject the comparison instead of guessing alignment.
/// </summary>
public sealed class ComparisonEngine : IScenarioComparisonService
{
    public Task<ScenarioComparison> CompareAsync(ComparisonInput input, CancellationToken ct = default)
    {
        if (input.BaselineValues.Count != input.AlternativeValues.Count)
            throw new ArgumentException(
                $"Scenarios have different analyzed areas ({input.BaselineValues.Count} vs {input.AlternativeValues.Count}); " +
                "comparison requires the same geometry. Re-analyze both scenarios from the same file.");

        var baseByIndex = input.BaselineValues.OrderBy(v => v.PolygonIndex).ToList();
        var altByIndex = input.AlternativeValues.OrderBy(v => v.PolygonIndex).ToList();

        var areas = baseByIndex.Zip(altByIndex, (b, a) =>
        {
            if (b.PolygonIndex != a.PolygonIndex)
                throw new ArgumentException("Area indexes do not align; comparison requires the same geometry.");
            var delta = Math.Round(a.Value - b.Value, 2);
            return new AreaDelta(
                b.PolygonIndex, b.Value, b.Classification.ToString(),
                a.Value, a.Classification.ToString(), delta, delta < 0);
        }).ToList();

        var meanBase = baseByIndex.Count == 0 ? 0 : Math.Round(baseByIndex.Average(v => v.Value), 2);
        var meanAlt = altByIndex.Count == 0 ? 0 : Math.Round(altByIndex.Average(v => v.Value), 2);
        var improvedTransitions = areas.Count(a => Rank(a.AlternativeClassification) < Rank(a.BaselineClassification));
        var newProblems = areas.Count(a =>
            a.BaselineClassification != ProblemClass.ProblemArea.ToString()
            && a.AlternativeClassification == ProblemClass.ProblemArea.ToString());

        var environmental = new EnvironmentalDelta(
            areas, meanBase, meanAlt, Math.Round(meanAlt - meanBase, 2),
            areas.Count(a => a.Improved), areas.Count(a => !a.Improved && a.Delta > 0),
            improvedTransitions, newProblems);

        var whatChanged = input.BaselineParameters
            .Keys.Union(input.AlternativeParameters.Keys, StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k =>
            {
                input.BaselineParameters.TryGetValue(k, out var b);
                input.AlternativeParameters.TryGetValue(k, out var a);
                return (k, b, a);
            })
            .Where(t => t.b != t.a)
            .Select(t => new ParameterChange(t.k, t.b, t.a))
            .ToList();

        return Task.FromResult(new ScenarioComparison(
            whatChanged,
            environmental,
            new DimensionStatus("Unavailable", "No cost estimates linked to these scenarios yet."),
            new DimensionStatus("Unavailable", "No feasibility data linked to these scenarios yet."),
            BuildTradeoffs(whatChanged, environmental)));
    }

    private static int Rank(string classification) => classification switch
    {
        nameof(ProblemClass.Acceptable) => 0,
        nameof(ProblemClass.Moderate) => 1,
        nameof(ProblemClass.ProblemArea) => 2,
        // Unknown bands must fail loudly — silently ranking as Moderate corrupts transitions.
        _ => throw new ArgumentException($"Unknown classification '{classification}'."),
    };

    private static IReadOnlyList<string> BuildTradeoffs(
        IReadOnlyList<ParameterChange> changes, EnvironmentalDelta env)
    {
        var tradeoffs = new List<string>();
        foreach (var c in changes)
            tradeoffs.Add($"{c.Parameter} changed from {c.Baseline} to {c.Alternative}.");
        tradeoffs.Add(env.MeanDelta < 0
            ? $"Mean heat value improved by {Math.Abs(env.MeanDelta)}°C across {env.Areas.Count} area(s)."
            : env.MeanDelta > 0
                ? $"Mean heat value worsened by {env.MeanDelta}°C across {env.Areas.Count} area(s)."
                : "No mean heat change; check per-area deltas.");
        tradeoffs.Add($"{env.ImprovedCount} of {env.Areas.Count} area(s) improved, {env.WorsenedCount} worsened.");
        if (env.ImprovedClassTransitions > 0)
            tradeoffs.Add($"{env.ImprovedClassTransitions} area(s) moved to a better class.");
        if (env.NewProblemAreas > 0)
            tradeoffs.Add($"Warning: {env.NewProblemAreas} new problem area(s) appeared.");
        return tradeoffs;
    }
}
