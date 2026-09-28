using FluentAssertions;
using Microsoft.Extensions.Options;
using Urbanova.Domain;
using Urbanova.Domain.Analysis;
using Urbanova.Domain.Recommendations;
using Urbanova.Infrastructure.Analysis;
using Urbanova.Infrastructure.Recommendations;

namespace Urbanova.UnitTests;

/// <summary>Phase 10: recommendation rules. Pure — no DB.</summary>
public sealed class RecommendationEngineTests
{
    private readonly RuleRecommendationEngine _engine = new(
        Options.Create(new HeatAnalysisOptions()),
        Options.Create(new ClassificationOptions()));

    private static RecommendationInput Input(params AreaValue[] areas) => new(
        Guid.NewGuid(), Guid.NewGuid(), null, areas,
        new Dictionary<string, double> { ["vegetationCoverPct"] = 10 });

    [Fact]
    public async Task ProblemArea_YieldsCalculatedIntervention()
    {
        var recs = await _engine.GenerateAsync(Input(new AreaValue(0, 4, 36, ProblemClass.ProblemArea)));

        var rec = recs.Should().ContainSingle().Subject;
        rec.PolygonIndex.Should().Be(0);
        rec.EvidenceLevel.Should().Be(EvidenceLevel.Calculated);
        rec.Confidence.Should().BeNull("no confidence model exists");
        rec.Intervention.Should().Contain("31%", "10% current + floor((36−35)/0.05)+1=21% needed for strict band exit");
        rec.ExpectedImpact.PredictedDeltaC.Should().BeApproximately(-1.05, 1e-9);
        rec.ExpectedImpact.TargetClassification.Should().Be(nameof(ProblemClass.Moderate));
        rec.Feasibility.Should().Contain("Medium");
        rec.EvidenceSource.Should().Contain("HeatV01");
    }

    [Fact]
    public async Task ModerateArea_YieldsEstimatedPreventive()
    {
        var recs = await _engine.GenerateAsync(Input(new AreaValue(2, 1, 32, ProblemClass.Moderate)));

        var rec = recs.Should().ContainSingle().Subject;
        rec.EvidenceLevel.Should().Be(EvidenceLevel.Estimated);
        rec.Confidence.Should().BeNull();
        rec.Intervention.Should().Contain("Maintain");
    }

    [Fact]
    public async Task AcceptableArea_YieldsNothing()
    {
        (await _engine.GenerateAsync(Input(new AreaValue(0, 1, 28, ProblemClass.Acceptable))))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Never_Claims_Validation()
    {
        var recs = await _engine.GenerateAsync(Input(
            new AreaValue(0, 1, 36, ProblemClass.ProblemArea),
            new AreaValue(1, 1, 32, ProblemClass.Moderate)));

        recs.Should().HaveCount(2);
        recs.Should().OnlyContain(r => r.EvidenceLevel != EvidenceLevel.Validated);
    }

    [Fact]
    public async Task InsufficientVegetation_Admits_Limits()
    {
        // 50°C with no cover: even 100% vegetation (−5°C) cannot reach Moderate.
        var recs = await _engine.GenerateAsync(new RecommendationInput(
            Guid.NewGuid(), Guid.NewGuid(), null,
            [new AreaValue(0, 9, 50, ProblemClass.ProblemArea)],
            new Dictionary<string, double> { ["vegetationCoverPct"] = 0 }));

        var rec = recs.Should().ContainSingle().Subject;
        rec.Intervention.Should().Contain("100%").And.Contain("insufficient");
        rec.ExpectedImpact.TargetClassification.Should().Be(nameof(ProblemClass.ProblemArea));
        rec.Feasibility.Should().Contain("Low");
    }
}
