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
/// project's latest succeeded run. Existing rows for the run are replaced atomically —
/// deterministic rules make regeneration idempotent.
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

        var values = JsonSerializer.Deserialize<List<Application.Analysis.AreaValueDto>>(
                run.Result.ValuesJson, JsonOptions) ?? [];
        var parameters = ExtractParameters(run.InputSnapshotJson);

        var generated = await engine.GenerateAsync(new RecommendationInput(
            projectId, run.Id, run.ScenarioId,
            [.. values.Select(v => new AreaValue(
                v.PolygonIndex, v.Area, v.Value,
                Enum.Parse<ProblemClass>(v.Classification, ignoreCase: true)))],
            parameters), ct);

        // Idempotent refresh: replace this run's rows atomically — concurrent readers
        // never see an empty set and concurrent writers cannot interleave duplicates.
        // Runs inside the execution strategy (EnableRetryOnFailure forbids raw transactions).
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Recommendations.Where(r => r.AnalysisRunId == run.Id).ExecuteDeleteAsync(ct);
            foreach (var g in generated)
            {
                db.Recommendations.Add(new Recommendation
                {
                    ProjectId = projectId,
                    AnalysisRunId = run.Id,
                    ScenarioId = run.ScenarioId,
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
            await tx.CommitAsync(ct);
        });

        var rows = await db.Recommendations
            .Where(r => r.AnalysisRunId == run.Id)
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
        r.EvidenceSource, r.EvidenceLevel.ToString(), r.Feasibility, r.Confidence,
        new RecommendationCostDto("Unavailable", "Cost estimation arrives in Phase 11."));
}
