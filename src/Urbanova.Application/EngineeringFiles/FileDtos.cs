using System.Text.Json;

namespace Urbanova.Application.EngineeringFiles;

/// <summary>File DTOs. EF entities never leave the service layer.</summary>
public sealed record FileResponse(
    Guid Id,
    Guid ProjectId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? FormatDetected,
    string HashSha256,
    string ValidationStatus,
    IReadOnlyList<string> ValidationErrors,
    JsonElement? Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
