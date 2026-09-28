namespace Urbanova.Application.Reporting;

/// <summary>Reporting use cases (Phase 12). Owner-scoped; throws <see cref="ReportException"/>.</summary>
public interface IReportService
{
    Task<ReportResponse> GenerateAsync(
        Guid ownerId, Guid projectId, CreateReportRequest request, CancellationToken ct = default);

    Task<ReportResponse> GetAsync(Guid ownerId, Guid reportId, CancellationToken ct = default);

    Task<(string ContentType, byte[] Content, string FileName)> GetFileAsync(
        Guid ownerId, Guid reportId, CancellationToken ct = default);
}
