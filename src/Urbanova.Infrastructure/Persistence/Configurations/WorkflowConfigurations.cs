using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Urbanova.Domain.Entities;

namespace Urbanova.Infrastructure.Persistence.Configurations;

public sealed class AnalysisRunConfiguration : IEntityTypeConfiguration<AnalysisRun>
{
    public void Configure(EntityTypeBuilder<AnalysisRun> b)
    {
        b.ToTable("AnalysisRuns");
        b.HasKey(x => x.Id);
        b.Property(x => x.EngineName).IsRequired().HasMaxLength(100);
        b.Property(x => x.EngineVersion).IsRequired().HasMaxLength(50);
        b.Property(x => x.ConfigVersion).IsRequired().HasMaxLength(50);
        b.Property(x => x.ConfigSnapshotJson).IsRequired().HasColumnType("nvarchar(max)");
        b.Property(x => x.InputSnapshotJson).IsRequired().HasColumnType("nvarchar(max)");
        b.Property(x => x.InputHash).IsRequired().HasMaxLength(64).IsFixedLength();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.ErrorCode).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.ProjectId);
        // Reproducibility lookup + concurrent-insert guard: one succeeded run per
        // (project, inputs+config). Failed/Running duplicates stay possible by design.
        b.HasIndex(x => new { x.ProjectId, x.InputHash }).IsUnique().HasFilter("[Status] = 2");
        b.HasOne(x => x.EngineeringFile).WithMany()
            .HasForeignKey(x => x.EngineeringFileId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Scenario).WithMany()
            .HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Result).WithOne(x => x.AnalysisRun)
            .HasForeignKey<AnalysisResult>(x => x.AnalysisRunId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AnalysisResultConfiguration : IEntityTypeConfiguration<AnalysisResult>
{
    public void Configure(EntityTypeBuilder<AnalysisResult> b)
    {
        b.ToTable("AnalysisResults");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.AnalysisRunId).IsUnique();
        b.Property(x => x.Metric).IsRequired().HasMaxLength(100);
        b.Property(x => x.Unit).IsRequired().HasMaxLength(50);
        b.Property(x => x.ValuesJson).IsRequired().HasColumnType("nvarchar(max)");
        b.Property(x => x.ClassificationSummaryJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.MethodProvenance).HasMaxLength(500);
    }
}

public sealed class ScenarioConfiguration : IEntityTypeConfiguration<Scenario>
{
    public void Configure(EntityTypeBuilder<Scenario> b)
    {
        b.ToTable("Scenarios");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Kind).HasConversion<int>().IsRequired();
        b.Property(x => x.ParametersJson).IsRequired().HasColumnType("nvarchar(max)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.ProjectId);
        b.HasIndex(x => new { x.ProjectId, x.Kind });
        // Alternative inherits baseline (self-FK, no cascade to protect baseline).
        b.HasOne(x => x.ParentScenario).WithMany(x => x.ChildScenarios)
            .HasForeignKey(x => x.ParentScenarioId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.BaseAnalysisRun).WithMany()
            .HasForeignKey(x => x.BaseAnalysisRunId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class RecommendationConfiguration : IEntityTypeConfiguration<Recommendation>
{
    public void Configure(EntityTypeBuilder<Recommendation> b)
    {
        b.ToTable("Recommendations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Problem).IsRequired().HasMaxLength(1000);
        b.Property(x => x.Cause).IsRequired().HasMaxLength(1000);
        b.Property(x => x.Intervention).IsRequired().HasMaxLength(2000);
        b.Property(x => x.ExpectedImpactJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.EvidenceSource).HasMaxLength(500);
        b.Property(x => x.EvidenceLevel).HasConversion<int>().IsRequired();
        b.Property(x => x.Feasibility).HasMaxLength(1000);
        b.HasIndex(x => x.ProjectId);
        b.HasOne(x => x.AnalysisRun).WithMany()
            .HasForeignKey(x => x.AnalysisRunId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Scenario).WithMany()
            .HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.CostEstimate).WithMany()
            .HasForeignKey(x => x.CostEstimateId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class CostEstimateConfiguration : IEntityTypeConfiguration<CostEstimate>
{
    public void Configure(EntityTypeBuilder<CostEstimate> b)
    {
        b.ToTable("CostEstimates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.Unit).IsRequired().HasMaxLength(50);
        b.Property(x => x.UnitPrice).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("USD");
        b.Property(x => x.PriceSource).HasMaxLength(500);
        b.Property(x => x.Total).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.HasIndex(x => x.ProjectId);
        b.HasOne(x => x.Recommendation).WithMany()
            .HasForeignKey(x => x.RecommendationId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Scenario).WithMany()
            .HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> b)
    {
        b.ToTable("Reports");
        b.HasKey(x => x.Id);
        b.Property(x => x.Format).HasConversion<int>().IsRequired();
        b.Property(x => x.StoragePath).HasMaxLength(1000);
        b.Property(x => x.ContentJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength();
        b.HasIndex(x => x.ProjectId);
        // Per-project versions are allocated as Max+1 with retry; the unique index
        // turns a lost race into a catchable constraint violation instead of duplicates.
        b.HasIndex(x => new { x.ProjectId, x.Version }).IsUnique();
    }
}
