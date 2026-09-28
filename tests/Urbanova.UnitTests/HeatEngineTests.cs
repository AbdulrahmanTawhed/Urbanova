using FluentAssertions;
using Microsoft.Extensions.Options;
using Urbanova.Domain;
using Urbanova.Domain.Analysis;
using Urbanova.Domain.ValueObjects;
using Urbanova.Infrastructure.Analysis;

namespace Urbanova.UnitTests;

/// <summary>Phase 7: engine math + classification bands. Pure — no DB.</summary>
public sealed class HeatEngineTests
{
    private static HeatV01Engine Engine() => new(Options.Create(new HeatAnalysisOptions()));

    private static ThresholdClassificationService Classifier() =>
        new(Options.Create(new ClassificationOptions()));

    private static AnalysisInput Input(Dictionary<string, double>? parameters = null) => new(
        Guid.NewGuid(),
        new NormalizedGeometry("EPSG:4326", "deg²",
            [new NormalizedPolygon([[new NormalizedPoint(0, 0)]], 4.0, 0, 0, 2, 2)],
            4.0, 1, 0),
        parameters ?? []);

    [Fact]
    public async Task Defaults_YieldBaseTemperature_Estimated()
    {
        var result = await Engine().AnalyzeAsync(Input());
        result.Metric.Should().Be("LandSurfaceTempProxy");
        result.Unit.Should().Be("Celsius");
        result.IsEstimated.Should().BeTrue("MVP has no validated measurements");
        result.Values.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new RawAreaValue(0, 4.0, 32.0));
    }

    [Fact]
    public async Task Vegetation_Cools()
    {
        var result = await Engine().AnalyzeAsync(Input(new() { ["vegetationCoverPct"] = 100 }));
        result.Values.Single().Value.Should().BeApproximately(27.0, 1e-9);
    }

    [Fact]
    public async Task HighAlbedo_Cools_LowAlbedo_Warms()
    {
        var cool = await Engine().AnalyzeAsync(Input(new() { ["albedo"] = 0.8 }));
        cool.Values.Single().Value.Should().BeApproximately(28.0, 1e-9);
        var warm = await Engine().AnalyzeAsync(Input(new() { ["albedo"] = 0.0 }));
        warm.Values.Single().Value.Should().BeApproximately(34.4, 1e-9);
    }

    [Fact]
    public async Task Shading_Cools()
    {
        var result = await Engine().AnalyzeAsync(Input(new() { ["shadingPct"] = 100 }));
        result.Values.Single().Value.Should().BeApproximately(29.0, 1e-9);
    }

    [Fact]
    public async Task SameInputs_SameOutputs_Deterministic()
    {
        var engine = Engine();
        var input = Input(new() { ["vegetationCoverPct"] = 40, ["albedo"] = 0.5 });
        var first = await engine.AnalyzeAsync(input);
        var second = await engine.AnalyzeAsync(input);
        second.Should().BeEquivalentTo(first);
    }

    [Theory]
    [InlineData(29.99, ProblemClass.Acceptable)]
    [InlineData(30.0, ProblemClass.Moderate)]
    [InlineData(34.99, ProblemClass.Moderate)]
    [InlineData(35.0, ProblemClass.ProblemArea)]
    public void Classification_Bands(double value, ProblemClass expected)
    {
        Classifier().Classify(value).Should().Be(expected);
    }

    [Fact]
    public void Classification_Summarizes()
    {
        Classifier().Summarize([28.0, 32.0, 32.0, 40.0])
            .Should().BeEquivalentTo(new Dictionary<ProblemClass, int>
            {
                [ProblemClass.Acceptable] = 1,
                [ProblemClass.Moderate] = 2,
                [ProblemClass.ProblemArea] = 1,
            });
    }
}
