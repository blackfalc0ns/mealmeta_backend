namespace MealMate.Application.Common.Abstractions;

public sealed record AdminPushTestResultDto(
    bool FcmConfigured,
    int ActiveTokenCount,
    int SentCount,
    int FailedCount,
    IReadOnlyList<string> Errors);

public interface IAdminPushTestService
{
    Task<AdminPushTestResultDto> SendTestAsync(
        Guid? adminUserId,
        string? directToken = null,
        CancellationToken cancellationToken = default);
}

public sealed class NullAdminPushTestService : IAdminPushTestService
{
    public static readonly NullAdminPushTestService Instance = new();

    public Task<AdminPushTestResultDto> SendTestAsync(
        Guid? adminUserId,
        string? directToken = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new AdminPushTestResultDto(false, 0, 0, 0, ["FCM is not configured."]));
}
