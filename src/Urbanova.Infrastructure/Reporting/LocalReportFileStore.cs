using Microsoft.Extensions.Options;
using Urbanova.Application.Reporting;

namespace Urbanova.Infrastructure.Reporting;

/// <summary>Local-disk rendered-report store: {Root}/{projectId}/{reportId}.{ext}.</summary>
public sealed class LocalReportFileStore(IOptions<ReportingOptions> options) : IReportFileStore
{
    private readonly ReportingOptions _options = options.Value;

    private string Root => Path.IsPathRooted(_options.OutputPath)
        ? _options.OutputPath
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.OutputPath));

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "html", "json" };

    public async Task<string> SaveAsync(Guid projectId, Guid reportId, string extension, string content, CancellationToken ct = default)
    {
        var dir = Path.Combine(Root, projectId.ToString());
        Directory.CreateDirectory(dir);
        var ext = extension.TrimStart('.');
        if (!AllowedExtensions.Contains(ext))
            throw new ArgumentException($"Report extension '{extension}' is not allowed.", nameof(extension));
        var full = Path.Combine(dir, $"{reportId}.{ext}");
        if (!full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Report path escaped project directory.");
        await File.WriteAllTextAsync(full, content, ct);
        return $"{projectId}/{reportId}.{ext}";
    }

    public async Task<byte[]> ReadAsync(string storagePath, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(Path.Combine(Root, storagePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new InvalidOperationException("Rendered report file is missing.");
        return await File.ReadAllBytesAsync(full, ct);
    }
}
