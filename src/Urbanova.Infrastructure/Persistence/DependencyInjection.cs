using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Urbanova.Application.Analysis;
using Urbanova.Application.Comparison;
using Urbanova.Application.Costing;
using Urbanova.Application.Reporting;
using Urbanova.Application.EngineeringFiles;
using Urbanova.Application.Projects;
using Urbanova.Application.Recommendations;
using Urbanova.Application.Scenarios;
using Urbanova.Domain.Analysis;
using Urbanova.Domain.Comparison;
using Urbanova.Domain.Costing;
using Urbanova.Domain.Interfaces;
using Urbanova.Domain.Recommendations;
using Urbanova.Domain.Reporting;
using Urbanova.Infrastructure.Analysis;
using Urbanova.Infrastructure.Comparison;
using Urbanova.Infrastructure.Costing;
using Urbanova.Infrastructure.Reporting;
using Urbanova.Infrastructure.FileProcessing;
using Urbanova.Infrastructure.Projects;
using Urbanova.Infrastructure.Recommendations;
using Urbanova.Infrastructure.Scenarios;

namespace Urbanova.Infrastructure.Persistence;

/// <summary>
/// Persistence wiring (+ module services that need AppDbContext; per-module DI
/// extensions split out from Phase 5 as the service count grows).
/// SQL Server health probe is registered by the Api project (owns the
/// AspNetCore.HealthChecks.SqlServer dependency) — see Program.cs.
/// </summary>
public static class DependencyInjection
{
    public const string ConnectionStringName = "UrbanovaDb";

    /// <returns>Connection string when SQL Server was registered; null when skipped (no CS configured).</returns>
    public static string? AddUrbanovaPersistence(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(cs))
            return null;

        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(cs, sql =>
        {
            sql.EnableRetryOnFailure();
            sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        }));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IProjectService, ProjectService>(); // Phase 4
        // Phase 5: file processing (register more IEngineeringFileProcessor impls to add formats).
        services.Configure<FileStorageOptions>(config.GetSection(FileStorageOptions.SectionName));
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IEngineeringFileProcessor, GeoJsonFileProcessor>();
        services.AddScoped<IProcessorRegistry, ProcessorRegistry>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<IGeometryService, GeometryService>(); // Phase 6
        // Phase 7: environmental analysis (config-driven; swap engines via IEnvironmentalAnalysisEngine).
        services.Configure<HeatAnalysisOptions>(config.GetSection(HeatAnalysisOptions.SectionName));
        services.Configure<ClassificationOptions>(config.GetSection(ClassificationOptions.SectionName));
        services.AddScoped<IEnvironmentalAnalysisEngine, HeatV01Engine>();
        services.AddScoped<IEnvironmentalClassificationService, ThresholdClassificationService>();
        services.AddScoped<IAnalysisService, AnalysisService>();
        // Phase 8: scenarios (validators live beside the service; Api registers them explicitly).
        services.Configure<ScenarioParameterOptions>(config.GetSection(ScenarioParameterOptions.SectionName));
        services.AddScoped<ScenarioParameterCatalog>();
        services.AddScoped<IScenarioService, ScenarioService>();
        // Phase 9: comparison (methodology swappable via IScenarioComparisonService).
        services.AddScoped<IScenarioComparisonService, ComparisonEngine>();
        services.AddScoped<IComparisonService, ComparisonService>();
        // Phase 10: recommendations (rules swappable via IRecommendationEngine).
        services.AddScoped<IRecommendationEngine, RuleRecommendationEngine>();
        services.AddScoped<IRecommendationService, RecommendationService>();
        // Phase 11: cost estimation (catalog swappable via IPriceCatalog).
        services.Configure<PriceCatalogOptions>(config.GetSection(PriceCatalogOptions.SectionName));
        services.AddScoped<IPriceCatalog, FilePriceCatalog>();
        services.AddScoped<ICostService, CostService>();
        // Phase 12: reporting (formats swappable via IReportGenerator).
        services.Configure<ReportingOptions>(config.GetSection(ReportingOptions.SectionName));
        services.AddScoped<IReportGenerator, JsonReportGenerator>();
        services.AddScoped<IReportGenerator, HtmlReportGenerator>();
        services.AddScoped<IReportFileStore, LocalReportFileStore>();
        services.AddScoped<IReportService, ReportService>();
        return cs;
    }
}
