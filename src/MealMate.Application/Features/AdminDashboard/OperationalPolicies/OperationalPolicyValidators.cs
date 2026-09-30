using FluentValidation;
namespace MealMate.Application.Features.AdminDashboard.OperationalPolicies;
public sealed class UpdateOperationalPoliciesCommandValidator : AbstractValidator<UpdateOperationalPoliciesCommand>
{
    public UpdateOperationalPoliciesCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.Settings).Custom((settings, context) => { var error = OperationalPolicyRules.Validate(settings); if (error is not null) context.AddFailure(error); });
    }
}
