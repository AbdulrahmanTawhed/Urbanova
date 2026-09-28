using FluentValidation;

namespace Urbanova.Application.Analysis;

/// <summary>MVP scenario parameters (assumptions.md §6). Unknown keys rejected — the set
/// is intentionally closed until engineering finalizes modifiable parameters.</summary>
public sealed class AnalyzeRequestValidator : AbstractValidator<AnalyzeRequest>
{
    public AnalyzeRequestValidator()
    {
        RuleFor(x => x.FileId).NotEmpty();
        RuleFor(x => x.Parameters).NotNull()
            .WithMessage("Parameters object is required (may be empty).");
        When(x => x.Parameters is not null, () =>
        {
            RuleForEach(x => x.Parameters!.Keys)
                .Must(k => k is "vegetationCoverPct" or "albedo" or "shadingPct")
                .WithMessage("Unknown parameter '{PropertyValue}'. MVP parameters: vegetationCoverPct, albedo, shadingPct.");
            RuleFor(x => x.Parameters!["vegetationCoverPct"]).InclusiveBetween(0, 100)
                .When(p => p.Parameters!.ContainsKey("vegetationCoverPct"));
            RuleFor(x => x.Parameters!["albedo"]).InclusiveBetween(0, 1)
                .When(p => p.Parameters!.ContainsKey("albedo"));
            RuleFor(x => x.Parameters!["shadingPct"]).InclusiveBetween(0, 100)
                .When(p => p.Parameters!.ContainsKey("shadingPct"));
        });
    }
}
