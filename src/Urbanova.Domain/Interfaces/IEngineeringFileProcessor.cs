using Urbanova.Domain.ValueObjects;

namespace Urbanova.Domain.Interfaces;

/// <summary>
/// File contract: detection + validation + metadata (Phase 5) + geometry
/// extraction/normalization (Phase 6). One implementation per format is registered
/// in DI and picked by the registry, so new formats plug in without touching
/// existing code (PRD §6). Analysis consumes only NormalizedGeometry (PRD §7).
/// </summary>
public interface IEngineeringFileProcessor
{
    /// <summary>Canonical format name stored on EngineeringFile.FormatDetected (e.g. "GeoJSON").</summary>
    string FormatName { get; }

    IReadOnlyList<string> SupportedExtensions { get; }

    IReadOnlyList<string> SupportedContentTypes { get; }

    bool CanProcess(string fileName, string contentType);

    /// <summary>Validates content. Never throws for bad content — returns errors instead.</summary>
    Task<FileValidationResult> ValidateAsync(Stream content, string fileName, CancellationToken ct = default);

    /// <summary>Extracts format metadata (feature counts, CRS hints, bounds). Call after a valid result.</summary>
    Task<EngineeringFileMetadata> GetMetadataAsync(Stream content, string fileName, CancellationToken ct = default);

    /// <summary>
    /// Extracts raw geometry in source CRS (rings as-is). Never throws for bad geometry —
    /// returns <see cref="GeometryExtractionResult"/> failure with explanations.
    /// </summary>
    Task<GeometryExtractionResult> ExtractGeometryAsync(Stream content, string fileName, CancellationToken ct = default);

    /// <summary>
    /// Normalizes raw geometry: closes rings, defaults CRS, recomputes areas/bboxes/totals.
    /// Pure function — safe to unit test without files.
    /// </summary>
    Task<NormalizedGeometry> NormalizeGeometryAsync(NormalizedGeometry raw, CancellationToken ct = default);
}
