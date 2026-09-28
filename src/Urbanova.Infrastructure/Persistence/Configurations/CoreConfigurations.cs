using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Urbanova.Domain.Entities;

namespace Urbanova.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).IsRequired().HasMaxLength(256);
        b.HasIndex(x => x.Email).IsUnique();
        b.Property(x => x.DisplayName).HasMaxLength(200);
    }
}

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.ToTable("Projects");
        b.HasKey(x => x.Id);
        b.Property(x => x.OwnerId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.OwnerId);
        b.HasIndex(x => new { x.OwnerId, x.Name });

        // SQL Server forbids multiple cascade paths (Project -> Runs + Project -> Scenarios
        // with cross-FKs between them), and PRD §19 requires failed ops to never destroy
        // project data. Only the 1:1 Site cascades; all other children use Restrict so
        // the application layer deletes explicitly in dependency order.
        b.HasOne(x => x.Site).WithOne(x => x.Project)
            .HasForeignKey<Site>(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Files).WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Scenarios).WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.AnalysisRuns).WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Recommendations).WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.CostEstimates).WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Reports).WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> b)
    {
        b.ToTable("Sites");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.ProjectId).IsUnique();
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.BoundaryGeoJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.Crs).IsRequired().HasMaxLength(50).HasDefaultValue("EPSG:4326");
    }
}

public sealed class EngineeringFileConfiguration : IEntityTypeConfiguration<EngineeringFile>
{
    public void Configure(EntityTypeBuilder<EngineeringFile> b)
    {
        b.ToTable("EngineeringFiles");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).IsRequired().HasMaxLength(260);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(200);
        b.Property(x => x.StoragePath).IsRequired().HasMaxLength(1000);
        b.Property(x => x.FormatDetected).HasMaxLength(100);
        b.Property(x => x.HashSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        b.Property(x => x.ValidationStatus).HasConversion<int>().IsRequired();
        b.Property(x => x.ValidationErrorsJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
        // Dedupe: same content hash cannot repeat within one project.
        b.HasIndex(x => new { x.ProjectId, x.HashSha256 }).IsUnique();
        b.HasIndex(x => x.ProjectId);
    }
}
