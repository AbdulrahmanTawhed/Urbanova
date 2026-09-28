namespace Urbanova.Application.Reporting;

/// <summary>Reporting failures. Api maps: NOT_FOUND → 404, FORBIDDEN → 403, INVALID_REPORT → 400.</summary>
public sealed class ReportException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    public static ReportException NotFound(Guid id) =>
        new("NOT_FOUND", $"Report subject '{id}' was not found.");

    public static ReportException Forbidden() =>
        new("FORBIDDEN", "You do not have access to this report.");

    public static ReportException Invalid(string detail) =>
        new("INVALID_REPORT", detail);
}
