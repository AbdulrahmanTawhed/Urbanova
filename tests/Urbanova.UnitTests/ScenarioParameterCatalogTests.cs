using FluentAssertions;
using Microsoft.Extensions.Options;
using Urbanova.Application.Scenarios;

namespace Urbanova.UnitTests;

/// <summary>PRD v0.2: catalog-driven parameter validation with coded errors.</summary>
public sealed class ScenarioParameterCatalogTests
{
    private static ScenarioParameterCatalog Catalog(
        int max = 3,
        bool allowVeg = true, bool allowAlbedo = true, bool allowShade = true) =>
        new(Options.Create(new ScenarioParameterOptions
        {
            MaxParameters = max,
            Parameters =
            [
                new() { Key = "vegetationCoverPct", Name = "Vegetation cover", Unit = "%", Min = 0, Max = 100, Allowed = allowVeg },
                new() { Key = "albedo", Name = "Albedo", Unit = "fraction", Min = 0, Max = 1, Allowed = allowAlbedo },
                new() { Key = "shadingPct", Name = "Shading", Unit = "%", Min = 0, Max = 100, Allowed = allowShade },
            ],
        }));

    [Fact]
    public void ValidSet_Passes()
    {
        var act = () => Catalog().Validate(new Dictionary<string, double>
        {
            ["vegetationCoverPct"] = 50, ["albedo"] = 0.5, ["shadingPct"] = 20,
        });
        act.Should().NotThrow();
    }

    [Fact]
    public void EmptySet_Passes()
    {
        var act = () => Catalog().Validate(new Dictionary<string, double>());
        act.Should().NotThrow();
    }

    [Fact]
    public void UnknownKey_ThrowsUnsupported()
    {
        var act = () => Catalog().Validate(new Dictionary<string, double> { ["treeCount"] = 5 });
        act.Should().Throw<ScenarioException>()
            .Where(e => e.ErrorCode == "UNSUPPORTED_SCENARIO_PARAMETER");
    }

    [Theory]
    [InlineData("vegetationCoverPct", -1)]
    [InlineData("vegetationCoverPct", 101)]
    [InlineData("albedo", -0.1)]
    [InlineData("albedo", 1.1)]
    [InlineData("shadingPct", 150)]
    public void OutOfRange_ThrowsInvalid(string key, double value)
    {
        var act = () => Catalog().Validate(new Dictionary<string, double> { [key] = value });
        act.Should().Throw<ScenarioException>()
            .Where(e => e.ErrorCode == "INVALID_SCENARIO_PARAMETER");
    }

    [Fact]
    public void DisallowedKey_ThrowsUnsupported()
    {
        var act = () => Catalog(allowShade: false).Validate(
            new Dictionary<string, double> { ["shadingPct"] = 10 });
        act.Should().Throw<ScenarioException>()
            .Where(e => e.ErrorCode == "UNSUPPORTED_SCENARIO_PARAMETER");
    }

    [Fact]
    public void FourthParameter_ThrowsTooMany()
    {
        var act = () => Catalog().Validate(new Dictionary<string, double>
        {
            ["vegetationCoverPct"] = 10, ["albedo"] = 0.5, ["shadingPct"] = 10, ["extra"] = 1,
        });
        act.Should().Throw<ScenarioException>()
            .Where(e => e.ErrorCode == "TOO_MANY_SCENARIO_PARAMETERS");
    }

    [Fact]
    public void MaxParameters_IsConfigurable()
    {
        var act = () => Catalog(max: 1).Validate(new Dictionary<string, double>
        {
            ["vegetationCoverPct"] = 10, ["albedo"] = 0.5,
        });
        act.Should().Throw<ScenarioException>()
            .Where(e => e.ErrorCode == "TOO_MANY_SCENARIO_PARAMETERS");
    }

    [Fact]
    public void NaN_ThrowsInvalid()
    {
        var act = () => Catalog().Validate(
            new Dictionary<string, double> { ["albedo"] = double.NaN });
        act.Should().Throw<ScenarioException>()
            .Where(e => e.ErrorCode == "INVALID_SCENARIO_PARAMETER");
    }
}

/// <summary>Startup validation for the parameter catalog itself.</summary>
public sealed class ScenarioParameterOptionsValidatorTests
{
    private static ValidateOptionsResult Check(ScenarioParameterOptions options) =>
        new ScenarioParameterOptionsValidator().Validate(null, options);

    [Fact]
    public void ValidCatalog_Passes()
    {
        Check(new ScenarioParameterOptions
        {
            MaxParameters = 3,
            Parameters = [new() { Key = "a", Name = "A", Unit = "%", Min = 0, Max = 100, Allowed = true }],
        }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void DuplicateKeys_Fail()
    {
        Check(new ScenarioParameterOptions
        {
            Parameters =
            [
                new() { Key = "a", Name = "A", Unit = "%", Min = 0, Max = 1 },
                new() { Key = "a", Name = "A2", Unit = "%", Min = 0, Max = 1 },
            ],
        }).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void InvertedBand_AndNegativeCap_Fail()
    {
        Check(new ScenarioParameterOptions
        {
            MaxParameters = -1,
            Parameters = [new() { Key = "a", Name = "A", Unit = "%", Min = 10, Max = 1 }],
        }).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void BlankKey_Fails()
    {
        Check(new ScenarioParameterOptions
        {
            Parameters = [new() { Key = " ", Name = "A", Unit = "%", Min = 0, Max = 1 }],
        }).Succeeded.Should().BeFalse();
    }
}
