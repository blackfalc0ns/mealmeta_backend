using System;
using System.Threading;
using System.Threading.Tasks;

namespace MealMate.Application.Common.Abstractions;

public interface IDriverRegistrationNotificationService
{
    Task NotifyRestaurantDriverApplicationSubmittedAsync(
        Guid restaurantId,
        Guid registrationId,
        string driverName,
        string phone,
        CancellationToken cancellationToken = default);

    Task NotifyRestaurantDriverApplicationResubmittedAsync(
        Guid restaurantId,
        Guid registrationId,
        string driverName,
        string phone,
        CancellationToken cancellationToken = default);

    Task NotifyAdminsDriverApplicationReadyForConfirmationAsync(
        Guid registrationId,
        Guid restaurantId,
        string restaurantName,
        string driverName,
        string phone,
        CancellationToken cancellationToken = default);
}

public sealed class NullDriverRegistrationNotificationService : IDriverRegistrationNotificationService
{
    public static readonly NullDriverRegistrationNotificationService Instance = new();

    public Task NotifyRestaurantDriverApplicationSubmittedAsync(
        Guid restaurantId,
        Guid registrationId,
        string driverName,
        string phone,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyRestaurantDriverApplicationResubmittedAsync(
        Guid restaurantId,
        Guid registrationId,
        string driverName,
        string phone,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyAdminsDriverApplicationReadyForConfirmationAsync(
        Guid registrationId,
        Guid restaurantId,
        string restaurantName,
        string driverName,
        string phone,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
