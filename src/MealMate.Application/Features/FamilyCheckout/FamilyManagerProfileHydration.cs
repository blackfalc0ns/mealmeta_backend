using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;

namespace MealMate.Application.Features.FamilyCheckout;

internal static class FamilyManagerProfileHydration
{
    public static int AgeYearsFromDateOfBirth(DateOnly? dateOfBirth, DateOnly todayUtc)
    {
        if (dateOfBirth is null)
            return 0;

        var dob = dateOfBirth.Value;
        var age = todayUtc.Year - dob.Year;
        if (todayUtc < dob.AddYears(age))
            age--;

        return age is < 0 or > 120 ? 0 : age;
    }

    public static void Apply(
        FamilyMember manager,
        string displayName,
        int ageYears,
        Guid? defaultAddressId)
    {
        if (manager.Role != FamilyMemberRole.Manager)
            return;

        if (string.IsNullOrWhiteSpace(manager.DisplayName) || manager.DisplayName == "عضو")
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                manager.DisplayName = displayName.Trim();
        }

        if (manager.AgeYears is < 1 or > 120 && ageYears is >= 1 and <= 120)
            manager.AgeYears = ageYears;

        manager.RelationshipCode = "self";

        if (manager.DeliveryAddressId is null && defaultAddressId is Guid addressId)
            manager.DeliveryAddressId = addressId;

        manager.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(manager);
    }
}
