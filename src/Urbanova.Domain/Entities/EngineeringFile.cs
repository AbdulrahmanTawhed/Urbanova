using Urbanova.Domain.Common;

namespace Urbanova.Domain.Entities;

/// <summary>Uploaded engineering file. Content lives on disk (IFileStorage); DB holds metadata + validation state.</summary>
public sealed class EngineeringFile : EntityBase
{
    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public Guid UploadedBy { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public string StoragePath { get; set; } = string.Empty;

    public string? FormatDetected { get; set; }

    /// <summary>SHA-256 hex. Unique per project (dedupe).</summary>
    public string HashSha256 { get; set; } = string.Empty;

    public FileValidationStatus ValidationStatus { get; set; } = FileValidationStatus.Pending;

    public string? ValidationErrorsJson { get; set; }

    public string? MetadataJson { get; set; }
}
