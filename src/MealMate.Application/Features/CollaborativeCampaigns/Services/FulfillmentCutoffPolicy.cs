using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed class FulfillmentCutoffPolicy : IFulfillmentCutoffPolicy
{
    public bool IsLocked(DateOnly serviceDate, DateTime currentUtc, TimeSpan cutoffWindow)
    {
        var serviceDateTimeUtc = serviceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return (serviceDateTimeUtc - currentUtc) <= cutoffWindow;
    }

    public ModificationEligibilityResult CanModifySelection(SubscriptionDay day, DateTime currentUtc)
    {
        if (day.Status == SubscriptionDayStatus.Locked72h ||
            day.Status == SubscriptionDayStatus.SentToRestaurant ||
            day.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
            day.Status == SubscriptionDayStatus.Preparing ||
            day.Status == SubscriptionDayStatus.OutForDelivery ||
            day.Status == SubscriptionDayStatus.Delivered ||
            day.Status == SubscriptionDayStatus.Cancelled)
        {
            return ModificationEligibilityResult.Disallowed(
                $"Subscription day on '{day.ServiceDate}' is locked or completed (Status: {day.Status}).");
        }

        if (day.SelectionDeadlineUtc.HasValue && currentUtc >= day.SelectionDeadlineUtc.Value)
        {
            return ModificationEligibilityResult.Disallowed(
                $"Selection deadline of '{day.SelectionDeadlineUtc.Value:O}' has passed.");
        }

        return ModificationEligibilityResult.Allowed();
    }

    public bool EnforceCutoffLock(SubscriptionDay day, DateTime currentUtc, TimeSpan cutoffWindow)
    {
        if (day.Status == SubscriptionDayStatus.Cancelled ||
            day.Status == SubscriptionDayStatus.Failed ||
            day.Status == SubscriptionDayStatus.Frozen)
        {
            return false;
        }

        if (IsLocked(day.ServiceDate, currentUtc, cutoffWindow))
        {
            day.Status = SubscriptionDayStatus.Locked72h;
            day.LockAtUtc = currentUtc;
            return true;
        }

        return false;
    }
}
