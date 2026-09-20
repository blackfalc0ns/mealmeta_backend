namespace MealMate.Application.Common.Abstractions;

public sealed record RestaurantPushTestResultDto(
    bool FcmConfigured,
    int ActiveTokenCount,
    int SentCount,
    int FailedCount,
    IReadOnlyList<string> Errors);

public interface IRestaurantPushTestService
{
    Task<RestaurantPushTestResultDto> SendTestAsync(
        Guid? restaurantUserId,
        string? directToken = null,
        CancellationToken cancellationToken = default);
}

public sealed class NullRestaurantPushTestService : IRestaurantPushTestService
{
    public static readonly NullRestaurantPushTestService Instance = new();

    public Task<RestaurantPushTestResultDto> SendTestAsync(
        Guid? restaurantUserId,
        string? directToken = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new RestaurantPushTestResultDto(false, 0, 0, 0, ["FCM is not configured."]));
}
