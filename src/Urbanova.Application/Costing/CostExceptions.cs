namespace Urbanova.Application.Costing;

/// <summary>Cost failures. Api maps: NOT_FOUND → 404, FORBIDDEN → 403, INVALID_COST → 400.</summary>
public sealed class CostException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    public static CostException NotFound(Guid id) =>
        new("NOT_FOUND", $"Cost subject '{id}' was not found.");

    public static CostException Forbidden() =>
        new("FORBIDDEN", "You do not have access to this cost estimate.");

    public static CostException Invalid(string detail) =>
        new("INVALID_COST", detail);
}
