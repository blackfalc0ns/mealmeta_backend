using MealMate.Domain.Enums;

namespace MealMate.Application.Common.Abstractions;

public interface IRegistrationPushNotificationService
{
    Task NotifyAdminsNewRegistrationAsync(
        Guid registrationId,
        string restaurantName,
        CancellationToken cancellationToken = default);

    Task NotifyAdminsNewRestaurantAccountCreatedAsync(
        Guid userId,
        string ownerName,
        string email,
        string phone,
        CancellationToken cancellationToken = default);

    Task NotifyAdminsRestaurantDraftCreatedAsync(
        Guid registrationId,
        string restaurantName,
        CancellationToken cancellationToken = default);

    Task NotifyRestaurantFieldRejectedAsync(
        Guid ownerUserId,
        string fieldKey,
        string reason,
        CancellationToken cancellationToken = default);

    Task NotifyRestaurantDocumentRejectedAsync(
        Guid ownerUserId,
        DocumentType documentType,
        string reason,
        CancellationToken cancellationToken = default);

    Task NotifyRestaurantSectionApprovedAsync(
        Guid ownerUserId,
        string sectionTitleAr,
        string sectionKey,
        CancellationToken cancellationToken = default);

    Task NotifyRestaurantRegistrationApprovedAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    Task NotifyRestaurantRegistrationRejectedAsync(
        Guid ownerUserId,
        string reason,
        CancellationToken cancellationToken = default);

    Task NotifyRestaurantRegistrationChangesRequestedAsync(
        Guid ownerUserId,
        string reason,
        CancellationToken cancellationToken = default);
}

public sealed class NullRegistrationPushNotificationService : IRegistrationPushNotificationService
{
    public static readonly NullRegistrationPushNotificationService Instance = new();

    public Task NotifyAdminsNewRegistrationAsync(
        Guid registrationId,
        string restaurantName,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyAdminsNewRestaurantAccountCreatedAsync(
        Guid userId,
        string ownerName,
        string email,
        string phone,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyAdminsRestaurantDraftCreatedAsync(
        Guid registrationId,
        string restaurantName,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyRestaurantFieldRejectedAsync(
        Guid ownerUserId,
        string fieldKey,
        string reason,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyRestaurantDocumentRejectedAsync(
        Guid ownerUserId,
        DocumentType documentType,
        string reason,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyRestaurantSectionApprovedAsync(
        Guid ownerUserId,
        string sectionTitleAr,
        string sectionKey,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyRestaurantRegistrationApprovedAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyRestaurantRegistrationRejectedAsync(
        Guid ownerUserId,
        string reason,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyRestaurantRegistrationChangesRequestedAsync(
        Guid ownerUserId,
        string reason,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
