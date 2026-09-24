namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Sends high-priority push notifications to driver mobile devices.
/// Failures must be logged and not block the originating business workflow.
/// </summary>
public interface IDriverPushNotifier
{
    Task NotifyBoxAssignedAsync(
        Guid driverUserId,
        string boxCode,
        string zoneName,
        CancellationToken cancellationToken = default);

    Task NotifyTripAssignedAsync(
        Guid driverUserId,
        string tripCode,
        int boxesCount,
        CancellationToken cancellationToken = default);

    Task NotifyKitchenReadyForPickupAsync(
        Guid driverUserId,
        string tripCode,
        CancellationToken cancellationToken = default);
}

public sealed class NullDriverPushNotifier : IDriverPushNotifier
{
    public static readonly NullDriverPushNotifier Instance = new();

    public Task NotifyBoxAssignedAsync(Guid driverUserId, string boxCode, string zoneName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyTripAssignedAsync(Guid driverUserId, string tripCode, int boxesCount, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyKitchenReadyForPickupAsync(Guid driverUserId, string tripCode, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
