using MealMate.Domain.Enums;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record CapacityHoldItemDto(
    Guid Id,
    Guid SubscriptionId,
    Guid RestaurantId,
    DateOnly ServiceDate,
    int Quantity,
    CapacityHoldStatus Status,
    DateTime ExpiresAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? ReleasedAtUtc);

public sealed record SnapshotMigrationIssueDto(
    Guid SubscriptionId,
    string ReasonCode,
    string DetailsJson,
    DateTime RecordedAtUtc,
    DateTime? ResolvedAtUtc);

public sealed record ExpireHoldsOutcome(
    int HoldsExpired,
    int SubscriptionsExpired);

public sealed record RetryFulfillmentOutcome(
    Guid SubscriptionId,
    string Status,
    bool IsAlreadyComplete,
    string? ErrorCode);

public sealed record ReassignRestaurantRequest(
    Guid RestaurantId,
    string? Reason = null);

public sealed record ReassignmentOutcome(
    string? ErrorCode,
    IReadOnlyList<string>? ReasonCodes = null,
    Guid? DayId = null,
    Guid? RestaurantId = null,
    Guid? MenuPlanVersionId = null,
    SubscriptionDayStatus? Status = null);

public sealed record FrozenRestaurantOption(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    Guid MenuPlanVersionId,
    bool IsEligible,
    IReadOnlyList<string> ReasonCodes);
