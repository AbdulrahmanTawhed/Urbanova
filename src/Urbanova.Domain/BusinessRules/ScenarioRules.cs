using Urbanova.Domain.Entities;

namespace Urbanova.Domain.BusinessRules;

/// <summary>Baseline immutability + versioning (PRD §11). Baseline must never be silently overwritten.</summary>
public static class ScenarioRules
{
    public static void EnsureMutable(Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (scenario is { Kind: ScenarioKind.Baseline, IsLocked: true })
            throw new InvalidOperationException("Baseline scenario is locked and cannot be modified. Create an alternative instead.");
    }

    public static Scenario CreateAlternative(Scenario baseline, string name)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.Kind != ScenarioKind.Baseline)
            throw new InvalidOperationException("Alternatives can only inherit from a baseline scenario.");
        ProjectRules.ValidateName(name);
        return new Scenario
        {
            ProjectId = baseline.ProjectId,
            ParentScenarioId = baseline.Id,
            BaseAnalysisRunId = baseline.BaseAnalysisRunId,
            Kind = ScenarioKind.Alternative,
            Name = name,
            ParametersJson = baseline.ParametersJson,
            IsLocked = false,
            Version = 1,
            CreatedBy = baseline.CreatedBy
        };
    }
}
