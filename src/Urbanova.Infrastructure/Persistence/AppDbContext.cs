using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Urbanova.Domain.Entities;
using Urbanova.Domain.Interfaces;

namespace Urbanova.Infrastructure.Persistence;

/// <summary>
/// EF Core context: Identity tables + URBANOVA aggregates. Applies all
/// IEntityTypeConfiguration in this assembly (Configurations/).
/// Implements IUnitOfWork and stamps CreatedAt/UpdatedAt automatically.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<UrbanovaIdentityUser, IdentityRole<Guid>, Guid>(options), IUnitOfWork
{
    public DbSet<User> DomainUsers => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<EngineeringFile> EngineeringFiles => Set<EngineeringFile>();
    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();
    public DbSet<AnalysisResult> AnalysisResults => Set<AnalysisResult>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<CostEstimate> CostEstimates => Set<CostEstimate>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var e in ChangeTracker.Entries<Domain.Common.EntityBase>())
        {
            if (e.State == EntityState.Added)
            {
                e.Entity.CreatedAt = now;
                e.Entity.UpdatedAt = now;
            }
            else if (e.State == EntityState.Modified)
            {
                e.Entity.UpdatedAt = now;
            }
        }
        return base.SaveChangesAsync(ct);
    }

    async Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken ct) => await SaveChangesAsync(ct);
}
