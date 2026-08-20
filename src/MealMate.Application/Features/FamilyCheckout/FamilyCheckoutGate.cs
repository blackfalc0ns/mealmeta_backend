using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;

namespace MealMate.Application.Features.FamilyCheckout;

public static class FamilyCheckoutGate
{
    public static (bool CanCheckout, string? BlockReason) Evaluate(
        FamilyGroup group,
        IReadOnlyList<FamilyMember> members,
        FamilySizePackageSettings package,
        FamilyQuoteResult quote)
    {
        if (group.Status != FamilyGroupStatus.Draft)
            return (false, "family.not_draft");

        if (members.Count < package.MinMembers)
            return (false, "family.min_members");

        if (members.Count > package.MaxMembers)
            return (false, "family.max_members");

        if (members.Any(m => m.SetupStatus != FamilyMemberSetupStatus.Ready))
            return (false, "family.member_incomplete");

        if (quote.FinalPrice <= 0m)
            return (false, "family.invalid_amount");

        return (true, null);
    }
}
