namespace MealMate.Application.Common.Abstractions;

public sealed record DriverSupportHandoverEvent(
    Guid EventId, DateTime OccurredAtUtc, Guid HandoverId, Guid TripId, Guid? ReplacementTripId,
    Guid RestaurantId, Guid OriginalDriverProfileId, Guid ReplacementDriverProfileId, string Status, string Event);

public interface IDriverSupportHandoverNotifier
{
    Task PublishAsync(DriverSupportHandoverEvent message, CancellationToken cancellationToken);
}
