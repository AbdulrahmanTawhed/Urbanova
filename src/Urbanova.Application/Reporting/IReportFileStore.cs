namespace Urbanova.Application.Reporting;

/// <summary>Rendered-report file store port (HTML today; PDF tomorrow without service changes).</summary>
public interface IReportFileStore
{
    Task<string> SaveAsync(Guid projectId, Guid reportId, string extension, string content, CancellationToken ct = default);

    Task<byte[]> ReadAsync(string storagePath, CancellationToken ct = default);
}
