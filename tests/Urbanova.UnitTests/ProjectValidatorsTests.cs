using FluentAssertions;
using Urbanova.Application.Projects;

namespace Urbanova.UnitTests;

/// <summary>Phase 4: project input validation (API/Application level).</summary>
public sealed class ProjectValidatorsTests
{
    [Fact]
    public void Create_Valid_Passes()
    {
        var r = new CreateProjectRequestValidator().Validate(
            new CreateProjectRequest("Downtown Pilot", "Desc",
                new SiteInput("Main St 1", 52.5, 13.4, null, "EPSG:4326", 1500)));
        r.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_Fails(string name)
    {
        new CreateProjectRequestValidator()
            .Validate(new CreateProjectRequest(name, null, null)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Create_TooLongName_Fails()
    {
        new CreateProjectRequestValidator()
            .Validate(new CreateProjectRequest(new string('x', 201), null, null)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(100.0, 10.0)]   // latitude out of range
    [InlineData(10.0, 200.0)]   // longitude out of range
    public void Create_BadCoordinates_Fail(double lat, double lon)
    {
        new CreateProjectRequestValidator()
            .Validate(new CreateProjectRequest("P", null, new SiteInput(null, lat, lon, null, null, null)))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Create_NegativeArea_Fails()
    {
        new CreateProjectRequestValidator()
            .Validate(new CreateProjectRequest("P", null, new SiteInput(null, null, null, null, null, -5)))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Update_MissingRowVersion_Fails()
    {
        new UpdateProjectRequestValidator()
            .Validate(new UpdateProjectRequest("P", null, null, null, null)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Active")]
    [InlineData("Archived")]
    [InlineData("active")]
    [InlineData("ARCHIVED")]
    public void Update_ValidStatuses_Pass(string status)
    {
        // Regression: any casing validates (service parses case-insensitively).
        new UpdateProjectRequestValidator()
            .Validate(new UpdateProjectRequest("P", null, status, null, [1, 2, 3])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_BadStatus_Fails()
    {
        new UpdateProjectRequestValidator()
            .Validate(new UpdateProjectRequest("P", null, "Deleted", null, [1])).IsValid.Should().BeFalse();
    }
}
