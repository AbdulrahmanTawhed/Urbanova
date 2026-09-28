namespace Urbanova.Application.Comparison;

/// <summary>
/// Comparison failures. Api maps: NOT_FOUND → 404, FORBIDDEN → 403, INVALID_COMPARISON → 400.
/// </summary>
public sealed class ComparisonException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    public static ComparisonException NotFound(Guid id) =>
        new("NOT_FOUND", $"Comparison subject '{id}' was not found.");

    public static ComparisonException Forbidden() =>
        new("FORBIDDEN", "You do not have access to this comparison.");

    public static ComparisonException Invalid(string detail) =>
        new("INVALID_COMPARISON", detail);
}
