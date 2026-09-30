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

    Task NotifyDriverApplicationSubmittedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        Guid? restaurantId = null,
        int? reviewStage = null,
        string? applicationStatus = null,
        string? eventKey = null,
        CancellationToken cancellationToken = default);

    Task NotifyDriverRestaurantApprovedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        CancellationToken cancellationToken = default);

    Task NotifyDriverRestaurantChangesRequestedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        string notes,
        CancellationToken cancellationToken = default);

    Task NotifyDriverRestaurantRejectedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        string reason,
        CancellationToken cancellationToken = default);

    Task NotifyDriverAdminApprovedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        CancellationToken cancellationToken = default);

    Task NotifyDriverAdminChangesRequestedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string notes,
        CancellationToken cancellationToken = default);

    Task NotifyDriverAdminRejectedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string reason,
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

    public Task NotifyDriverApplicationSubmittedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        Guid? restaurantId = null,
        int? reviewStage = null,
        string? applicationStatus = null,
        string? eventKey = null,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyDriverRestaurantApprovedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyDriverRestaurantChangesRequestedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        string notes,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyDriverRestaurantRejectedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        string reason,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyDriverAdminApprovedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyDriverAdminChangesRequestedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string notes,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyDriverAdminRejectedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string reason,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

