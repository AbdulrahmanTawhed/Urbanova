using FluentAssertions;
using Urbanova.Application.Analysis;

namespace Urbanova.UnitTests;

/// <summary>Analyze request shape validation. Parameter content moved to
/// ScenarioParameterCatalog (PRD v0.2) — see ScenarioParameterCatalogTests.</summary>
public sealed class AnalyzeRequestValidatorTests
{
    private readonly AnalyzeRequestValidator _validator = new();

    [Fact]
    public void Valid_WithEmptyParameters_Passes()
    {
        _validator.Validate(new AnalyzeRequest(Guid.NewGuid(), [])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Valid_WithParameters_PassesShape()
    {
        // Content rules live in the catalog; the DTO validator only checks shape.
        _validator.Validate(new AnalyzeRequest(Guid.NewGuid(),
            new() { ["vegetationCoverPct"] = 50, ["albedo"] = 0.5, ["shadingPct"] = 20 }))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void NullParameters_Fails()
    {
        _validator.Validate(new AnalyzeRequest(Guid.NewGuid(), null!)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void EmptyFileId_Fails()
    {
        _validator.Validate(new AnalyzeRequest(Guid.Empty, [])).IsValid.Should().BeFalse();
    }
}
