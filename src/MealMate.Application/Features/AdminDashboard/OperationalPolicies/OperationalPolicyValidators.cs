using FluentValidation;

namespace MealMate.Application.Features.AdminDashboard.OperationalPolicies;

public sealed class UpdateOperationalPoliciesCommandValidator : AbstractValidator<UpdateOperationalPoliciesCommand>
{
    public UpdateOperationalPoliciesCommandValidator()
    {
        RuleFor(x => x.AdminUserId)
            .NotEmpty();

        RuleFor(x => x.Settings)
            .NotNull();

        When(x => x.Settings != null, () =>
        {
            RuleFor(x => x.Settings.Cutoff.CutoffLeadHours)
                .InclusiveBetween(1, 168)
                .WithMessage("Cutoff lead hours must be between 1 and 168 (1 hour to 7 days).");

            RuleFor(x => x.Settings.Cutoff.FixedDailyHourKuwait)
                .InclusiveBetween(0, 23)
                .WithMessage("Fixed daily hour must be between 0 and 23.");

            RuleFor(x => x.Settings.Cancellation.FeeValue)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Cancellation fee cannot be negative.");

            When(x => x.Settings.Cancellation.FeeType == CancellationFeeType.Percentage, () =>
            {
                RuleFor(x => x.Settings.Cancellation.FeeValue)
                    .LessThanOrEqualTo(100)
                    .WithMessage("Percentage fee cannot exceed 100%.");
            });

            RuleFor(x => x.Settings.Freeze.LeadDaysNotice)
                .GreaterThanOrEqualTo(1)
                .WithMessage("Freeze lead days notice must be at least 1 day.");

            RuleFor(x => x.Settings.Freeze.MaxDays)
                .GreaterThanOrEqualTo(1)
                .WithMessage("Freeze max days must be at least 1 day.");
        });
    }
}
