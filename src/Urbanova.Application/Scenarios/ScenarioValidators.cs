using FluentValidation;

namespace Urbanova.Application.Scenarios;

/// <summary>
/// Scenario request shape validation. Parameter content (allowed keys, bands, max
/// count with coded errors) is enforced by <see cref="ScenarioParameterCatalog"/>
/// in the service layer — including on merged run+request sets, which DTO
/// validators cannot see.
/// </summary>
public sealed class CreateScenarioRequestValidator : AbstractValidator<CreateScenarioRequest>
{
    public CreateScenarioRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateScenarioRequestValidator : AbstractValidator<UpdateScenarioRequest>
{
    public UpdateScenarioRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.RowVersion).NotEmpty().WithMessage("RowVersion is required for concurrency control.");
    }
}

public sealed class AnalyzeScenarioRequestValidator : AbstractValidator<AnalyzeScenarioRequest>
{
    public AnalyzeScenarioRequestValidator() { }
}
