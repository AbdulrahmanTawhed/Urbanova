using System.Net;
using System.Text;
using Urbanova.Domain;
using Urbanova.Domain.Reporting;

namespace Urbanova.Infrastructure.Reporting;

/// <summary>Simple HTML renderer over the canonical model (no PDF dependency — see assumptions.md §9).</summary>
public sealed class HtmlReportGenerator : IReportGenerator
{
    public ReportFormat Format => ReportFormat.Html;

    public Task<GeneratedReport> GenerateAsync(ReportModel model, CancellationToken ct = default)
    {
        var h = new StringBuilder();
        h.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        h.Append("<title>").Append(E($"URBANOVA Report — {model.Project.Name}")).Append("</title>");
        h.Append("<style>body{font-family:sans-serif;max-width:960px;margin:auto;padding:1rem}" +
                 "table{border-collapse:collapse;width:100%}td,th{border:1px solid #ccc;padding:.35rem;text-align:left}" +
                 ".warn{background:#fff4e5;padding:.5rem}.muted{color:#666}</style></head><body>");

        h.Append("<h1>").Append(E(model.Project.Name)).Append("</h1>");
        h.Append("<p class=\"muted\">Generated ").Append(E(model.Project.GeneratedAtUtc.ToString("u")))
            .Append(" · Status ").Append(E(model.Project.Status)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(model.Project.Description))
            h.Append("<p>").Append(E(model.Project.Description)).Append("</p>");

        Section(h, "Design overview", model.Site is null
            ? "<p class=\"muted\">No site information recorded.</p>"
            : $"<p>Address: {E(model.Site.Address ?? "—")} · CRS: {E(model.Site.Crs)} · " +
              $"Area: {(model.Site.AreaM2?.ToString() ?? "—")} m²</p>");

        Section(h, "Environmental analysis", model.Analysis.Availability.Status != "Available"
            ? Unavailable(model.Analysis.Availability)
            : Table(["Area", "Value", "Class"],
                model.Analysis.Values.Select(v =>
                    new[] { v.PolygonIndex.ToString(), $"{v.Value} {model.Analysis.Unit ?? ""}", v.Classification })) +
              $"<p class=\"muted\">Engine {E(model.Analysis.EngineName ?? "")} {E(model.Analysis.EngineVersion ?? "")} · " +
              $"{(model.Analysis.IsEstimated ? "Estimated values — not validated measurements." : "Validated.")}</p>");

        Section(h, "Problem areas", model.ProblemAreas.Count == 0
            ? "<p>No problem areas in the latest analysis.</p>"
            : Table(["Area", "Value", "Suggested next step"],
                model.ProblemAreas.Select(p => new[] { p.PolygonIndex.ToString(), p.Value.ToString(), p.RecommendationHint })));

        Section(h, "Scenarios", model.Scenarios.Count == 0
            ? "<p class=\"muted\">No scenarios created yet.</p>"
            : Table(["Scenario", "Kind", "Mean", "Params"],
                model.Scenarios.Select(s => new[]
                {
                    s.Name, s.Kind,
                    s.MeanValue?.ToString() ?? "—",
                    string.Join(", ", s.Parameters.Select(kv => $"{kv.Key}={kv.Value}")),
                })));

        Section(h, "Scenario comparison", model.Comparison.Availability.Status != "Available"
            ? Unavailable(model.Comparison.Availability)
            : $"<p>Mean delta: {model.Comparison.MeanDelta} · Improved: {model.Comparison.ImprovedCount} · " +
              $"Worsened: {model.Comparison.WorsenedCount} · Cost delta: " +
              $"{(model.Comparison.CostDelta?.ToString() ?? "unavailable")} {E(model.Comparison.CostCurrency ?? "")}</p>");

        Section(h, "Recommendations", model.Recommendations.Count == 0
            ? "<p class=\"muted\">No recommendations for the latest analysis.</p>"
            : Table(["Area", "Problem", "Intervention", "Evidence", "Feasibility"],
                model.Recommendations.Select(r => new[]
                    { r.PolygonIndex.ToString(), r.Problem, r.Intervention, r.EvidenceLevel, r.Feasibility ?? "—" })));

        Section(h, "Estimated cost",
            $"<p>Calculated: {model.Costs.CalculatedCount} estimate(s), total {model.Costs.CalculatedTotal} {E(model.Costs.Currency)} · " +
            $"Unavailable: {model.Costs.UnavailableCount}</p>");

        Section(h, "Decision-support summary",
            "<ul>" + string.Concat(model.DecisionSummary.Select(s => $"<li>{E(s)}</li>")) + "</ul>" +
            "<div class=\"warn\"><strong>Caveats</strong><ul>" +
            string.Concat(model.Caveats.Select(c => $"<li>{E(c)}</li>")) + "</ul></div>");

        h.Append("</body></html>");
        return Task.FromResult(new GeneratedReport(ReportFormat.Html, h.ToString(), "text/html"));
    }

    private static void Section(StringBuilder h, string title, string body)
    {
        h.Append("<h2>").Append(E(title)).Append("</h2>").Append(body);
    }

    private static string Unavailable(ReportSectionStatus s) =>
        $"<p class=\"muted\">Unavailable — {E(s.Reason ?? "no data")}.</p>";

    // Table encodes every cell itself: callers pass raw values, so a future column
    // cannot introduce XSS by forgetting to encode.
    private static string Table(string[] headers, IEnumerable<string[]> rows)
    {
        var h = new StringBuilder("<table><tr>");
        foreach (var c in headers)
            h.Append("<th>").Append(E(c)).Append("</th>");
        h.Append("</tr>");
        foreach (var r in rows)
        {
            h.Append("<tr>");
            foreach (var c in r)
                h.Append("<td>").Append(E(c)).Append("</td>");
            h.Append("</tr>");
        }
        return h.Append("</table>").ToString();
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
