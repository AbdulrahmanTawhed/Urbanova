using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Urbanova.Domain;
using Urbanova.Domain.Entities;
using Urbanova.Infrastructure.Persistence;

namespace Urbanova.IntegrationTests;

/// <summary>
/// Phase 2: EF mappings against LocalDB (requires MSSQLLocalDB running).
/// Uses a dedicated Urbanova_Test database; each test rebuilds schema via migrations.
/// </summary>
public sealed class PersistenceTests : IAsyncLifetime
{
    private const string TestCs =
        "Server=(localdb)\\MSSQLLocalDB;Database=Urbanova_Test;Trusted_Connection=True;TrustServerCertificate=True";

    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(TestCs, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureDeletedAsync();
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static Project NewProject(Guid? ownerId = null) => new()
    {
        OwnerId = ownerId ?? Guid.NewGuid(),
        Name = "Test Project",
        Status = ProjectStatus.Draft
    };

    [Fact]
    public async Task CanPersistProjectWithSite()
    {
        var project = NewProject();
        project.Site = new Site { Address = "Test St 1", Crs = "EPSG:4326", AreaM2 = 1500 };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var loaded = await _db.Projects.Include(p => p.Site).FirstAsync(p => p.Id == project.Id);
        loaded.Site.Should().NotBeNull();
        loaded.Site!.Address.Should().Be("Test St 1");
        loaded.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task FileHash_IsUniquePerProject()
    {
        var owner = Guid.NewGuid();
        var p1 = NewProject(owner);
        var p2 = NewProject(Guid.NewGuid());
        _db.Projects.AddRange(p1, p2);
        await _db.SaveChangesAsync();

        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        _db.EngineeringFiles.Add(new EngineeringFile
        {
            ProjectId = p1.Id, UploadedBy = owner, FileName = "a.geojson",
            ContentType = "application/geo+json", SizeBytes = 100,
            StoragePath = "AppData/uploads/x/a.geojson", HashSha256 = hash
        });
        await _db.SaveChangesAsync();

        // Same hash, same project -> violation.
        _db.EngineeringFiles.Add(new EngineeringFile
        {
            ProjectId = p1.Id, UploadedBy = owner, FileName = "b.geojson",
            ContentType = "application/geo+json", SizeBytes = 100,
            StoragePath = "AppData/uploads/x/b.geojson", HashSha256 = hash
        });
        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CanPersistFullAnalysisGraph()
    {
        var owner = Guid.NewGuid();
        var project = NewProject(owner);
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var file = new EngineeringFile
        {
            ProjectId = project.Id, UploadedBy = owner, FileName = "site.geojson",
            ContentType = "application/geo+json", SizeBytes = 42,
            StoragePath = "AppData/uploads/x/site.geojson",
            HashSha256 = new string('a', 64), ValidationStatus = FileValidationStatus.Valid
        };
        _db.EngineeringFiles.Add(file);
        await _db.SaveChangesAsync();

        var run = new AnalysisRun
        {
            ProjectId = project.Id, EngineeringFileId = file.Id,
            EngineName = "HeatV01", EngineVersion = "0.1.0-mvp", ConfigVersion = "mvp-001",
            ConfigSnapshotJson = "{}", InputSnapshotJson = "{}",
            InputHash = new string('b', 64), Status = AnalysisStatus.Succeeded
        };
        _db.AnalysisRuns.Add(run);
        await _db.SaveChangesAsync();

        _db.AnalysisResults.Add(new AnalysisResult
        {
            AnalysisRunId = run.Id, Metric = "LandSurfaceTempProxy",
            Unit = "Celsius", ValuesJson = "{}", IsEstimated = true
        });

        var baseline = new Scenario
        {
            ProjectId = project.Id, BaseAnalysisRunId = run.Id,
            Kind = ScenarioKind.Baseline, Name = "Baseline",
            ParametersJson = "{}", IsLocked = true
        };
        _db.Scenarios.Add(baseline);
        await _db.SaveChangesAsync();

        var count = await _db.Scenarios.CountAsync(s => s.ProjectId == project.Id);
        count.Should().Be(1);

        // Restrict delete: project with children must NOT cascade-delete silently.
        // Clear the tracker so the delete hits the DB FK constraint (not EF's client-side null check).
        _db.ChangeTracker.Clear();
        _db.Projects.Remove(new Project { Id = project.Id });
        var del = () => _db.SaveChangesAsync();
        await del.Should().ThrowAsync<DbUpdateException>();
    }
}
