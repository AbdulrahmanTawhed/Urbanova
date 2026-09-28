namespace Urbanova.Application.EngineeringFiles;

/// <summary>
/// File failures. Api maps: NOT_FOUND → 404, FORBIDDEN → 403, FILE_DUPLICATE → 409,
/// FILE_TOO_LARGE → 413, UNSUPPORTED_FORMAT → 415, INVALID_FILE → 422,
/// GEOMETRY_EXTRACTION_FAILED → 422, FILE_STORAGE_ERROR → 500. Messages are safe to return.
/// </summary>
public sealed class FileException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    public static FileException NotFound(Guid id) =>
        new("NOT_FOUND", $"Engineering file '{id}' was not found.");

    public static FileException Forbidden() =>
        new("FORBIDDEN", "You do not have access to this engineering file.");

    public static FileException UnsupportedFormat(string fileName, string supported) =>
        new("UNSUPPORTED_FORMAT",
            $"File '{fileName}' has an unsupported format. Supported formats: {supported}.");

    public static FileException InvalidFile(IReadOnlyList<string> errors) =>
        new("INVALID_FILE", $"File content is invalid: {string.Join("; ", errors)}");

    public static FileException Duplicate(Guid existingId) =>
        new("FILE_DUPLICATE", $"Identical content already uploaded (file '{existingId}').");

    public static FileException TooLarge(long maxBytes) =>
        new("FILE_TOO_LARGE", $"File exceeds the {maxBytes / 1_048_576} MB upload limit.");

    public static FileException StorageError(string detail) =>
        new("FILE_STORAGE_ERROR", $"File storage failure: {detail}");

    public static FileException ExtractionFailed(IReadOnlyList<string> errors) =>
        new("GEOMETRY_EXTRACTION_FAILED",
            $"Geometry extraction failed; analysis stopped: {string.Join("; ", errors)}");
}
