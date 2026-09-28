using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>
/// Registered engineering rule backing recommendations (PRD v0.2 §5). A recommendation
/// is traceable to its evidence through this row — never through free text alone.
/// Seeded with the MVP rule set; unknown or inactive codes block generation (NO_EVIDENCE).
/// </summary>
public sealed class RecommendationRule : EntityBase
{
    /// <summary>Stable rule code cited by engines, e.g. "HEAT-VEG-001". Unique.</summary>
    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>JSON array of scientific references, e.g. ["..."].</summary>
    public string? ScientificReferencesJson { get; set; }

    /// <summary>What the expected-impact calculation is based on.</summary>
    public string? ImpactBasis { get; set; }

    public string? EngineName { get; set; }

    public string? EngineVersion { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();
}
