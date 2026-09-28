namespace Urbanova.Infrastructure.Reporting;

/// <summary>Binds to "Reporting".</summary>
public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    public string DefaultFormat { get; set; } = "Json";

    public string OutputPath { get; set; } = "AppData/reports";
}
