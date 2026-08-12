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

    public DateTime? RejectedAtUtc { get; set; }

    public string? RejectionReason { get; set; }

    public ICollection<RestaurantConfirmation> Confirmations { get; set; } = new List<RestaurantConfirmation>();

    public ICollection<OrderHandover> Handovers { get; set; } = new List<OrderHandover>();
}
