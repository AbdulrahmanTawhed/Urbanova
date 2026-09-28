namespace Urbanova.Infrastructure.FileProcessing;

/// <summary>Binds to the "FileStorage" configuration section.</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string RootPath { get; set; } = "AppData/uploads";

    public long MaxBytes { get; set; } = 52_428_800; // 50 MB

    public List<string> AllowedContentTypes { get; set; } = [];
}
