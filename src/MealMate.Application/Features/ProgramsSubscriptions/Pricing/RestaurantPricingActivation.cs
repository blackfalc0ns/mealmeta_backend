using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Pricing;

/// <summary>
/// MM-PRC-AMD-01: commercially active supply/reference prices vs pending change requests.
/// </summary>
public static class RestaurantPricingActivation
{
    public static bool IsCommerciallyActive(PriceLockStatus status) =>
        status is PriceLockStatus.Locked
            or PriceLockStatus.Approved
            or PriceLockStatus.ApprovedWithEffectiveDate;

    public static bool IsPendingChange(PriceLockStatus status) =>
        status is PriceLockStatus.ChangeRequested
            or PriceLockStatus.PendingApproval;

    public static bool IsEditableDraft(PriceLockStatus status) =>
        status is PriceLockStatus.Draft
            or PriceLockStatus.ChangeRequested
            or PriceLockStatus.PendingApproval;
}
