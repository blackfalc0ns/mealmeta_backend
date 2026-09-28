using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CalendarMeals;

public sealed class SubscriptionDay : AuditableEntity
{
    public Guid SubscriptionId { get; set; }

    public Guid? CalendarId { get; set; }

    public SubscriptionCalendar? Calendar { get; set; }

    public DateOnly ServiceDate { get; set; }

    public SubscriptionDayStatus Status { get; set; } = SubscriptionDayStatus.Open;

    public DateTime? SelectionDeadlineUtc { get; set; }

    public Guid? RestaurantId { get; set; }

    /// <summary>Approved menu version fixed when payment confirms the subscription.</summary>
    public Guid? ResolvedMenuPlanVersionId { get; set; }

    public DateTime? LockAtUtc { get; set; }

    public DateTime? ConfirmedAtUtc { get; set; }

    public bool IsAutoSelected { get; set; }

    public Guid? DeliveryAddressId { get; set; }

    public string? DeliveryAddressLine { get; set; }

    public Guid? DeliveryRegionId { get; set; }

    public Guid? DeliveryBlockRegionId { get; set; }

    public ICollection<DailyRestaurantSelection> RestaurantSelections { get; set; } = new List<DailyRestaurantSelection>();

    public ICollection<DailyMealSelection> MealSelections { get; set; } = new List<DailyMealSelection>();

    public ICollection<MealChangeRequest> MealChangeRequests { get; set; } = new List<MealChangeRequest>();

    public bool CanTransitionTo(SubscriptionDayStatus target)
    {
        if (Status == target) return true;

        return Status switch
        {
            SubscriptionDayStatus.Open => target is SubscriptionDayStatus.CustomerSelected
                                                or SubscriptionDayStatus.AutoSelected
                                                or SubscriptionDayStatus.Locked72h
                                                or SubscriptionDayStatus.ReplacementRequired
                                                or SubscriptionDayStatus.Frozen
                                                or SubscriptionDayStatus.Cancelled,

            SubscriptionDayStatus.AutoSelected => target is SubscriptionDayStatus.CustomerSelected
                                                         or SubscriptionDayStatus.Open
                                                         or SubscriptionDayStatus.Locked72h
                                                         or SubscriptionDayStatus.ReplacementRequired
                                                         or SubscriptionDayStatus.Frozen
                                                         or SubscriptionDayStatus.Cancelled,

            SubscriptionDayStatus.CustomerSelected => target is SubscriptionDayStatus.AutoSelected
                                                             or SubscriptionDayStatus.Open
                                                             or SubscriptionDayStatus.Locked72h
                                                             or SubscriptionDayStatus.ReplacementRequired
                                                             or SubscriptionDayStatus.Frozen
                                                             or SubscriptionDayStatus.Cancelled,

            SubscriptionDayStatus.Locked72h => target is SubscriptionDayStatus.SentToRestaurant
                                                      or SubscriptionDayStatus.ReplacementRequired
                                                      or SubscriptionDayStatus.Frozen
                                                      or SubscriptionDayStatus.Cancelled,

            SubscriptionDayStatus.SentToRestaurant => target is SubscriptionDayStatus.ConfirmedByRestaurant
                                                             or SubscriptionDayStatus.ReplacementRequired
                                                             or SubscriptionDayStatus.Frozen
                                                             or SubscriptionDayStatus.Cancelled,

            SubscriptionDayStatus.ConfirmedByRestaurant => target is SubscriptionDayStatus.Preparing
                                                                  or SubscriptionDayStatus.ReplacementRequired
                                                                  or SubscriptionDayStatus.Frozen
                                                                  or SubscriptionDayStatus.Cancelled,

            SubscriptionDayStatus.ReplacementRequired => target is SubscriptionDayStatus.ReplacementWindowOpen
                                                                 or SubscriptionDayStatus.SentToRestaurant
                                                                 or SubscriptionDayStatus.Cancelled,

            SubscriptionDayStatus.ReplacementWindowOpen => target is SubscriptionDayStatus.CustomerSelected
                                                                  or SubscriptionDayStatus.AutoSelected
                                                                  or SubscriptionDayStatus.SentToRestaurant
                                                                  or SubscriptionDayStatus.Cancelled,

            SubscriptionDayStatus.Preparing => target is SubscriptionDayStatus.OutForDelivery
                                                      or SubscriptionDayStatus.Failed
                                                      or SubscriptionDayStatus.Cancelled,

            SubscriptionDayStatus.OutForDelivery => target is SubscriptionDayStatus.Delivered
                                                           or SubscriptionDayStatus.Failed,

            SubscriptionDayStatus.Failed => target is SubscriptionDayStatus.Open
                                                   or SubscriptionDayStatus.ReplacementRequired
                                                   or SubscriptionDayStatus.Cancelled,

            SubscriptionDayStatus.Frozen => target is SubscriptionDayStatus.Open
                                                   or SubscriptionDayStatus.Cancelled,

            SubscriptionDayStatus.Delivered => false,
            SubscriptionDayStatus.Cancelled => false,
            _ => false
        };
    }

    public Result TransitionTo(SubscriptionDayStatus target)
    {
        if (!CanTransitionTo(target))
        {
            return Result.Failure($"INVALID_STATE_TRANSITION: Cannot transition subscription day from {Status} to {target}.");
        }

        Status = target;
        return Result.Success();
    }

    public void MarkReplacementRequired(string reason)
    {
        Status = SubscriptionDayStatus.ReplacementRequired;
        LockAtUtc = null;
        Raise(new Events.CustomerReplacementRequiredDomainEvent(Id, SubscriptionId, ServiceDate, reason));
    }
}
