using Urbanova.Domain.Interfaces;

namespace Urbanova.Application.EngineeringFiles;

/// <summary>
/// Format detection: first registered <see cref="IEngineeringFileProcessor"/>
/// whose CanProcess matches wins. No match = UNSUPPORTED_FORMAT rejection that
/// names every supported format (the "generic stub" behavior).
/// </summary>
public interface IProcessorRegistry
{
    IEngineeringFileProcessor? Find(string fileName, string contentType);

    IEngineeringFileProcessor? FindByFormat(string formatName);

    IReadOnlyList<string> SupportedFormats { get; }
}
