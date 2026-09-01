namespace MealMate.Application.Common.Abstractions;

public sealed record CustomerPushTestResultDto(
    bool FcmConfigured,
    int ActiveTokenCount,
    int SentCount,
    int FailedCount,
    IReadOnlyList<string> Errors);

public interface ICustomerPushTestService
{
    Task<CustomerPushTestResultDto> SendTestAsync(
        Guid customerUserId,
        CancellationToken cancellationToken = default);
}

public sealed class NullCustomerPushTestService : ICustomerPushTestService
{
    public static readonly NullCustomerPushTestService Instance = new();

    public Task<CustomerPushTestResultDto> SendTestAsync(
        Guid customerUserId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new CustomerPushTestResultDto(false, 0, 0, 0, ["FCM is not configured."]));
}
