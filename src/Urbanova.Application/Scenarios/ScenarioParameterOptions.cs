namespace Urbanova.Application.Scenarios;

/// <summary>
/// Configurable MVP scenario parameter catalog (PRD v0.2 §7–§8). Binds to
/// "ScenarioParameters". The approved 1–3 parameters change here — never in code.
/// Existing camelCase keys are preserved; PRD snake_case names are documented
/// aliases in docs/assumptions.md, not separate keys.
/// </summary>
public sealed class ScenarioParameterDefinition
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public double Min { get; set; }

    public double Max { get; set; }

    public bool Allowed { get; set; } = true;
}

public sealed class ScenarioParameterOptions
{
    public const string SectionName = "ScenarioParameters";

    public int MaxParameters { get; set; } = 3;

    public List<ScenarioParameterDefinition> Parameters { get; set; } = [];
}
