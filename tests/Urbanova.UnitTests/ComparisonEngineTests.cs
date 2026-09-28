using FluentAssertions;
using Urbanova.Domain;
using Urbanova.Domain.Analysis;
using Urbanova.Domain.Comparison;
using Urbanova.Infrastructure.Comparison;

namespace Urbanova.UnitTests;

/// <summary>Phase 9: comparison math. Pure — no DB.</summary>
public sealed class ComparisonEngineTests
{
    private readonly ComparisonEngine _engine = new();

    private static ComparisonInput Input(
        List<AreaValue> baseValues, Dictionary<string, double> baseParams,
        List<AreaValue> altValues, Dictionary<string, double> altParams) => new(
        Guid.NewGuid(), baseValues, baseParams, Guid.NewGuid(), altValues, altParams);

    [Fact]
    public async Task Improvement_ComputesDeltas_Transitions_Tradeoffs()
    {
        var result = await _engine.CompareAsync(Input(
            [new(0, 4, 32, ProblemClass.Moderate), new(1, 2, 36, ProblemClass.ProblemArea)],
            new() { ["vegetationCoverPct"] = 10 },
            [new(0, 4, 27, ProblemClass.Acceptable), new(1, 2, 33, ProblemClass.Moderate)],
            new() { ["vegetationCoverPct"] = 100 }));

        result.Environmental.Areas.Should().HaveCount(2);
        result.Environmental.Areas[0].Delta.Should().BeApproximately(-5, 1e-9);
        result.Environmental.Areas[0].Improved.Should().BeTrue();
        result.Environmental.MeanDelta.Should().BeApproximately(-4, 1e-9);
        result.Environmental.ImprovedCount.Should().Be(2);
        result.Environmental.WorsenedCount.Should().Be(0);
        result.Environmental.ImprovedClassTransitions.Should().Be(2);
        result.Environmental.NewProblemAreas.Should().Be(0);
        result.WhatChanged.Should().ContainSingle()
            .Which.Should().Be(new ParameterChange("vegetationCoverPct", 10, 100));
        result.Tradeoffs.Should().Contain(t => t.Contains("improved by 4"));
        result.Cost.Status.Should().Be("Unavailable");
        result.Feasibility.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task Worsening_FlagsNewProblemAreas()
    {
        var result = await _engine.CompareAsync(Input(
            [new(0, 1, 28, ProblemClass.Acceptable)], new(),
            [new(0, 1, 36, ProblemClass.ProblemArea)], new()));

        result.Environmental.WorsenedCount.Should().Be(1);
        result.Environmental.NewProblemAreas.Should().Be(1);
        result.Environmental.ImprovedClassTransitions.Should().Be(0);
        result.Tradeoffs.Should().Contain(t => t.Contains("new problem area"));
        result.WhatChanged.Should().BeEmpty();
    }

    [Fact]
    public async Task MismatchedAreas_Throws()
    {
        var act = () => _engine.CompareAsync(Input(
            [new(0, 1, 30, ProblemClass.Moderate)], new(),
            [new(0, 1, 30, ProblemClass.Moderate), new(1, 1, 30, ProblemClass.Moderate)], new()));
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*same geometry*");
    }

    [Fact]
    public async Task UnknownClassification_Throws_Loudly()
    {
        // Regression: Rank used to default unknown bands to Moderate, silently corrupting transitions.
        var engine = new ComparisonEngine();
        var input = Input(
            [new(0, 1, 30, (ProblemClass)999)], new(),
            [new(0, 1, 30, ProblemClass.Moderate)], new());
        // Enum.ToString of an undefined value yields "999", which Rank must reject.
        await engine.Invoking(e => e.CompareAsync(input)).Should().ThrowAsync<ArgumentException>();
    }
}
