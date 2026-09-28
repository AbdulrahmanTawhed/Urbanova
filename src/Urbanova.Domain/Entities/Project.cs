using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>
/// Root aggregate. Ownership = OwnerId (Identity user Guid).
/// Navigation to Identity user deliberately omitted so Domain stays
/// free of ASP.NET Core Identity (see ArchitectureGuardTests).
/// </summary>
public sealed class Project : EntityBase
{
    public Guid OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;

    public Site? Site { get; set; }

    public ICollection<EngineeringFile> Files { get; set; } = new List<EngineeringFile>();

    public ICollection<Scenario> Scenarios { get; set; } = new List<Scenario>();

    public ICollection<AnalysisRun> AnalysisRuns { get; set; } = new List<AnalysisRun>();

    public ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    public ICollection<CostEstimate> CostEstimates { get; set; } = new List<CostEstimate>();

    public ICollection<Report> Reports { get; set; } = new List<Report>();
}
