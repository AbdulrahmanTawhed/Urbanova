using FluentAssertions;
using Urbanova.Application.Scenarios;

namespace Urbanova.UnitTests;

/// <summary>Phase 8: scenario request validation (closed MVP parameter set).</summary>
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
    public void Create_UnknownParam_Fails()
    {
        new CreateScenarioRequestValidator()
            .Validate(new CreateScenarioRequest("A", null, null, new() { ["treeCount"] = 1 }))
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
    public void Update_BadAlbedo_Fails()
    {
        new UpdateScenarioRequestValidator()
            .Validate(new UpdateScenarioRequest(null, new() { ["albedo"] = 2 }, [1]))
            .IsValid.Should().BeFalse();
    }
}
