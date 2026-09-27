namespace MealMate.Application.Common.Abstractions;

public sealed record RestaurantPushTestResultDto(
    bool FcmConfigured,
    int ActiveTokenCount,
    int SentCount,
    int FailedCount,
    IReadOnlyList<string> Errors);

public sealed record TestPushDeliveryResultDto(
    bool IsSuccess,
    string TargetRole,
    int DevicesCount,
    string Message,
    IReadOnlyList<string>? Errors = null);

public interface IRestaurantPushTestService
{
    Task<RestaurantPushTestResultDto> SendTestAsync(
        Guid? restaurantUserId,
        string? directToken = null,
        CancellationToken cancellationToken = default);

    Task<TestPushDeliveryResultDto> SendTestToDispatcherAsync(
        Guid restaurantId,
        Guid? deliveryManagerId = null,
        string? customMessage = null,
        CancellationToken cancellationToken = default);

    Task<TestPushDeliveryResultDto> SendTestToDriverAsync(
        Guid restaurantId,
        Guid? driverId = null,
        string? customMessage = null,
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

    public Task<TestPushDeliveryResultDto> SendTestToDispatcherAsync(
        Guid restaurantId,
        Guid? deliveryManagerId = null,
        string? customMessage = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TestPushDeliveryResultDto(false, "Dispatcher", 0, "FCM is not configured.", ["FCM not configured."]));

    public Task<TestPushDeliveryResultDto> SendTestToDriverAsync(
        Guid restaurantId,
        Guid? driverId = null,
        string? customMessage = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TestPushDeliveryResultDto(false, "Driver", 0, "FCM is not configured.", ["FCM not configured."]));
}
