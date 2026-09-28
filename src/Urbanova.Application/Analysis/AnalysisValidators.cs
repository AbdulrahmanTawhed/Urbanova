using FluentValidation;

namespace Urbanova.Application.Analysis;

/// <summary>Analyze request shape validation. Parameter content (allowed keys, bands,
/// max count with coded errors) is enforced by ScenarioParameterCatalog in the
/// analysis service, shared with scenario validation (PRD v0.2 §8).</summary>
public sealed class AnalyzeRequestValidator : AbstractValidator<AnalyzeRequest>
{
    public AnalyzeRequestValidator()
    {
        RuleFor(x => x.FileId).NotEmpty();
        RuleFor(x => x.Parameters).NotNull()
            .WithMessage("Parameters object is required (may be empty).");
    }
}
