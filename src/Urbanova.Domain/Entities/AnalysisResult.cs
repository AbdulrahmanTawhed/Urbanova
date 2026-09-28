using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>1:1 with AnalysisRun. ValuesJson holds heat values per design area (flexible by design).</summary>
public sealed class AnalysisResult : EntityBase
{
    public Guid AnalysisRunId { get; set; }

    public AnalysisRun? AnalysisRun { get; set; }

    public string Metric { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string ValuesJson { get; set; } = "{}";

    public string? ClassificationSummaryJson { get; set; }

    /// <summary>True until validated measurements replace MVP estimates.</summary>
    public bool IsEstimated { get; set; } = true;

    public string? MethodProvenance { get; set; }
}
