using Microsoft.Extensions.Options;

namespace Urbanova.Application.Scenarios;

/// <summary>
/// Fail-fast validation for the scenario parameter catalog (PRD v0.2 §8). A hand-edited
/// config mistake (duplicate key, inverted band, negative cap) must surface at startup,
/// not as per-request 500s from every analysis/scenario call.
/// </summary>
public sealed class ScenarioParameterOptionsValidator : IValidateOptions<ScenarioParameterOptions>
{
    public ValidateOptionsResult Validate(string? name, ScenarioParameterOptions options)
    {
        var failures = new List<string>();
        if (options.MaxParameters < 0)
            failures.Add($"MaxParameters must be >= 0 (was {options.MaxParameters}).");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in options.Parameters)
        {
            if (string.IsNullOrWhiteSpace(p.Key))
                failures.Add("Every parameter needs a non-empty Key.");
            else if (!seen.Add(p.Key))
                failures.Add($"Duplicate parameter Key '{p.Key}'.");
            if (p.Min > p.Max)
                failures.Add($"Parameter '{p.Key}': Min ({p.Min}) exceeds Max ({p.Max}).");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
