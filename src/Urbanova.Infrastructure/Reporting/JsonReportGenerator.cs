using System.Text.Json;
using Urbanova.Domain;
using Urbanova.Domain.Reporting;

namespace Urbanova.Infrastructure.Reporting;

/// <summary>Canonical JSON renderer — the source of truth every other format derives from.</summary>
public sealed class JsonReportGenerator : IReportGenerator
{
    public ReportFormat Format => ReportFormat.Json;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public Task<GeneratedReport> GenerateAsync(ReportModel model, CancellationToken ct = default) =>
        Task.FromResult(new GeneratedReport(
            ReportFormat.Json, JsonSerializer.Serialize(model, Options), "application/json"));
}
