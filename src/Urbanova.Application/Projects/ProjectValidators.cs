using FluentValidation;

namespace Urbanova.Application.Projects;

/// <summary>API-level project validation (Application layer of the multi-level chain).</summary>
public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.Site).SetValidator(new SiteInputValidator()!).When(x => x.Site is not null);
    }
}

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.Status)
            .Must(s => Enum.TryParse<Domain.ProjectStatus>(s, ignoreCase: true, out _))
            .When(x => x.Status is not null)
            .WithMessage("Status must be one of: Draft, Active, Archived (any casing).");
        RuleFor(x => x.RowVersion).NotEmpty().WithMessage("RowVersion is required for concurrency control.");
        RuleFor(x => x.Site).SetValidator(new SiteInputValidator()!).When(x => x.Site is not null);
    }
}

public sealed class SiteInputValidator : AbstractValidator<SiteInput>
{
    public SiteInputValidator()
    {
        RuleFor(x => x.Address).MaximumLength(500).When(x => x.Address is not null);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue);
        RuleFor(x => x.Crs).MaximumLength(50).When(x => x.Crs is not null);
        RuleFor(x => x.AreaM2).GreaterThan(0).When(x => x.AreaM2.HasValue);
    }
}
