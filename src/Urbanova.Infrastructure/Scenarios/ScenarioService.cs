using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Urbanova.Application.Analysis;
using Urbanova.Application.Scenarios;
using Urbanova.Domain;
using Urbanova.Domain.BusinessRules;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.Scenarios;

/// <summary>
/// Scenario use cases (Phase 8). Owner-scoped throughout. Baselines lock at creation
/// (never silently overwritten); alternatives inherit via <see cref="ScenarioRules"/>
/// with request parameters merged over the baseline's. Updates bump Version with
/// RowVersion concurrency. Analyze delegates to the shared analysis pipeline.
/// </summary>
public sealed class ScenarioService(
    AppDbContext db,
    IValidator<CreateScenarioRequest> createValidator,
    IValidator<UpdateScenarioRequest> updateValidator,
    IAnalysisService analysis) : IScenarioService
{
    public async Task<ScenarioResponse> CreateAsync(
        Guid ownerId, Guid projectId, CreateScenarioRequest request, CancellationToken ct = default)
    {
        var validation = await createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            throw ScenarioException.Invalid(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

        var owner = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct)
            ?? throw ScenarioException.Invalid($"Project '{projectId}' was not found.");
        if (owner != ownerId)
            throw ScenarioException.Forbidden();

        // Start from the baseline run's preserved inputs when linked.
        var parameters = new Dictionary<string, double>(StringComparer.Ordinal);
        Guid? baseRunId = null;
        if (request.BaseAnalysisRunId is not null)
        {
            var run = await db.AnalysisRuns.SingleOrDefaultAsync(r => r.Id == request.BaseAnalysisRunId, ct);
            if (run is null || run.ProjectId != projectId)
                throw ScenarioException.Invalid($"Baseline analysis run '{request.BaseAnalysisRunId}' was not found in this project.");
            baseRunId = run.Id;
            foreach (var (k, v) in ExtractParameters(run.InputSnapshotJson))
                parameters[k] = v;
        }

        Scenario scenario;
        if (request.ParentScenarioId is null)
        {
            ProjectRules.ValidateName(request.Name);
            if (request.Parameters is not null)
                foreach (var (k, v) in request.Parameters)
                    parameters[k] = v;
            scenario = new Scenario
            {
                ProjectId = projectId,
                BaseAnalysisRunId = baseRunId,
                Kind = ScenarioKind.Baseline,
                Name = request.Name.Trim(),
                ParametersJson = JsonSerializer.Serialize(parameters),
                IsLocked = true, // baselines never silently overwritten (PRD §11)
                Version = 1,
                CreatedBy = ownerId,
            };
        }
        else
        {
            var parent = await db.Scenarios.SingleOrDefaultAsync(s => s.Id == request.ParentScenarioId, ct);
            if (parent is null || parent.ProjectId != projectId)
                throw ScenarioException.Invalid($"Parent scenario '{request.ParentScenarioId}' was not found in this project.");
            if (parent.Kind != ScenarioKind.Baseline)
                throw ScenarioException.Invalid("Alternatives can only inherit from a baseline scenario.");
            scenario = ScenarioRules.CreateAlternative(parent, request.Name);
            // Merge order: parent params → explicitly linked run params → request params win.
            // (`parameters` already holds the linked run's preserved inputs from above.)
            var merged = JsonSerializer.Deserialize<Dictionary<string, double>>(parent.ParametersJson) ?? [];
            foreach (var (k, v) in parameters)
                merged[k] = v;
            if (request.Parameters is not null)
                foreach (var (k, v) in request.Parameters)
                    merged[k] = v;
            scenario.ParametersJson = JsonSerializer.Serialize(merged);
            scenario.BaseAnalysisRunId = baseRunId ?? parent.BaseAnalysisRunId;
        }

        db.Scenarios.Add(scenario);
        await db.SaveChangesAsync(ct);
        return await LoadAsync(scenario.Id, ct);
    }

    public async Task<IReadOnlyList<ScenarioResponse>> ListAsync(Guid ownerId, Guid projectId, CancellationToken ct = default)
    {
        var owner = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct)
            ?? throw ScenarioException.Invalid($"Project '{projectId}' was not found.");
        if (owner != ownerId)
            throw ScenarioException.Forbidden();

        var scenarios = await db.Scenarios
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync(ct);
        return [.. scenarios.Select(ToResponse)];
    }

    public async Task<ScenarioResponse> GetAsync(Guid ownerId, Guid scenarioId, CancellationToken ct = default)
    {
        var scenario = await db.Scenarios.SingleOrDefaultAsync(s => s.Id == scenarioId, ct)
            ?? throw ScenarioException.NotFound(scenarioId);
        await OwnsAsync(ownerId, scenario.ProjectId, scenarioId, ct);
        return ToResponse(scenario);
    }

    public async Task<ScenarioResponse> UpdateAsync(
        Guid ownerId, Guid scenarioId, UpdateScenarioRequest request, CancellationToken ct = default)
    {
        var validation = await updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            throw ScenarioException.Invalid(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

        var scenario = await db.Scenarios.SingleOrDefaultAsync(s => s.Id == scenarioId, ct)
            ?? throw ScenarioException.NotFound(scenarioId);
        await OwnsAsync(ownerId, scenario.ProjectId, scenarioId, ct);

        if (scenario is { Kind: ScenarioKind.Baseline, IsLocked: true })
            throw ScenarioException.Locked();

        db.Entry(scenario).Property(s => s.RowVersion).OriginalValue = request.RowVersion;
        if (request.Name is not null)
        {
            ProjectRules.ValidateName(request.Name);
            scenario.Name = request.Name.Trim();
        }
        if (request.Parameters is not null)
            scenario.ParametersJson = JsonSerializer.Serialize(
                new SortedDictionary<string, double>(request.Parameters, StringComparer.Ordinal));
        scenario.Version++;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ScenarioException.ConcurrencyConflict();
        }

        db.ChangeTracker.Clear();
        return await LoadAsync(scenarioId, ct);
    }

    public async Task<(AnalysisRunResponse Response, bool IsNew)> AnalyzeAsync(
        Guid ownerId, Guid scenarioId, AnalyzeScenarioRequest request, CancellationToken ct = default)
    {
        // Ownership + existence verified inside; AnalysisException (404/403/422) maps in Api.
        try
        {
            return await analysis.RunForScenarioAsync(ownerId, scenarioId, request.FileId, ct);
        }
        catch (AnalysisException ex) when (ex.ErrorCode is "NOT_FOUND" or "FORBIDDEN")
        {
            // Re-scope analysis errors to scenario semantics without leaking.
            throw ex.ErrorCode == "NOT_FOUND"
                ? ScenarioException.NotFound(scenarioId)
                : ScenarioException.Forbidden();
        }
    }

    private async Task OwnsAsync(Guid ownerId, Guid projectId, Guid scenarioId, CancellationToken ct)
    {
        var owner = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct);
        if (owner is null || owner != ownerId)
            throw owner is null ? ScenarioException.NotFound(scenarioId) : ScenarioException.Forbidden();
    }

    private async Task<ScenarioResponse> LoadAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var scenario = await db.Scenarios.SingleAsync(s => s.Id == id, ct);
        return ToResponse(scenario);
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
            // Corrupt snapshot → empty params (analysis re-validates); never throw here.
        }
        return [];
    }

    private static ScenarioResponse ToResponse(Scenario s) => new(
        s.Id, s.ProjectId, s.Kind.ToString(), s.Name,
        JsonSerializer.Deserialize<Dictionary<string, double>>(s.ParametersJson) ?? [],
        s.IsLocked, s.Version, s.ParentScenarioId, s.BaseAnalysisRunId,
        s.RowVersion, s.CreatedAt, s.UpdatedAt);
}
