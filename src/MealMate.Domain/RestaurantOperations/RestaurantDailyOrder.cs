using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantDailyOrder : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid SubscriptionDayId { get; set; }

    public Guid? OrderId { get; set; }

    public DateOnly ServiceDate { get; set; }

    public Guid? ResolvedMenuPlanVersionId { get; set; }

    public Guid? ResolvedMenuDayId { get; set; }

    public Guid? ResolvedProgramId { get; set; }

    public ScheduleMode? ScheduleMode { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Created;

    public int BoxCount { get; set; }

    public DateTime? ConfirmedAtUtc { get; set; }

    public DateTime? ReadyForPickupAtUtc { get; set; }

    public DateTime? RejectedAtUtc { get; set; }

    public string? RejectionReason { get; set; }

    public ICollection<RestaurantConfirmation> Confirmations { get; set; } = new List<RestaurantConfirmation>();

    public ICollection<OrderHandover> Handovers { get; set; } = new List<OrderHandover>();

    public bool CanTransitionTo(OrderStatus target)
    {
        if (Status == target) return true;

        return Status switch
        {
            OrderStatus.Created => target is OrderStatus.AwaitingRestaurantConfirmation
                                          or OrderStatus.Cancelled,

            OrderStatus.AwaitingRestaurantConfirmation => target is OrderStatus.RestaurantConfirmed
                                                                 or OrderStatus.RestaurantRejected
                                                                 or OrderStatus.ReplacementPending
                                                                 or OrderStatus.Hold
                                                                 or OrderStatus.Cancelled,

            OrderStatus.RestaurantConfirmed => target is OrderStatus.Preparing
                                                      or OrderStatus.Hold
                                                      or OrderStatus.Cancelled,

            OrderStatus.RestaurantRejected => target is OrderStatus.ReplacementPending
                                                    or OrderStatus.Cancelled,

            OrderStatus.ReplacementPending => target is OrderStatus.AwaitingRestaurantConfirmation
                                                    or OrderStatus.Cancelled,

            OrderStatus.Preparing => target is OrderStatus.ReadyForPickup
                                           or OrderStatus.Hold
                                           or OrderStatus.Cancelled,

            OrderStatus.ReadyForPickup => target is OrderStatus.AssignedToDriver
                                                or OrderStatus.Hold
                                                or OrderStatus.Cancelled,

            OrderStatus.AssignedToDriver => target is OrderStatus.OnTheWay
                                                  or OrderStatus.PickedUp
                                                  or OrderStatus.ReadyForPickup
                                                  or OrderStatus.Hold
                                                  or OrderStatus.Cancelled,

            OrderStatus.PickedUp => target is OrderStatus.OnTheWay
                                           or OrderStatus.Hold,

            OrderStatus.OnTheWay => target is OrderStatus.Delivered
                                          or OrderStatus.DeliveryFailed
                                          or OrderStatus.Hold,

            OrderStatus.DeliveryFailed => target is OrderStatus.SecondAttemptScheduled
                                                or OrderStatus.ReturnedToKitchen
                                                or OrderStatus.Hold
                                                or OrderStatus.Cancelled,

            OrderStatus.SecondAttemptScheduled => target is OrderStatus.AssignedToDriver
                                                        or OrderStatus.OnTheWay
                                                        or OrderStatus.Cancelled,

            OrderStatus.Hold => target is OrderStatus.AwaitingRestaurantConfirmation
                                       or OrderStatus.RestaurantConfirmed
                                       or OrderStatus.Preparing
                                       or OrderStatus.ReadyForPickup
                                       or OrderStatus.AssignedToDriver
                                       or OrderStatus.OnTheWay
                                       or OrderStatus.DeliveryFailed
                                       or OrderStatus.Cancelled,

            OrderStatus.ReturnedToKitchen => target is OrderStatus.Cancelled,
            OrderStatus.Delivered => false,
            OrderStatus.Cancelled => false,
            _ => false
        };
    }

    public Result TransitionTo(OrderStatus target)
    {
        if (!CanTransitionTo(target))
        {
            return Result.Failure($"INVALID_ORDER_TRANSITION: Cannot transition restaurant daily order from {Status} to {target}.");
        }

        Status = target;
        return Result.Success();
    }
}
