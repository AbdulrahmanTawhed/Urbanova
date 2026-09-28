using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Urbanova.Application.Analysis;
using Urbanova.Application.EngineeringFiles;
using Urbanova.Domain;
using Urbanova.Domain.Analysis;
using Urbanova.Domain.Entities;
using Urbanova.Domain.ValueObjects;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.Analysis;

/// <summary>
/// Analysis orchestration (Phase 7, extended Phase 8): ownership → valid file →
/// geometry → engine → classification → traceable run + result in ONE SaveChanges
/// (never partial-valid). Idempotent on (project, InputHash): replays return the
/// existing run. Scenario runs scope the hash with the scenario id.
/// Engine failures persist a Failed run, then surface ANALYSIS_FAILED.
/// </summary>
public sealed class AnalysisService(
    AppDbContext db,
    IProcessorRegistry registry,
    IFileStorage storage,
    IEnvironmentalAnalysisEngine engine,
    IEnvironmentalClassificationService classification,
    IOptions<HeatAnalysisOptions> heatOptions,
    IOptions<ClassificationOptions> classOptions) : IAnalysisService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<(AnalysisRunResponse Response, bool IsNew)> RunAsync(
        Guid ownerId, Guid projectId, AnalyzeRequest request, CancellationToken ct = default)
    {
        var owner = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct)
            ?? throw AnalysisException.NotFound("Project", projectId);
        if (owner != ownerId)
            throw AnalysisException.Forbidden();

        var file = await db.EngineeringFiles.SingleOrDefaultAsync(f => f.Id == request.FileId, ct)
            ?? throw AnalysisException.NotFound("Engineering file", request.FileId);
        if (file.ProjectId != projectId)
            throw AnalysisException.NotFound("Engineering file", request.FileId);
        if (file.ValidationStatus != FileValidationStatus.Valid)
            throw AnalysisException.InvalidFile("file must validate successfully before analysis.");

        var parameters = new SortedDictionary<string, double>(
            request.Parameters ?? new Dictionary<string, double>(), StringComparer.Ordinal);
        return await ExecuteAsync(ownerId, projectId, file, parameters, scenarioId: null, ct);
    }

    public async Task<(AnalysisRunResponse Response, bool IsNew)> RunForScenarioAsync(
        Guid ownerId, Guid scenarioId, Guid? fileIdOverride, CancellationToken ct = default)
    {
        var scenario = await db.Scenarios.SingleOrDefaultAsync(s => s.Id == scenarioId, ct)
            ?? throw AnalysisException.NotFound("Scenario", scenarioId);
        var owner = await db.Projects
            .Where(p => p.Id == scenario.ProjectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct);
        if (owner is null || owner != ownerId)
            throw owner is null
                ? AnalysisException.NotFound("Scenario", scenarioId)
                : AnalysisException.Forbidden();

        var parameters = new SortedDictionary<string, double>(
            JsonSerializer.Deserialize<Dictionary<string, double>>(scenario.ParametersJson) ?? [],
            StringComparer.Ordinal);

        Guid fileId = fileIdOverride ?? Guid.Empty;
        if (fileId == Guid.Empty)
        {
            EngineeringFile? baseFile = null;
            if (scenario.BaseAnalysisRunId is not null)
            {
                var baseRun = await db.AnalysisRuns.SingleOrDefaultAsync(r => r.Id == scenario.BaseAnalysisRunId, ct);
                if (baseRun?.EngineeringFileId is not null)
                    baseFile = await db.EngineeringFiles.SingleOrDefaultAsync(f => f.Id == baseRun.EngineeringFileId, ct);
            }
            if (baseFile is null)
                throw AnalysisException.InvalidFile("scenario has no baseline analysis file; provide fileId.");
            fileId = baseFile.Id;
        }

        var file = await db.EngineeringFiles.SingleOrDefaultAsync(f => f.Id == fileId, ct)
            ?? throw AnalysisException.NotFound("Engineering file", fileId);
        if (file.ProjectId != scenario.ProjectId)
            throw AnalysisException.NotFound("Engineering file", fileId);
        if (file.ValidationStatus != FileValidationStatus.Valid)
            throw AnalysisException.InvalidFile("file must validate successfully before analysis.");

        return await ExecuteAsync(ownerId, scenario.ProjectId, file, parameters, scenarioId, ct);
    }

    /// <summary>Shared pipeline: geometry → hashes → idempotent run. Direct-run hash inputs
    /// are frozen (scenario segment appended only for scenario runs).</summary>
    private async Task<(AnalysisRunResponse Response, bool IsNew)> ExecuteAsync(
        Guid ownerId, Guid projectId, EngineeringFile file,
        SortedDictionary<string, double> parameters, Guid? scenarioId,
        CancellationToken ct)
    {
        var processor = registry.FindByFormat(file.FormatDetected ?? "")
            ?? registry.Find(file.FileName, file.ContentType)
            ?? throw AnalysisException.InvalidFile($"unsupported format '{file.FormatDetected}'.");

        await using var stream = await storage.OpenReadAsync(file.StoragePath, ct);
        var extraction = await processor.ExtractGeometryAsync(stream, file.FileName, ct);
        if (!extraction.Success || extraction.Geometry is null)
            throw AnalysisException.GeometryFailed(extraction.Errors);
        var geometry = await processor.NormalizeGeometryAsync(extraction.Geometry, ct);

        var heat = heatOptions.Value;
        var configSnapshot = JsonSerializer.Serialize(new
        {
            heat.Metric, heat.Unit, heat.Method, heat.MethodVersion, heat.ConfigVersion,
            heat.BaseTemperatureC, heat.VegCoolingPerPct, heat.ShadingCoolingPerPct,
            heat.AlbedoReference, heat.AlbedoSensitivity,
            acceptableBelow = classOptions.Value.AcceptableBelow,
            moderateBelow = classOptions.Value.ModerateBelow,
        }, JsonOptions);
        var geometryHash = Hash(JsonSerializer.Serialize(new
        {
            geometry.Crs,
            Polygons = geometry.Polygons.Select(p => p.Rings.Select(r => r.Select(pt => new[] { pt.X, pt.Y }))),
        }));
        // Scenario segment appended only for scenario runs: direct-run hashes are frozen.
        var inputHash = scenarioId is null
            ? Hash(JsonSerializer.Serialize(new
            {
                engine = engine.EngineName,
                engineVersion = engine.EngineVersion,
                configVersion = heat.ConfigVersion,
                fileHash = file.HashSha256,
                geometryHash,
                parameters,
            }))
            : Hash(JsonSerializer.Serialize(new
            {
                engine = engine.EngineName,
                engineVersion = engine.EngineVersion,
                configVersion = heat.ConfigVersion,
                fileHash = file.HashSha256,
                geometryHash,
                parameters,
                scenario = scenarioId,
            }));

        var existingId = await db.AnalysisRuns
            .Where(r => r.ProjectId == projectId && r.InputHash == inputHash && r.Status == AnalysisStatus.Succeeded)
            .Select(r => (Guid?)r.Id)
            .SingleOrDefaultAsync(ct);
        if (existingId is not null)
            return (await LoadAsync(ownerId, existingId.Value, ct), false);

        var inputSnapshot = JsonSerializer.Serialize(new
        {
            fileId = file.Id, fileHash = file.HashSha256, geometryHash,
            format = file.FormatDetected, parameters,
        }, JsonOptions);

        var now = DateTimeOffset.UtcNow;
        var run = new AnalysisRun
        {
            ProjectId = projectId,
            EngineeringFileId = file.Id,
            ScenarioId = scenarioId,
            EngineName = engine.EngineName,
            EngineVersion = engine.EngineVersion,
            ConfigVersion = heat.ConfigVersion,
            ConfigSnapshotJson = configSnapshot,
            InputSnapshotJson = inputSnapshot,
            InputHash = inputHash,
            Status = AnalysisStatus.Running,
            StartedAt = now,
            CreatedBy = ownerId,
        };
        db.AnalysisRuns.Add(run);

        try
        {
            var engineResult = await engine.AnalyzeAsync(
                new AnalysisInput(projectId, geometry, parameters), ct);
            var classified = engineResult.Values
                .Select(v => new AreaValue(v.PolygonIndex, v.Area, v.Value, classification.Classify(v.Value)))
                .ToList();
            var summary = classification.Summarize(classified.Select(v => v.Value));

            run.Status = AnalysisStatus.Succeeded;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Result = new AnalysisResult
            {
                AnalysisRunId = run.Id,
                Metric = engineResult.Metric,
                Unit = engineResult.Unit,
                ValuesJson = JsonSerializer.Serialize(classified.Select(v => new
                {
                    polygonIndex = v.PolygonIndex, area = v.Area, value = v.Value,
                    classification = v.Classification.ToString(),
                }), JsonOptions),
                ClassificationSummaryJson = JsonSerializer.Serialize(new
                {
                    acceptable = summary[ProblemClass.Acceptable],
                    moderate = summary[ProblemClass.Moderate],
                    problemArea = summary[ProblemClass.ProblemArea],
                }, JsonOptions),
                IsEstimated = engineResult.IsEstimated,
                MethodProvenance = engineResult.MethodProvenance,
                CreatedBy = ownerId,
            };
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Lost the insert race against an identical request: the filtered unique
                // index on (ProjectId, InputHash, Succeeded) rejected us, so replay the winner.
                db.ChangeTracker.Clear();
                var winnerId = await db.AnalysisRuns
                    .Where(r => r.ProjectId == projectId && r.InputHash == inputHash
                        && r.Status == AnalysisStatus.Succeeded)
                    .Select(r => (Guid?)r.Id)
                    .SingleOrDefaultAsync(ct);
                if (winnerId is not null)
                    return (await LoadAsync(ownerId, winnerId.Value, ct), false);
                throw;
            }
        }
        catch (Exception ex) when (ex is not AnalysisException)
        {
            // Never partial-valid: persist the failure, then report it.
            run.Status = AnalysisStatus.Failed;
            run.ErrorCode = "ANALYSIS_FAILED";
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            throw AnalysisException.Failed(ex.Message);
        }

        return (await LoadAsync(ownerId, run.Id, ct), true);
    }

    public async Task<AnalysisRunResponse> GetAsync(Guid ownerId, Guid runId, CancellationToken ct = default) =>
        await LoadAsync(ownerId, runId, ct);

    private async Task<AnalysisRunResponse> LoadAsync(Guid ownerId, Guid runId, CancellationToken ct)
    {
        var run = await db.AnalysisRuns
            .Include(r => r.Result)
            .SingleOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw AnalysisException.NotFound("Analysis run", runId);
        var owner = await db.Projects
            .Where(p => p.Id == run.ProjectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct);
        if (owner is null || owner != ownerId)
            throw owner is null
                ? AnalysisException.NotFound("Analysis run", runId)
                : AnalysisException.Forbidden();
        if (run.Result is null)
            throw AnalysisException.Failed("run has no result.");

        var values = JsonSerializer.Deserialize<List<AreaValueDto>>(run.Result.ValuesJson, JsonOptions) ?? [];
        var summary = JsonSerializer.Deserialize<Dictionary<string, int>>(
            run.Result.ClassificationSummaryJson ?? "{}", JsonOptions) ?? [];
        return new AnalysisRunResponse(
            run.Id, run.ProjectId, run.EngineeringFileId, run.ScenarioId, run.Status.ToString(),
            run.EngineName, run.EngineVersion, run.ConfigVersion, run.InputHash,
            run.Result.Metric, run.Result.Unit, run.Result.IsEstimated,
            values, summary, run.CreatedAt);
    }

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
