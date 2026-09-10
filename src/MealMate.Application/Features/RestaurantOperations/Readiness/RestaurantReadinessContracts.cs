namespace MealMate.Application.Features.RestaurantOperations.Readiness;

public sealed record UnresolvedSnapshotDayDto(
    Guid SubscriptionId,
    Guid SubscriptionDayId,
    DateOnly ServiceDate,
    string ReasonCode);

public sealed record MealSnapshotBackfillResultDto(
    int SubscriptionsScanned,
    int SnapshotsCreated,
    IReadOnlyList<UnresolvedSnapshotDayDto> Unresolved);
