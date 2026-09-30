namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Sends notifications for meal review and lifecycle events.
/// Handles both in-app notification persistence and push messaging.
/// </summary>
public interface IMealPushNotifier
{
    Task NotifyAdminsMealSubmittedAsync(
        Guid mealId,
        Guid restaurantId,
        string mealNameAr,
        string mealNameEn,
        CancellationToken cancellationToken = default);

    Task NotifyRestaurantMealApprovedAsync(
        Guid mealId,
        Guid restaurantId,
        string mealNameAr,
        string mealNameEn,
        CancellationToken cancellationToken = default);

    Task NotifyRestaurantMealRejectedAsync(
        Guid mealId,
        Guid restaurantId,
        string mealNameAr,
        string mealNameEn,
        string rejectionReason,
        CancellationToken cancellationToken = default);
}

public sealed class NullMealPushNotifier : IMealPushNotifier
{
    public static readonly NullMealPushNotifier Instance = new();

    public Task NotifyAdminsMealSubmittedAsync(
        Guid mealId,
        Guid restaurantId,
        string mealNameAr,
        string mealNameEn,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyRestaurantMealApprovedAsync(
        Guid mealId,
        Guid restaurantId,
        string mealNameAr,
        string mealNameEn,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyRestaurantMealRejectedAsync(
        Guid mealId,
        Guid restaurantId,
        string mealNameAr,
        string mealNameEn,
        string rejectionReason,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
