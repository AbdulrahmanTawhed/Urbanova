using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Urbanova.Application.Recommendations;
using Urbanova.Domain;
using Urbanova.Domain.Analysis;
using Urbanova.Domain.Entities;
using Urbanova.Domain.Recommendations;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.Recommendations;

/// <summary>
/// Generate-and-store recommendations (Phase 10). Run resolution: explicit runId, else the
/// project's latest succeeded run. Rows are generated once per immutable run and then
/// returned as-is — deterministic rules make generation idempotent, and stable rows keep
/// identities and linked CostEstimates intact across repeated reads and reports.
/// </summary>
public sealed class RecommendationService(AppDbContext db, IRecommendationEngine engine) : IRecommendationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<IReadOnlyList<RecommendationResponse>> GetForProjectAsync(
        Guid ownerId, Guid projectId, Guid? runId, CancellationToken ct = default)
    {
        var owner = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct)
            ?? throw RecommendationException.NotFound(projectId);
        if (owner != ownerId)
            throw RecommendationException.Forbidden();

        var run = runId is not null
            ? await db.AnalysisRuns.Include(r => r.Result).SingleOrDefaultAsync(r => r.Id == runId, ct)
                ?? throw RecommendationException.NotFound(runId.Value)
            : await db.AnalysisRuns
                .Where(r => r.ProjectId == projectId && r.Status == AnalysisStatus.Succeeded)
                .OrderByDescending(r => r.CompletedAt)
                .ThenByDescending(r => r.CreatedAt)
                .Include(r => r.Result)
                .FirstOrDefaultAsync(ct)
                ?? throw RecommendationException.NoAnalysis();

        if (run.ProjectId != projectId)
            throw RecommendationException.NotFound(run.Id);
        if (run.Status != AnalysisStatus.Succeeded || run.Result is null)
            throw RecommendationException.NoAnalysis();

        // Idempotent read: AnalysisRuns are immutable, so rows already stored for
        // this run are the answer. Returning them preserves Recommendation identities
        // and linked CostEstimates (a delete/recreate would orphan estimates through
        // the SET NULL FKs and churn ids on every read and every report).
        var existing = await db.Recommendations
            .Where(r => r.AnalysisRunId == run.Id)
            .Include(r => r.RecommendationRule)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        if (existing.Count > 0)
            return [.. existing.Select(ToResponse)];

        var values = JsonSerializer.Deserialize<List<Application.Analysis.AreaValueDto>>(
                run.Result.ValuesJson, JsonOptions) ?? [];
        var parameters = ExtractParameters(run.InputSnapshotJson);

        var generated = await engine.GenerateAsync(new RecommendationInput(
            projectId, run.Id, run.ScenarioId,
            [.. values.Select(v => new AreaValue(
                v.PolygonIndex, v.Area, v.Value,
                Enum.Parse<ProblemClass>(v.Classification, ignoreCase: true)))],
            parameters), ct);

        // Resolve every cited rule before writing anything: an unknown or inactive
        // rule blocks the whole set with NO_EVIDENCE (PRD v0.2 §6) — never stored
        // as an unvalidated recommendation.
        var rules = new Dictionary<string, RecommendationRule>(StringComparer.Ordinal);
        foreach (var code in generated.Select(g => g.RuleCode).Distinct(StringComparer.Ordinal))
        {
            var rule = await db.RecommendationRules
                .SingleOrDefaultAsync(r => r.Code == code, ct);
            if (rule is null || !rule.IsActive)
                throw RecommendationException.NoEvidence(code);
            rules[code] = rule;
        }

        // Insert-only generation: rows are created once per immutable run and never
        // replaced, so identities and cost links stay stable across repeated reads
        // and reports. Runs inside the execution strategy (EnableRetryOnFailure
        // forbids raw transactions); re-checks inside the transaction to narrow the
        // concurrent first-writer window — under ReadCommitted this reduces but does
        // not eliminate a simultaneous first-write race (see follow-up: unique
        // constraint on (AnalysisRunId, PolygonIndex)).
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var current = await db.Recommendations
                .Where(r => r.AnalysisRunId == run.Id)
                .Select(r => r.Id)
                .ToListAsync(ct);
            if (current.Count == 0)
            {
                foreach (var g in generated)
                {
                    db.Recommendations.Add(new Recommendation
                    {
                        ProjectId = projectId,
                        AnalysisRunId = run.Id,
                        ScenarioId = run.ScenarioId,
                        RecommendationRuleId = rules[g.RuleCode].Id,
                        Problem = g.Problem,
                        Cause = g.Cause,
                        Intervention = g.Intervention,
                        ExpectedImpactJson = JsonSerializer.Serialize(g.ExpectedImpact, JsonOptions),
                        EvidenceSource = g.EvidenceSource,
                        EvidenceLevel = g.EvidenceLevel,
                        PolygonIndex = g.PolygonIndex,
                        Feasibility = g.Feasibility,
                        Confidence = g.Confidence,
                        CreatedBy = ownerId,
                    });
                }
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        });

        var rows = await db.Recommendations
            .Where(r => r.AnalysisRunId == run.Id)
            .Include(r => r.RecommendationRule)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        return [.. rows.Select(ToResponse)];
    }

    private static Dictionary<string, double> ExtractParameters(string inputSnapshotJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputSnapshotJson);
            if (doc.RootElement.TryGetProperty("parameters", out var p)
                && p.ValueKind == JsonValueKind.Object)
            {
                var dict = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var prop in p.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        dict[prop.Name] = prop.Value.GetDouble();
                return dict;
            }
        }
        catch (JsonException)
        {
            // Corrupt snapshot → empty params; engine falls back to configured defaults.
        }
        return [];
    }

    // AnalysisRunId stays nullable: the FK is SET NULL when a run is deleted, and
    // Guid.Empty would masquerade as a real run id.
    private static RecommendationResponse ToResponse(Recommendation r) => new(
        r.Id, r.ProjectId, r.AnalysisRunId, r.ScenarioId, r.PolygonIndex,
        r.Problem, r.Cause, r.Intervention,
        string.IsNullOrWhiteSpace(r.ExpectedImpactJson)
            ? null : JsonDocument.Parse(r.ExpectedImpactJson!).RootElement.Clone(),
        r.RecommendationRule?.Code, r.EvidenceSource, r.EvidenceLevel.ToString(), r.Feasibility, r.Confidence,
        ParseReferences(r.RecommendationRule?.ScientificReferencesJson),
        new RecommendationCostDto("Unavailable", "Cost estimation arrives in Phase 11."));

    private static IReadOnlyList<string> ParseReferences(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
