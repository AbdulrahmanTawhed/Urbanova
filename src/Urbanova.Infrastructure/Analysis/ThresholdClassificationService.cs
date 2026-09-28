using Microsoft.Extensions.Options;
using Urbanova.Domain;
using Urbanova.Domain.Analysis;

namespace Urbanova.Infrastructure.Analysis;

/// <summary>Threshold classifier (PRD §9). Bands come only from <see cref="ClassificationOptions"/>.</summary>
public sealed class ThresholdClassificationService(IOptions<ClassificationOptions> options)
    : IEnvironmentalClassificationService
{
    private readonly ClassificationOptions _options = options.Value;

    public ProblemClass Classify(double value)
    {
        if (value < _options.AcceptableBelow)
            return ProblemClass.Acceptable;
        if (value < _options.ModerateBelow)
            return ProblemClass.Moderate;
        return ProblemClass.ProblemArea;
    }

    public IReadOnlyDictionary<ProblemClass, int> Summarize(IEnumerable<double> values)
    {
        var counts = new Dictionary<ProblemClass, int>
        {
            [ProblemClass.Acceptable] = 0,
            [ProblemClass.Moderate] = 0,
            [ProblemClass.ProblemArea] = 0,
        };
        foreach (var v in values)
            counts[Classify(v)]++;
        return counts;
    }
}
