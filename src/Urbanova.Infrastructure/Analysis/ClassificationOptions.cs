namespace Urbanova.Infrastructure.Analysis;

/// <summary>
/// Binds to "Classification". TEMPORARY thresholds pending validation (assumptions.md §5).
/// </summary>
public sealed class ClassificationOptions
{
    public const string SectionName = "Classification";

    public double AcceptableBelow { get; set; } = 30.0;

    public double ModerateBelow { get; set; } = 35.0;
}
