using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Urbanova.Application.Analysis;
using Urbanova.Application.Comparison;
using Urbanova.Domain;
using Urbanova.Domain.Analysis;
using Urbanova.Domain.Comparison;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.Comparison;

/// <summary>
/// Comparison orchestration (Phase 9): ownership → kind checks → latest succeeded runs
/// → engine → DTOs. "Latest" = most recently completed run per scenario.
/// </summary>
public sealed class ComparisonService(AppDbContext db, IScenarioComparisonService engine) : IComparisonService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<ComparisonResponse> CompareAsync(
        Guid ownerId, Guid projectId, Guid baselineId, Guid alternativeId,
        CancellationToken ct = default)
    {
        var owner = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct)
            ?? throw ComparisonException.NotFound(projectId);
        if (owner != ownerId)
            throw ComparisonException.Forbidden();

        var baseline = await LoadScenarioAsync(projectId, baselineId, "baseline", ct);
        var alternative = await LoadScenarioAsync(projectId, alternativeId, "alternative", ct);
        if (baseline.Kind != ScenarioKind.Baseline)
            throw ComparisonException.Invalid($"Scenario '{baselineId}' is not a baseline scenario.");
        if (alternative.Kind != ScenarioKind.Alternative)
            throw ComparisonException.Invalid($"Scenario '{alternativeId}' is not an alternative scenario.");

        // A scenario's analysis is its latest scenario-scoped run, falling back to the
        // linked baseline run (baselines are typically created FROM an analysis, not re-run).
        var baseRun = await LatestSucceededRunAsync(baseline.Id, ct)
            ?? await LinkedBaseRunAsync(baseline, ct)
            ?? throw ComparisonException.Invalid($"Baseline scenario '{baselineId}' has no succeeded analysis yet.");
        var altRun = await LatestSucceededRunAsync(alternative.Id, ct)
            ?? await LinkedBaseRunAsync(alternative, ct)
            ?? throw ComparisonException.Invalid($"Alternative scenario '{alternativeId}' has no succeeded analysis yet.");

        ScenarioComparison result;
        try
        {
            result = await engine.CompareAsync(new ComparisonInput(
                baseline.Id, baseRun.Values, ReadParameters(baseline.ParametersJson),
                alternative.Id, altRun.Values, ReadParameters(alternative.ParametersJson)), ct);
        }
        catch (ArgumentException ex)
        {
            throw ComparisonException.Invalid(ex.Message);
        }

        // Phase 11: fill the cost dimension from Calculated estimates linked to each scenario.
        var cost = await CostDeltaAsync(baseline.Id, alternative.Id, result.Cost.Reason, ct);

        return new ComparisonResponse(
            new ComparisonSideDto(baseline.Id, baseRun.Run.Id, baseline.Name),
            new ComparisonSideDto(alternative.Id, altRun.Run.Id, alternative.Name),
            [.. result.WhatChanged.Select(c => new ParameterChangeDto(c.Parameter, c.Baseline, c.Alternative))],
            new EnvironmentalDeltaDto(
                [.. result.Environmental.Areas.Select(a => new AreaDeltaDto(
                    a.PolygonIndex, a.BaselineValue, a.BaselineClassification,
                    a.AlternativeValue, a.AlternativeClassification, a.Delta, a.Improved))],
                result.Environmental.MeanBaseline, result.Environmental.MeanAlternative,
                result.Environmental.MeanDelta, result.Environmental.ImprovedCount,
                result.Environmental.WorsenedCount, result.Environmental.ImprovedClassTransitions,
                result.Environmental.NewProblemAreas),
            cost,
            new DimensionStatusDto(result.Feasibility.Status, result.Feasibility.Reason),
            result.Tradeoffs);
    }

    private async Task<CostDeltaDto> CostDeltaAsync(
        Guid baselineId, Guid alternativeId, string? fallbackReason, CancellationToken ct)
    {
        var estimates = await db.CostEstimates
            .Where(e => e.ScenarioId == baselineId || e.ScenarioId == alternativeId)
            .ToListAsync(ct);
        if (estimates.Count == 0)
            return new CostDeltaDto("Unavailable", fallbackReason, null, null, null, null);

        var calculated = estimates.Where(e => e.Status == CostStatus.Calculated).ToList();
        if (calculated.Count == 0)
            return new CostDeltaDto("Unavailable",
                "Estimates exist but none have reliable prices.", null, null, null, null);

        // Totals across mixed currencies would be meaningless — refuse instead of mislabeling.
        var currencies = calculated
            .Select(e => (e.Currency ?? string.Empty).Trim().ToUpperInvariant())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (currencies.Count > 1)
            return new CostDeltaDto("Unavailable",
                $"Estimates span multiple currencies ({string.Join(", ", currencies)}); refusing to sum.",
                null, null, null, null);

        var baseTotal = calculated.Where(e => e.ScenarioId == baselineId).Sum(e => e.Total);
        var altTotal = calculated.Where(e => e.ScenarioId == alternativeId).Sum(e => e.Total);
        return new CostDeltaDto("Calculated", null, baseTotal, altTotal, altTotal - baseTotal,
            currencies.SingleOrDefault() ?? "USD");
    }

    private async Task<Scenario> LoadScenarioAsync(Guid projectId, Guid id, string role, CancellationToken ct)
    {
        var scenario = await db.Scenarios.SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ComparisonException.NotFound(id);
        if (scenario.ProjectId != projectId)
            throw ComparisonException.Invalid($"The {role} scenario does not belong to this project.");
        return scenario;
    }

    private async Task<(AnalysisRun Run, List<AreaValue> Values)?> LinkedBaseRunAsync(Scenario scenario, CancellationToken ct)
    {
        if (scenario.BaseAnalysisRunId is null)
            return null;
        var run = await db.AnalysisRuns
            .Where(r => r.Id == scenario.BaseAnalysisRunId && r.Status == AnalysisStatus.Succeeded)
            .Include(r => r.Result)
            .SingleOrDefaultAsync(ct);
        if (run?.Result is null)
            return null;
        return (run, ReadValues(run));
    }

    private async Task<(AnalysisRun Run, List<AreaValue> Values)?> LatestSucceededRunAsync(Guid scenarioId, CancellationToken ct)
    {
        var run = await db.AnalysisRuns
            .Where(r => r.ScenarioId == scenarioId && r.Status == AnalysisStatus.Succeeded)
            .OrderByDescending(r => r.CompletedAt)
            .ThenByDescending(r => r.CreatedAt)
            .Include(r => r.Result)
            .FirstOrDefaultAsync(ct);
        if (run?.Result is null)
            return null;
        return (run, ReadValues(run));
    }

    private List<AreaValue> ReadValues(AnalysisRun run)
    {
        var dtos = JsonSerializer.Deserialize<List<AreaValueDto>>(run.Result!.ValuesJson, JsonOptions) ?? [];
        return [.. dtos.Select(d => new AreaValue(
            d.PolygonIndex, d.Area, d.Value,
            Enum.Parse<ProblemClass>(d.Classification, ignoreCase: true)))];
    }

    private static Dictionary<string, double> ReadParameters(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, double>>(json) ?? [];
}
