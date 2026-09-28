using FluentAssertions;
using Urbanova.Application.Scenarios;

namespace Urbanova.UnitTests;

/// <summary>Scenario request shape validation. Parameter content moved to
/// ScenarioParameterCatalog (PRD v0.2) — see ScenarioParameterCatalogTests.</summary>
public sealed class ScenarioValidatorTests
{
    [Fact]
    public void Create_Valid_Passes()
    {
        new CreateScenarioRequestValidator()
            .Validate(new CreateScenarioRequest("Alt-1", null, Guid.NewGuid(),
                new() { ["vegetationCoverPct"] = 40 }))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Create_BlankName_Fails()
    {
        new CreateScenarioRequestValidator()
            .Validate(new CreateScenarioRequest("", null, null, null))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Update_MissingRowVersion_Fails()
    {
        new UpdateScenarioRequestValidator()
            .Validate(new UpdateScenarioRequest("A", null, null))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Update_Partial_NoParams_Passes()
    {
        new UpdateScenarioRequestValidator()
            .Validate(new UpdateScenarioRequest("Renamed", null, [1, 2, 3]))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_WithParameters_PassesShape()
    {
        // Content rules live in the catalog; the DTO validator only checks shape.
        new UpdateScenarioRequestValidator()
            .Validate(new UpdateScenarioRequest(null, new() { ["albedo"] = 2 }, [1]))
            .IsValid.Should().BeTrue();
    }
}
