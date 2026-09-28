using FluentAssertions;
using Urbanova.Domain;
using Urbanova.Domain.BusinessRules;
using Urbanova.Domain.Entities;

namespace Urbanova.UnitTests;

/// <summary>Phase 2: domain invariants. No DB — pure rule coverage for PRD §§11,14 + project validation.</summary>
public sealed class BusinessRulesTests
{
    [Fact]
    public void ProjectName_Valid_Passes()
    {
        var act = () => ProjectRules.ValidateName("Downtown Pilot");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ProjectName_Blank_Throws(string name)
    {
        var act = () => ProjectRules.ValidateName(name);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProjectName_TooLong_Throws()
    {
        var act = () => ProjectRules.ValidateName(new string('x', ProjectRules.NameMaxLength + 1));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CostTotal_Multiplies()
    {
        CostRules.CalculateTotal(10m, 2.5m).Should().Be(25m);
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(5, -1)]
    public void CostTotal_Negative_Throws(decimal qty, decimal price)
    {
        var act = () => CostRules.CalculateTotal(qty, price);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CostApply_SetsTotalAndStatus()
    {
        var e = new CostEstimate { Quantity = 4m, UnitPrice = 12.5m, Unit = "m2", Status = CostStatus.PendingValidation };
        CostRules.ApplyCalculation(e);
        e.Total.Should().Be(50m);
        e.Status.Should().Be(CostStatus.Calculated);
    }

    [Fact]
    public void CostApply_Unavailable_ZeroesTotal()
    {
        var e = new CostEstimate { Quantity = 4m, UnitPrice = 12.5m, Unit = "m2", Status = CostStatus.Unavailable };
        CostRules.ApplyCalculation(e);
        e.Total.Should().Be(0m);
        e.Status.Should().Be(CostStatus.Unavailable);
    }

    [Fact]
    public void BaselineLocked_CannotMutate()
    {
        var baseline = new Scenario { Kind = ScenarioKind.Baseline, IsLocked = true, Name = "B" };
        var act = () => ScenarioRules.EnsureMutable(baseline);
        act.Should().Throw<InvalidOperationException>().WithMessage("*locked*");
    }

    [Fact]
    public void Alternative_InheritsBaseline()
    {
        var baseline = new Scenario
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Kind = ScenarioKind.Baseline,
            Name = "Baseline",
            ParametersJson = """{"vegetationCoverPct": 10}""",
            IsLocked = true
        };
        var alt = ScenarioRules.CreateAlternative(baseline, "Alt-1");
        alt.Kind.Should().Be(ScenarioKind.Alternative);
        alt.ParentScenarioId.Should().Be(baseline.Id);
        alt.ParametersJson.Should().Be(baseline.ParametersJson);
        alt.IsLocked.Should().BeFalse();
    }

    [Fact]
    public void Alternative_FromNonBaseline_Throws()
    {
        var notBaseline = new Scenario { Kind = ScenarioKind.Alternative, Name = "A" };
        var act = () => ScenarioRules.CreateAlternative(notBaseline, "Alt-2");
        act.Should().Throw<InvalidOperationException>();
    }
}
