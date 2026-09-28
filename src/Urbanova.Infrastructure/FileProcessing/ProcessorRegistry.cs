using Urbanova.Application.EngineeringFiles;
using Urbanova.Domain.Interfaces;

namespace Urbanova.Infrastructure.FileProcessing;

/// <summary>First-match registry over DI-registered processors. Add formats by registering
/// another IEngineeringFileProcessor — no existing code changes (PRD §6).</summary>
public sealed class ProcessorRegistry(IEnumerable<IEngineeringFileProcessor> processors) : IProcessorRegistry
{
    private readonly IReadOnlyList<IEngineeringFileProcessor> _processors = [.. processors];

    public IEngineeringFileProcessor? Find(string fileName, string contentType) =>
        _processors.FirstOrDefault(p => p.CanProcess(fileName, contentType));

    public IEngineeringFileProcessor? FindByFormat(string formatName) =>
        _processors.FirstOrDefault(p => p.FormatName.Equals(formatName, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> SupportedFormats =>
        [.. _processors.Select(p => $"{p.FormatName} ({string.Join(", ", p.SupportedExtensions)})")];
}
