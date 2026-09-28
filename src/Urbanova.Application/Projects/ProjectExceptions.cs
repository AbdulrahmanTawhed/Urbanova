namespace Urbanova.Application.Projects;

/// <summary>
/// Project failures. Api maps codes: NOT_FOUND → 404, FORBIDDEN → 403,
/// CONCURRENCY_CONFLICT → 409. Messages are safe to return.
/// </summary>
public sealed class ProjectException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    public static ProjectException NotFound(Guid id) =>
        new("NOT_FOUND", $"Project '{id}' was not found.");

    public static ProjectException Forbidden() =>
        new("FORBIDDEN", "You do not have access to this project.");

    public static ProjectException ConcurrencyConflict() =>
        new("CONCURRENCY_CONFLICT", "Project was modified by another request. Reload and retry.");
}
