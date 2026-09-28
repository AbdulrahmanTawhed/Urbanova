namespace Urbanova.Application.Analysis;

/// <summary>
/// Analysis failures. Api maps: NOT_FOUND → 404, FORBIDDEN → 403,
/// INVALID_FILE / GEOMETRY_EXTRACTION_FAILED → 422, ANALYSIS_FAILED → 500.
/// Incomplete results are never returned as valid (PRD §19).
/// </summary>
public sealed class AnalysisException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    public static AnalysisException NotFound(string what, Guid id) =>
        new("NOT_FOUND", $"{what} '{id}' was not found.");

    public static AnalysisException Forbidden() =>
        new("FORBIDDEN", "You do not have access to this analysis.");

    public static AnalysisException InvalidFile(string detail) =>
        new("INVALID_FILE", $"Analysis input is not usable: {detail}");

    public static AnalysisException GeometryFailed(IReadOnlyList<string> errors) =>
        new("GEOMETRY_EXTRACTION_FAILED",
            $"Geometry extraction failed; analysis stopped: {string.Join("; ", errors)}");

    public static AnalysisException Failed(string detail) =>
        new("ANALYSIS_FAILED", $"Analysis failed and no result was stored: {detail}");
}
