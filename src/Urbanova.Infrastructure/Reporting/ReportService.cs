using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Urbanova.Application.Analysis;
using Urbanova.Application.Comparison;
using Urbanova.Application.Recommendations;
using Urbanova.Application.Reporting;
using Urbanova.Domain;
using Urbanova.Domain.Analysis;
using Urbanova.Domain.Entities;
using Urbanova.Domain.Reporting;
using Urbanova.Infrastructure.Analysis;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.Reporting;

/// <summary>
/// Reporting orchestration (Phase 12). Assembles the canonical model from project,
/// latest analysis, scenarios, optional comparison, stored recommendations and costs;
/// renders via the format-matched generator; persists the row (canonical JSON always
/// stored; rendered HTML additionally filed). Owner-scoped throughout.
/// </summary>
public sealed class ReportService(
    AppDbContext db,
    IEnumerable<IReportGenerator> generators,
    IComparisonService comparison,
    IRecommendationService recommendations,
    IReportFileStore files,
    IOptions<ClassificationOptions> classOptions) : IReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<ReportResponse> GenerateAsync(
        Guid ownerId, Guid projectId, CreateReportRequest request, CancellationToken ct = default)
    {
        var project = await db.Projects
            .Include(p => p.Site)
            .SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw ReportException.NotFound(projectId);
        var owner = project.OwnerId;
        if (owner != ownerId)
            throw ReportException.Forbidden();

        if (!Enum.TryParse<ReportFormat>(request.Format ?? "Json", ignoreCase: true, out var format)
            || !Enum.IsDefined(format))
            throw ReportException.Invalid($"Format must be one of: {string.Join(", ", Enum.GetNames<ReportFormat>())}.");
        var generator = generators.SingleOrDefault(g => g.Format == format)
            ?? throw ReportException.Invalid($"No generator registered for format '{format}'.");

        var now = DateTimeOffset.UtcNow;
        var run = await LatestRunAsync(projectId, ct);
        var model = await BuildModelAsync(ownerId, project, run, request, now, ct);

        var rendered = await generator.GenerateAsync(model, ct);
        var canonical = format == ReportFormat.Json
            ? rendered.Content
            : JsonSerializer.Serialize(model, new JsonSerializerOptions
                { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });

        // Insert first so the rendered file can use the real report id. Versions allocate
        // as Max+1 with bounded retry: the unique index on (ProjectId, Version) turns a
        // lost race into a catchable violation instead of duplicate versions.
        var report = new Report
        {
            ProjectId = projectId,
            Format = format,
            ContentJson = canonical,
            PayloadHash = Hash(canonical),
            Version = await NextVersionAsync(projectId, ct),
            GeneratedAt = now,
            CreatedBy = ownerId,
        };
        db.Reports.Add(report);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                db.Entry(report).State = EntityState.Detached;
                report.Id = Guid.NewGuid();
                report.Version = await NextVersionAsync(projectId, ct);
                db.Reports.Add(report);
            }
        }

        if (format == ReportFormat.Html)
        {
            report.StoragePath = await files.SaveAsync(projectId, report.Id, "html", rendered.Content, ct);
            await db.SaveChangesAsync(ct);
        }

        return ToResponse(report);
    }

    public async Task<ReportResponse> GetAsync(Guid ownerId, Guid reportId, CancellationToken ct = default)
    {
        var report = await db.Reports.SingleOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw ReportException.NotFound(reportId);
        await OwnsAsync(ownerId, report.ProjectId, reportId, ct);
        return ToResponse(report);
    }

    public async Task<(string ContentType, byte[] Content, string FileName)> GetFileAsync(
        Guid ownerId, Guid reportId, CancellationToken ct = default)
    {
        var report = await db.Reports.SingleOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw ReportException.NotFound(reportId);
        await OwnsAsync(ownerId, report.ProjectId, reportId, ct);
        if (string.IsNullOrWhiteSpace(report.StoragePath))
            throw ReportException.NotFound(reportId);
        try
        {
            var bytes = await files.ReadAsync(report.StoragePath!, ct);
            var ext = Path.GetExtension(report.StoragePath!).TrimStart('.');
            var contentType = ext == "html" ? "text/html" : "application/octet-stream";
            return (contentType, bytes, $"urbanova-report-{report.Id}.{ext}");
        }
        catch (InvalidOperationException)
        {
            throw ReportException.NotFound(reportId);
        }
    }

    private async Task<ReportModel> BuildModelAsync(
        Guid ownerId, Project project, AnalysisRun? run, CreateReportRequest request,
        DateTimeOffset now, CancellationToken ct)
    {
        var values = ReadValues(run);
        var summary = ReadSummary(run);
        var scenarios = await db.Scenarios
            .Where(s => s.ProjectId == project.Id)
            .OrderBy(s => s.Kind).ThenBy(s => s.CreatedAt)
            .ToListAsync(ct);

        var scenarioSections = new List<ReportScenarioSection>();
        foreach (var s in scenarios)
        {
            var latest = await ResolveRunAsync(s, ct);
            var mean = latest?.Values.Average(v => v.Value);
            scenarioSections.Add(new ReportScenarioSection(
                s.Id, s.Kind.ToString(), s.Name, s.IsLocked, s.Version,
                ReadParameters(s.ParametersJson), latest?.Run.Id, mean,
                mean is null ? null : ClassifyBand(mean.Value)));
        }

        ComparisonResponse? comparisonResult = null;
        string? comparisonReason = null;
        if (request.BaselineScenarioId is not null && request.AlternativeScenarioId is not null)
        {
            try
            {
                comparisonResult = await comparison.CompareAsync(
                    project.OwnerId, project.Id,
                    request.BaselineScenarioId.Value, request.AlternativeScenarioId.Value, ct);
            }
            catch (ComparisonException ex)
            {
                comparisonReason = ex.Message;
            }
        }

        // Recommendations resolve through the idempotent service, so reports never
        // silently omit them when the endpoint was not called first. Retrieval is
        // non-destructive (stable rows per immutable run), therefore cost links are
        // read from persisted Recommendation→CostEstimate rows keyed by real id —
        // one batched query, no per-recommendation round-trips, project-scoped.
        var recRows = run is null
            ? []
            : await recommendations.GetForProjectAsync(ownerId, project.Id, run.Id, ct);
        Dictionary<Guid, CostEstimate?> costByRecId = new();
        if (recRows.Count > 0)
        {
            var recIds = recRows.Select(r => r.Id).ToList();
            var linked = await db.Recommendations
                .Where(r => r.ProjectId == project.Id && recIds.Contains(r.Id))
                .Include(r => r.CostEstimate)
                .AsNoTracking()
                .ToListAsync(ct);
            foreach (var row in linked)
                costByRecId[row.Id] = row.CostEstimate;
        }
        var recByPolygon = recRows
            .GroupBy(r => r.PolygonIndex)
            .ToDictionary(g => g.Key, g => g.First().Intervention);

        var problemAreas = values
            .Where(v => v.Classification == ProblemClass.ProblemArea)
            .Select(v => new ReportProblemArea(v.PolygonIndex, v.Value, v.Area,
                recByPolygon.TryGetValue(v.PolygonIndex, out var hint) ? hint : "Analyze intervention options."))
            .ToList();

        var estimates = await db.CostEstimates.Where(e => e.ProjectId == project.Id).ToListAsync(ct);
        var calculated = estimates.Where(e => e.Status == CostStatus.Calculated).ToList();
        // Single currency-safe summary shared by ReportCostSection and the
        // DecisionSummary below — one decision, no duplicated currency logic.
        var costSummary = SummarizeCosts(estimates, calculated);

        var decision = new List<string>();
        var caveats = new List<string>
        {
            "All environmental values are MVP estimates, not validated measurements.",
            "Heat thresholds and classification bands are temporary (pending engineering validation).",
            "Areas are planar CRS units (deg² for EPSG:4326), not square meters.",
        };
        if (run?.Result is null)
        {
            decision.Add($"Project '{project.Name}' has no succeeded analysis yet — quantitative sections are unavailable.");
        }
        else
        {
            var mean = values.Average(v => v.Value);
            var hottest = values.MaxBy(v => v.Value)!;
            decision.Add(
                $"Latest analysis ({run.Result.Metric}): mean {Math.Round(mean, 2)} {run.Result.Unit} " +
                $"across {values.Count} area(s).");
            decision.Add(
                $"Hottest area: {hottest.PolygonIndex} at {hottest.Value} {run.Result.Unit} ({hottest.Classification}).");
            decision.Add(problemAreas.Count == 0
                ? "No problem areas identified."
                : $"{problemAreas.Count} problem area(s) identified, " +
                  $"{recByPolygon.Count} with calculated interventions.");
        }
        if (comparisonResult is not null)
        {
            decision.Add(
                $"Alternative averages {comparisonResult.Environmental.MeanAlternative} vs baseline " +
                $"{comparisonResult.Environmental.MeanBaseline} (Δ {comparisonResult.Environmental.MeanDelta}).");
            if (comparisonResult.Cost.Status == "Calculated")
                decision.Add(
                    $"Estimated cost delta: {comparisonResult.Cost.Delta} {comparisonResult.Cost.Currency} " +
                    $"(baseline {comparisonResult.Cost.BaselineTotal}, alternative {comparisonResult.Cost.AlternativeTotal}).");
        }
        else
        {
            decision.Add("No scenario comparison included (both scenarios need succeeded analyses).");
        }
        if (costSummary.Status == "Calculated")
        {
            decision.Add(
                $"Estimated total (calculated): {costSummary.CalculatedTotal} " +
                $"{costSummary.Currency} across {costSummary.CalculatedCount} estimate(s); " +
                $"{costSummary.UnavailableCount} unavailable.");
            if (calculated.Any(e => (e.PriceSource ?? "").Contains("placeholder", StringComparison.OrdinalIgnoreCase)
                || (e.PriceSource ?? "").Contains("catalog", StringComparison.OrdinalIgnoreCase)))
                caveats.Add("Catalog prices are placeholders, not market data.");
        }
        else if (estimates.Count == 0)
        {
            decision.Add("No cost estimates recorded.");
        }
        else
        {
            decision.Add($"Estimated total unavailable: {costSummary.Reason}");
        }

        return new ReportModel(
            new ReportProjectSection(project.Id, project.Name, project.Description,
                project.Status.ToString(), now),
            project.Site is null ? null : new ReportSiteSection(
                project.Site.Address, project.Site.Latitude, project.Site.Longitude,
                project.Site.Crs, project.Site.AreaM2),
            run?.Result is null
                ? new ReportAnalysisSection(new ReportSectionStatus("Unavailable", "No succeeded analysis yet."),
                    null, null, null, null, null, null, true, [], new Dictionary<string, int>())
                : new ReportAnalysisSection(new ReportSectionStatus("Available", null),
                    run.Id, run.EngineName, run.EngineVersion, run.ConfigVersion,
                    run.Result.Metric, run.Result.Unit, run.Result.IsEstimated,
                    [.. values.Select(v => new ReportAreaValue(v.PolygonIndex, v.Area, v.Value, v.Classification.ToString()))],
                    summary),
            problemAreas,
            scenarioSections,
            comparisonResult is null
                ? new ReportComparisonSection(
                    new ReportSectionStatus("Unavailable", comparisonReason ?? "No scenario pair requested."),
                    request.BaselineScenarioId, request.AlternativeScenarioId, null, null, null, null, null)
                : new ReportComparisonSection(new ReportSectionStatus("Available", null),
                    comparisonResult.Baseline.ScenarioId, comparisonResult.Alternative.ScenarioId,
                    comparisonResult.Environmental.MeanDelta, comparisonResult.Environmental.ImprovedCount,
                    comparisonResult.Environmental.WorsenedCount,
                    comparisonResult.Cost.Status == "Calculated" ? comparisonResult.Cost.Delta : null,
                    comparisonResult.Cost.Currency),
            [.. recRows.OrderBy(r => r.PolygonIndex).Select(r =>
            {
                // Real linked status from the stable persisted relationship. Unlinked
                // stays Unavailable; non-Calculated links expose their real status with
                // no detail row (a stored Total of 0 must never read as confirmed price).
                costByRecId.TryGetValue(r.Id, out var estimate);
                var status = estimate?.Status.ToString() ?? "Unavailable";
                ReportRecommendationCost? cost = estimate is { Status: CostStatus.Calculated }
                    ? new ReportRecommendationCost(
                        estimate.Status.ToString(), estimate.Total, estimate.Currency,
                        estimate.Quantity, estimate.QuantitySource, estimate.Unit,
                        estimate.UnitPrice, estimate.PriceSource)
                    : null;
                return new ReportRecommendationSection(
                    r.PolygonIndex, r.Problem, r.Cause, r.Intervention, r.ExpectedImpact,
                    r.RuleCode, r.EvidenceSource, r.EvidenceLevel,
                    r.ScientificReferences, r.Feasibility, r.Confidence,
                    status, cost);
            })],
            costSummary,
            decision, caveats);
    }

    /// <summary>
    /// Project-wide cost aggregate over already-loaded estimates (no DB access).
    /// Currency convention mirrors <c>ComparisonService.CostDeltaAsync</c>: trim,
    /// upper-invariant, ordinal distinct; currencies named in reasons are sorted
    /// ordinally so output never depends on database row order.
    /// </summary>
    private static ReportCostSection SummarizeCosts(
        IReadOnlyList<CostEstimate> estimates, IReadOnlyList<CostEstimate> calculated)
    {
        if (calculated.Count == 0)
            return new ReportCostSection(
                0, estimates.Count, "Unavailable",
                estimates.Count == 0
                    ? "No cost estimates recorded."
                    : "Estimates exist but none have reliable prices.",
                null, null);

        var currencies = calculated
            .Select(e => (e.Currency ?? string.Empty).Trim().ToUpperInvariant())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        if (currencies.Count > 1)
            return new ReportCostSection(
                calculated.Count, estimates.Count - calculated.Count, "Unavailable",
                $"A combined total is unavailable because estimates use multiple currencies " +
                $"({string.Join(", ", currencies)}); refusing to sum.",
                null, null);

        return new ReportCostSection(
            calculated.Count, estimates.Count - calculated.Count, "Calculated", null,
            calculated.Sum(e => e.Total),
            currencies.SingleOrDefault() ?? "USD");
    }

    private async Task<int> NextVersionAsync(Guid projectId, CancellationToken ct) =>
        await db.Reports.Where(r => r.ProjectId == projectId).MaxAsync(r => (int?)r.Version, ct) + 1 ?? 1;

    private async Task OwnsAsync(Guid ownerId, Guid projectId, Guid reportId, CancellationToken ct)
    {
        var owner = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct);
        if (owner is null || owner != ownerId)
            throw owner is null ? ReportException.NotFound(reportId) : ReportException.Forbidden();
    }

    private async Task<AnalysisRun?> LatestRunAsync(Guid projectId, CancellationToken ct) =>
        await db.AnalysisRuns
            .Where(r => r.ProjectId == projectId && r.Status == AnalysisStatus.Succeeded)
            .OrderByDescending(r => r.CompletedAt)
            .ThenByDescending(r => r.CreatedAt)
            .Include(r => r.Result)
            .FirstOrDefaultAsync(ct);

    private async Task<(AnalysisRun Run, List<AreaValue> Values)?> ResolveRunAsync(Scenario s, CancellationToken ct)
    {
        var run = await db.AnalysisRuns
            .Where(r => r.ScenarioId == s.Id && r.Status == AnalysisStatus.Succeeded)
            .OrderByDescending(r => r.CompletedAt)
            .Include(r => r.Result)
            .FirstOrDefaultAsync(ct)
            ?? (s.BaseAnalysisRunId is null ? null : await db.AnalysisRuns
                .Where(r => r.Id == s.BaseAnalysisRunId && r.Status == AnalysisStatus.Succeeded)
                .Include(r => r.Result)
                .SingleOrDefaultAsync(ct));
        if (run?.Result is null)
            return null;
        return (run, ReadValues(run));
    }

    private List<AreaValue> ReadValues(AnalysisRun? run)
    {
        if (run?.Result is null)
            return [];
        var dtos = JsonSerializer.Deserialize<List<AreaValueDto>>(run.Result.ValuesJson, JsonOptions) ?? [];
        return [.. dtos.Select(d => new AreaValue(d.PolygonIndex, d.Area, d.Value,
            Enum.Parse<ProblemClass>(d.Classification, ignoreCase: true)))];
    }

    private Dictionary<string, int> ReadSummary(AnalysisRun? run)
    {
        if (run?.Result?.ClassificationSummaryJson is null)
            return new Dictionary<string, int>();
        return JsonSerializer.Deserialize<Dictionary<string, int>>(run.Result.ClassificationSummaryJson, JsonOptions)
            ?? [];
    }

    private static Dictionary<string, double> ReadParameters(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, double>>(json) ?? [];

    private string ClassifyBand(double mean)
    {
        var o = classOptions.Value;
        return mean < o.AcceptableBelow
            ? nameof(ProblemClass.Acceptable)
            : mean < o.ModerateBelow ? nameof(ProblemClass.Moderate) : nameof(ProblemClass.ProblemArea);
    }

    private static ReportResponse ToResponse(Report r) => new(
        r.Id, r.ProjectId, r.Format.ToString(), r.Version, r.PayloadHash ?? string.Empty,
        !string.IsNullOrWhiteSpace(r.StoragePath), r.ContentJson, r.GeneratedAt);

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
