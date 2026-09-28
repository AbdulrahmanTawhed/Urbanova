using Urbanova.Domain;

namespace Urbanova.Domain.Reporting;

/// <summary>Generator port (PRD §15). One implementation per output format.</summary>
public sealed record GeneratedReport(ReportFormat Format, string Content, string ContentType);

public interface IReportGenerator
{
    ReportFormat Format { get; }

    Task<GeneratedReport> GenerateAsync(ReportModel model, CancellationToken ct = default);
}
