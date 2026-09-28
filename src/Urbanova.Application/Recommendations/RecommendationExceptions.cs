namespace Urbanova.Application.Recommendations;

/// <summary>
/// Recommendation failures. Api maps: NOT_FOUND → 404, FORBIDDEN → 403, NO_ANALYSIS → 400.
/// </summary>
public sealed class RecommendationException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    public static RecommendationException NotFound(Guid id) =>
        new("NOT_FOUND", $"Recommendation subject '{id}' was not found.");

    public static RecommendationException Forbidden() =>
        new("FORBIDDEN", "You do not have access to these recommendations.");

    public static RecommendationException NoAnalysis() =>
        new("NO_ANALYSIS", "Project has no succeeded analysis to base recommendations on.");
}
