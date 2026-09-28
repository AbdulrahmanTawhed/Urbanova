using System.Text.Json;
using FluentAssertions;
using Urbanova.Domain.Reporting;
using Urbanova.Infrastructure.Reporting;

namespace Urbanova.UnitTests;

/// <summary>Phase 12: generator rendering. Pure — no DB.</summary>
public sealed class ReportGeneratorTests
{
    private static JsonElement Impact() =>
        JsonDocument.Parse(
            "{\"predictedDeltaC\":-1.05,\"targetClassification\":\"Moderate\"," +
            "\"basis\":\"HeatV01 sensitivity\"}").RootElement.Clone();

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
        [new ReportRecommendationSection(0, "Hot", "Low vegetation", "Plant", Impact(),
            "HEAT-VEG-001", "HeatV01 sensitivities", "Calculated",
            ["MVP placeholder reference — pending engineering validation"],
            "High", null, "Unavailable", null)],
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
                new ReportRecommendationSection(0, "<b>Hot</b>", "<script>cause()</script>",
                    "<script>x()</script>", Impact(), "HEAT-VEG-001", "HeatV01 sensitivities",
                    "Calculated", ["ref"], "High", null, "Unavailable", null),
            ],
        };
        var rendered = await new HtmlReportGenerator().GenerateAsync(model);
        rendered.Content.Should().Contain("&lt;script&gt;").And.NotContain("<script>x()")
            .And.NotContain("<script>cause()")
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

    [Fact]
    public async Task Json_ExposesRecommendationTraceability()
    {
        var rendered = await new JsonReportGenerator().GenerateAsync(Model());
        using var doc = JsonDocument.Parse(rendered.Content);
        var rec = doc.RootElement.GetProperty("recommendations")[0];

        rec.GetProperty("cause").GetString().Should().Be("Low vegetation");
        rec.GetProperty("expectedImpact").GetProperty("predictedDeltaC").GetDouble().Should().BeApproximately(-1.05, 1e-9);
        rec.GetProperty("expectedImpact").GetProperty("targetClassification").GetString().Should().Be("Moderate");
        rec.GetProperty("ruleCode").GetString().Should().Be("HEAT-VEG-001");
        rec.GetProperty("evidenceSource").GetString().Should().Contain("HeatV01");
        rec.GetProperty("evidenceLevel").GetString().Should().Be("Calculated");
        rec.GetProperty("scientificReferences").GetArrayLength().Should().BeGreaterThan(0);
        rec.GetProperty("feasibility").GetString().Should().Be("High");
    }

    [Fact]
    public async Task Json_LinkedCalculatedCost_ShowsCalculated_DetailMatches()
    {
        var model = Model() with
        {
            Recommendations =
            [
                new ReportRecommendationSection(0, "Hot", "Low vegetation", "Plant", Impact(),
                    "HEAT-VEG-001", "HeatV01 sensitivities", "Calculated", ["ref"],
                    "High", null, "Calculated",
                    new ReportRecommendationCost("Calculated", 850m, "USD", 100m,
                        "UserProvided", "m2", 8.50m, "user-provided")),
            ],
        };

        var rendered = await new JsonReportGenerator().GenerateAsync(model);
        using var doc = JsonDocument.Parse(rendered.Content);
        var rec = doc.RootElement.GetProperty("recommendations")[0];

        rec.GetProperty("costStatus").GetString().Should().Be("Calculated");
        var cost = rec.GetProperty("cost");
        cost.GetProperty("total").GetDecimal().Should().Be(850m);
        cost.GetProperty("currency").GetString().Should().Be("USD");
        cost.GetProperty("quantitySource").GetString().Should().Be("UserProvided");
        cost.GetProperty("priceSource").GetString().Should().Be("user-provided");
    }

    [Fact]
    public async Task Json_MissingCost_StaysUnavailable_NoFakeTotal()
    {
        var rendered = await new JsonReportGenerator().GenerateAsync(Model());
        using var doc = JsonDocument.Parse(rendered.Content);
        var rec = doc.RootElement.GetProperty("recommendations")[0];

        rec.GetProperty("costStatus").GetString().Should().Be("Unavailable");
        rec.GetProperty("cost").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Html_RendersTraceability_AndTrueCost_NoWinnerLanguage()
    {
        var model = Model() with
        {
            Recommendations =
            [
                new ReportRecommendationSection(0, "Hot area", "Low vegetation", "Plant trees", Impact(),
                    "HEAT-VEG-001", "HeatV01 sensitivities", "Calculated",
                    ["MVP placeholder reference — pending engineering validation"],
                    "High", null, "Calculated",
                    new ReportRecommendationCost("Calculated", 850m, "USD", 100m,
                        "UserProvided", "m2", 8.50m, "user-provided")),
            ],
        };

        var rendered = await new HtmlReportGenerator().GenerateAsync(model);
        rendered.Content.Should().Contain("Low vegetation")
            .And.Contain("Plant trees")
            .And.Contain("HEAT-VEG-001")
            .And.Contain("HeatV01 sensitivities")
            .And.Contain("High")
            .And.Contain("Calculated — 850 USD")
            .And.Contain("MVP placeholder reference")
            .And.NotContain("best scenario")
            .And.NotContain("Best scenario")
            .And.NotContain("winner")
            .And.NotContain("Winner");
    }

    [Fact]
    public async Task Html_MissingOptionals_RenderHonestly()
    {
        var model = Model() with
        {
            Recommendations =
            [
                new ReportRecommendationSection(0, "Hot", "", "Plant", null, null, null,
                    "Estimated", [], null, null, "Unavailable", null),
            ],
        };

        var rendered = await new HtmlReportGenerator().GenerateAsync(model);
        rendered.Content.Should().Contain("—")
            .And.NotContain("[]")
            .And.NotContain("null");
    }
}
