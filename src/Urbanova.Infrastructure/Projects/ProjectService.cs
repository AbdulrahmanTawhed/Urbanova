using Microsoft.EntityFrameworkCore;
using Urbanova.Application.Projects;
using Urbanova.Domain;
using Urbanova.Domain.BusinessRules;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.Infrastructure.Projects;

/// <summary>
/// Project use cases. Ownership is enforced here (defense in depth — controllers
/// additionally authorize via the MustOwnProject policy before calling in).
/// Deletes proceed in explicit dependency order because Project-child FKs are
/// RESTRICT (PRD §19: failures must never cascade-destroy project data).
/// </summary>
public sealed class ProjectService(AppDbContext db) : IProjectService
{
    public const int MaxPageSize = 100;

    public async Task<ProjectResponse> CreateAsync(Guid ownerId, CreateProjectRequest request, CancellationToken ct = default)
    {
        ProjectRules.ValidateName(request.Name);

        var project = new Project
        {
            OwnerId = ownerId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Status = ProjectStatus.Draft,
            CreatedBy = ownerId,
        };
        if (request.Site is not null)
            project.Site = ToSite(request.Site, ownerId);

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return await LoadAsync(project.Id, ct)
            ?? throw ProjectException.NotFound(project.Id);
    }

    public async Task<PagedResult<ProjectResponse>> ListAsync(Guid ownerId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Projects
            .Where(p => p.OwnerId == ownerId)
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id);

        var total = await query.CountAsync(ct);
        var ids = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => p.Id)
            .ToListAsync(ct);

        // Materialize before mapping: enum ToString() has no SQL translation.
        List<ProjectResponse> items = ids.Count == 0
            ? []
            : (await db.Projects
                .Where(p => ids.Contains(p.Id))
                .Include(p => p.Site)
                .OrderByDescending(p => p.CreatedAt)
                .ThenBy(p => p.Id)
                .ToListAsync(ct))
                .Select(ToResponse)
                .ToList();

        return new PagedResult<ProjectResponse>(
            items, page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<ProjectResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var project = await db.Projects
            .Include(p => p.Site)
            .SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw ProjectException.NotFound(id);
        if (project.OwnerId != ownerId)
            throw ProjectException.Forbidden();
        return ToResponse(project);
    }

    public async Task<ProjectResponse> UpdateAsync(Guid ownerId, Guid id, UpdateProjectRequest request, CancellationToken ct = default)
    {
        ProjectRules.ValidateName(request.Name);

        var project = await db.Projects
            .Include(p => p.Site)
            .SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw ProjectException.NotFound(id);
        if (project.OwnerId != ownerId)
            throw ProjectException.Forbidden();

        // Optimistic concurrency: mismatch with the Get-provided RowVersion → 409.
        db.Entry(project).Property(p => p.RowVersion).OriginalValue = request.RowVersion;

        project.Name = request.Name.Trim();
        project.Description = request.Description?.Trim();
        if (request.Status is not null)
        {
            // Validator guarantees parsability; TryParse keeps casing-tolerant input from
            // ever surfacing as an unstructured 400 from Enum.Parse.
            if (!Enum.TryParse<ProjectStatus>(request.Status, ignoreCase: true, out var status))
                throw new ArgumentException($"Unknown project status '{request.Status}'.", nameof(request));
            project.Status = status;
        }

        if (request.Site is not null)
        {
            if (project.Site is null)
            {
                project.Site = ToSite(request.Site, ownerId);
                project.Site.ProjectId = project.Id;
            }
            else
            {
                ApplySite(project.Site, request.Site);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ProjectException.ConcurrencyConflict();
        }

        db.ChangeTracker.Clear();
        return await LoadAsync(id, ct) ?? throw ProjectException.NotFound(id);
    }

    public async Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var owner = await db.Projects
            .Where(p => p.Id == id)
            .Select(p => (Guid?)p.OwnerId)
            .SingleOrDefaultAsync(ct);
        if (owner is null)
            throw ProjectException.NotFound(id);
        if (owner != ownerId)
            throw ProjectException.Forbidden();

        // Explicit dependency order (all FKs RESTRICT except Site/Result cascades),
        // executed atomically: a mid-sequence failure rolls back instead of leaving
        // a partially destroyed project. Runs inside the execution strategy because
        // EnableRetryOnFailure forbids raw user-initiated transactions.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Reports.Where(r => r.ProjectId == id).ExecuteDeleteAsync(ct);
            await db.Recommendations.Where(r => r.ProjectId == id).ExecuteDeleteAsync(ct);
            await db.CostEstimates.Where(c => c.ProjectId == id).ExecuteDeleteAsync(ct);
            // Alternatives reference baselines (self-FK RESTRICT): delete children first.
            await db.Scenarios.Where(s => s.ProjectId == id && s.ParentScenarioId != null).ExecuteDeleteAsync(ct);
            await db.Scenarios.Where(s => s.ProjectId == id).ExecuteDeleteAsync(ct);
            // Results cascade from runs at the DB level; runs' ScenarioId/FileId are SET NULL.
            await db.AnalysisRuns.Where(r => r.ProjectId == id).ExecuteDeleteAsync(ct);
            await db.EngineeringFiles.Where(f => f.ProjectId == id).ExecuteDeleteAsync(ct);
            await db.Sites.Where(s => s.ProjectId == id).ExecuteDeleteAsync(ct);
            await db.Projects.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    private async Task<ProjectResponse?> LoadAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var project = await db.Projects
            .Where(p => p.Id == id)
            .Include(p => p.Site)
            .SingleOrDefaultAsync(ct);
        return project is null ? null : ToResponse(project);
    }

    private static Site ToSite(SiteInput input, Guid ownerId) => new()
    {
        Address = input.Address?.Trim(),
        Latitude = input.Latitude,
        Longitude = input.Longitude,
        BoundaryGeoJson = input.BoundaryGeoJson,
        Crs = string.IsNullOrWhiteSpace(input.Crs) ? "EPSG:4326" : input.Crs!.Trim(),
        AreaM2 = input.AreaM2,
        CreatedBy = ownerId,
    };

    private static void ApplySite(Site site, SiteInput input)
    {
        site.Address = input.Address?.Trim();
        site.Latitude = input.Latitude;
        site.Longitude = input.Longitude;
        site.BoundaryGeoJson = input.BoundaryGeoJson;
        site.Crs = string.IsNullOrWhiteSpace(input.Crs) ? "EPSG:4326" : input.Crs!.Trim();
        site.AreaM2 = input.AreaM2;
    }

    private static ProjectResponse ToResponse(Project p) => new(
        p.Id, p.OwnerId, p.Name, p.Description, p.Status.ToString(),
        p.CreatedAt, p.UpdatedAt, p.RowVersion,
        p.Site is null ? null : new SiteResponse(
            p.Site.Id, p.Site.Address, p.Site.Latitude, p.Site.Longitude,
            p.Site.BoundaryGeoJson, p.Site.Crs, p.Site.AreaM2));
}
