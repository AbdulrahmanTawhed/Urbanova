using FluentAssertions;
using Urbanova.Application.Analysis;

namespace Urbanova.UnitTests;

/// <summary>Phase 7: analysis request validation (closed MVP parameter set).</summary>
public sealed class AnalyzeRequestValidatorTests
{
    private readonly AnalyzeRequestValidator _validator = new();

    [Fact]
    public void Valid_WithEmptyParameters_Passes()
    {
        _validator.Validate(new AnalyzeRequest(Guid.NewGuid(), [])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Valid_WithAllParameters_Passes()
    {
        _validator.Validate(new AnalyzeRequest(Guid.NewGuid(),
            new() { ["vegetationCoverPct"] = 50, ["albedo"] = 0.5, ["shadingPct"] = 20 }))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void UnknownParameter_Fails()
    {
        _validator.Validate(new AnalyzeRequest(Guid.NewGuid(), new() { ["treeCount"] = 5 }))
            .IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("vegetationCoverPct", -1)]
    [InlineData("vegetationCoverPct", 101)]
    [InlineData("albedo", -0.1)]
    [InlineData("albedo", 1.1)]
    [InlineData("shadingPct", 150)]
    public void OutOfRange_Fails(string key, double value)
    {
        _validator.Validate(new AnalyzeRequest(Guid.NewGuid(), new() { [key] = value }))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void EmptyFileId_Fails()
    {
        _validator.Validate(new AnalyzeRequest(Guid.Empty, [])).IsValid.Should().BeFalse();
    }
}
