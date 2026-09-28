using Microsoft.Extensions.Options;

namespace Urbanova.Application.Scenarios;

/// <summary>
/// Validates parameter sets against the configured catalog (PRD v0.2 §8, §12).
/// Throws coded <see cref="ScenarioException"/> — never silently ignores input.
/// Must run on merged sets (run + request params), not just raw request DTOs.
/// </summary>
public sealed class ScenarioParameterCatalog(IOptions<ScenarioParameterOptions> options)
{
    private readonly ScenarioParameterOptions _options = options.Value;

    public IReadOnlyDictionary<string, ScenarioParameterDefinition> Allowed =>
        _options.Parameters
            .Where(p => p.Allowed && !string.IsNullOrWhiteSpace(p.Key))
            .ToDictionary(p => p.Key, p => p, StringComparer.Ordinal);

    public void Validate(IDictionary<string, double> parameters, string context = "parameters")
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Count > _options.MaxParameters)
            throw ScenarioException.TooManyParameters(parameters.Count, _options.MaxParameters);

        var allowed = Allowed;
        foreach (var (key, value) in parameters)
        {
            if (!allowed.TryGetValue(key, out var def))
                throw ScenarioException.UnsupportedParameter(key);
            if (double.IsNaN(value) || double.IsInfinity(value) || value < def.Min || value > def.Max)
                throw ScenarioException.InvalidParameter(key, value, def.Min, def.Max, def.Unit);
        }
    }
}
