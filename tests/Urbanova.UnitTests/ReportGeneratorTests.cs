using System.Text.Json;
using FluentAssertions;
using Urbanova.Domain.Reporting;
using Urbanova.Infrastructure.Reporting;

namespace Urbanova.UnitTests;

/// <summary>Phase 12: generator rendering. Pure — no DB.</summary>
public sealed class ReportGeneratorTests
{
    private static ReportModel Model(string projectName = "Downtown") => new(
        new ReportProjectSection(Guid.NewGuid(), projectName, "Desc", "Active", DateTimeOffset.UtcNow),
        new ReportSiteSection("Main St", 52.5, 13.4, "EPSG:4326", 1500),
        new ReportAnalysisSection(new ReportSectionStatus("Available", null),
            Guid.NewGuid(), "HeatV01", "0.1.0-mvp", "mvp-001",
            "LandSurfaceTempProxy", "Celsius", true,
            [new ReportAreaValue(0, 4, 32, "Moderate")],
            new Dictionary<string, int> { ["moderate"] = 1 }),
        [new ReportProblemArea(0, 36, 4, "Plant trees")],
        [new ReportScenarioSection(Guid.NewGuid(), "Baseline", "B", true, 1,
            new Dictionary<string, double>(), Guid.NewGuid(), 32, "Moderate")],
        new ReportComparisonSection(new ReportSectionStatus("Available", null),
            Guid.NewGuid(), Guid.NewGuid(), -4.5, 1, 0, -40m, "USD"),
        [new ReportRecommendationSection(0, "Hot", "Plant", "Calculated", "High", null, "Unavailable")],
        new ReportCostSection(1, 0, 100m, "USD"),
        ["Mean improved."], ["MVP estimates only."]);

    [Fact]
    public async Task Json_IsCanonical_AndComplete()
    {
        var rendered = await new JsonReportGenerator().GenerateAsync(Model());
        rendered.ContentType.Should().Be("application/json");

        using var doc = JsonDocument.Parse(rendered.Content);
        var root = doc.RootElement;
        root.GetProperty("project").GetProperty("name").GetString().Should().Be("Downtown");
        root.GetProperty("analysis").GetProperty("values")[0].GetProperty("value").GetDouble().Should().Be(32);
        root.GetProperty("problemAreas").GetArrayLength().Should().Be(1);
        root.GetProperty("comparison").GetProperty("meanDelta").GetDouble().Should().Be(-4.5);
        root.GetProperty("decisionSummary").GetArrayLength().Should().Be(1);
        root.GetProperty("caveats")[0].GetString().Should().Contain("MVP");
    }

    [Fact]
    public async Task Html_EscapesContent_AndRendersSections()
    {
        var rendered = await new HtmlReportGenerator().GenerateAsync(Model("<script>alert(1)</script>"));
        rendered.ContentType.Should().Be("text/html");
        rendered.Content.Should().Contain("&lt;script&gt;").And.NotContain("<script>alert");
        rendered.Content.Should().Contain("Environmental analysis")
            .And.Contain("Decision-support summary")
            .And.Contain("Caveats")
            .And.Contain("<table>");
    }

    [Fact]
    public async Task Html_EncodesTableCells_NoDoubleEncoding()
    {
        // Regression: Table relied on callers to encode (future XSS); now it encodes itself.
        var model = Model() with
        {
            Recommendations =
            [
                new ReportRecommendationSection(0, "<b>Hot</b>", "<script>x()</script>",
                    "Calculated", "High", null, "Unavailable"),
            ],
        };
        var rendered = await new HtmlReportGenerator().GenerateAsync(model);
        rendered.Content.Should().Contain("&lt;script&gt;").And.NotContain("<script>x()")
            .And.NotContain("&amp;lt;", "must not double-encode");
    }

    [Fact]
    public async Task Html_UnavailableSections_RenderReason()
    {
        var model = Model() with
        {
            Analysis = new ReportAnalysisSection(
                new ReportSectionStatus("Unavailable", "No succeeded analysis yet."),
                null, null, null, null, null, null, true, [], new Dictionary<string, int>()),
        };
        var rendered = await new HtmlReportGenerator().GenerateAsync(model);
        rendered.Content.Should().Contain("No succeeded analysis yet.");
    }
}
