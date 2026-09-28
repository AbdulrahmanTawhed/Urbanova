using FluentValidation;

namespace Urbanova.Application.Scenarios;

/// <summary>Scenario request validation. Parameter bands mirror AnalyzeRequestValidator (MVP set).</summary>
public sealed class ScenarioParametersValidator : AbstractValidator<Dictionary<string, double>>
{
    public ScenarioParametersValidator()
    {
        RuleForEach(x => x.Keys)
            .Must(k => k is "vegetationCoverPct" or "albedo" or "shadingPct")
            .WithMessage("Unknown parameter '{PropertyValue}'. MVP parameters: vegetationCoverPct, albedo, shadingPct.");
        RuleFor(x => x["vegetationCoverPct"]).InclusiveBetween(0, 100)
            .When(x => x.ContainsKey("vegetationCoverPct"));
        RuleFor(x => x["albedo"]).InclusiveBetween(0, 1)
            .When(x => x.ContainsKey("albedo"));
        RuleFor(x => x["shadingPct"]).InclusiveBetween(0, 100)
            .When(x => x.ContainsKey("shadingPct"));
    }
}

public sealed class CreateScenarioRequestValidator : AbstractValidator<CreateScenarioRequest>
{
    public CreateScenarioRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Parameters).SetValidator(new ScenarioParametersValidator()!)
            .When(x => x.Parameters is not null);
    }
}

public sealed class UpdateScenarioRequestValidator : AbstractValidator<UpdateScenarioRequest>
{
    public UpdateScenarioRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.Parameters).SetValidator(new ScenarioParametersValidator()!)
            .When(x => x.Parameters is not null);
        RuleFor(x => x.RowVersion).NotEmpty().WithMessage("RowVersion is required for concurrency control.");
    }
}

public sealed class AnalyzeScenarioRequestValidator : AbstractValidator<AnalyzeScenarioRequest>
{
    public AnalyzeScenarioRequestValidator() { }
}
