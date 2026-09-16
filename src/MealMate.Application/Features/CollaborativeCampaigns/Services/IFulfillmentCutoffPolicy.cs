using MealMate.Domain.CalendarMeals;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public record ModificationEligibilityResult(bool CanModify, string? Reason = null)
{
    public static ModificationEligibilityResult Allowed() => new(true);
    public static ModificationEligibilityResult Disallowed(string reason) => new(false, reason);
}

public interface IFulfillmentCutoffPolicy
{
    bool IsLocked(DateOnly serviceDate, DateTime currentUtc, TimeSpan cutoffWindow);
    ModificationEligibilityResult CanModifySelection(SubscriptionDay day, DateTime currentUtc);
    bool EnforceCutoffLock(SubscriptionDay day, DateTime currentUtc, TimeSpan cutoffWindow);
}
