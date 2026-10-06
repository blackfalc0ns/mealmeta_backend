namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Restaurant-facing driver delivery lifecycle alerts. These events belong to the
/// restaurant (their batch left with the driver / reached the customers), so they
/// land in the restaurant dashboard bell and restaurant devices — never the admin.
/// </summary>
public interface IRestaurantDeliveryNotifier
{
    Task NotifyPickupCompletedAsync(
        Guid restaurantId,
        Guid tripId,
        string tripCode,
        string driverNameAr,
        string driverNameEn,
        int boxesCount,
        CancellationToken cancellationToken = default);

    Task NotifyTripDeliveredAsync(
        Guid restaurantId,
        Guid tripId,
        string tripCode,
        string driverNameAr,
        string driverNameEn,
        int boxesCount,
        CancellationToken cancellationToken = default);
}
