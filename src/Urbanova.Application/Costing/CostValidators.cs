using FluentValidation;

namespace Urbanova.Application.Costing;

/// <summary>Cost input validation. Price presence (direct or catalog) is enforced in the service.</summary>
public sealed class CreateCostEstimateRequestValidator : AbstractValidator<CreateCostEstimateRequest>
{
    // Upper bounds keep Quantity × UnitPrice inside decimal range (else OverflowException → 500).
    private const decimal MaxAmount = 1_000_000_000_000m;

    public CreateCostEstimateRequestValidator()
    {
        RuleFor(x => x.Quantity!.Value).GreaterThanOrEqualTo(0).LessThanOrEqualTo(MaxAmount)
            .When(x => x.Quantity.HasValue);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(50);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).LessThanOrEqualTo(MaxAmount)
            .When(x => x.UnitPrice.HasValue);
        RuleFor(x => x.ItemCode).MaximumLength(100).When(x => x.ItemCode is not null);
        RuleFor(x => x.Currency).Length(3).When(x => x.Currency is not null);
    }
}
