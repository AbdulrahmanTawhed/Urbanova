namespace Urbanova.Application.Scenarios;

/// <summary>
/// Scenario failures. Api maps: NOT_FOUND → 404, FORBIDDEN → 403,
/// SCENARIO_LOCKED / CONCURRENCY_CONFLICT → 409,
/// INVALID_SCENARIO / UNSUPPORTED_SCENARIO_PARAMETER / INVALID_SCENARIO_PARAMETER /
/// TOO_MANY_SCENARIO_PARAMETERS → 400.
/// </summary>
public sealed class ScenarioException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    public static ScenarioException NotFound(Guid id) =>
        new("NOT_FOUND", $"Scenario '{id}' was not found.");

    public static ScenarioException Forbidden() =>
        new("FORBIDDEN", "You do not have access to this scenario.");

    public static ScenarioException Locked() =>
        new("SCENARIO_LOCKED",
            "Baseline scenario is locked and cannot be modified. Create an alternative instead.");

    public static ScenarioException ConcurrencyConflict() =>
        new("CONCURRENCY_CONFLICT", "Scenario was modified by another request. Reload and retry.");

    public static ScenarioException Invalid(string detail) =>
        new("INVALID_SCENARIO", detail);

    public static ScenarioException UnsupportedParameter(string key) =>
        new("UNSUPPORTED_SCENARIO_PARAMETER",
            $"Parameter '{key}' is not approved for MVP scenarios.");

    public static ScenarioException InvalidParameter(string key, double value, double min, double max, string unit) =>
        new("INVALID_SCENARIO_PARAMETER",
            $"Parameter '{key}' value {value} is outside the approved range [{min}, {max}] {unit}.");

    public static ScenarioException TooManyParameters(int count, int max) =>
        new("TOO_MANY_SCENARIO_PARAMETERS",
            $"Scenario has {count} parameters but the MVP allows at most {max}.");
}
