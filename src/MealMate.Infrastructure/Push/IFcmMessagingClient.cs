namespace MealMate.Infrastructure.Push;

public sealed record FcmSendResult(bool IsSuccess, bool IsInvalidToken, string? ErrorMessage)
{
    public static FcmSendResult Success() => new(true, false, null);
    public static FcmSendResult InvalidToken(string? message = null) => new(false, true, message);
    public static FcmSendResult Failed(string? message) => new(false, false, message);
}

public interface IFcmMessagingClient
{
    bool IsConfigured { get; }

    Task<FcmSendResult> SendAsync(
        string deviceToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken = default);
}

public sealed class NullFcmMessagingClient : IFcmMessagingClient
{
    public static readonly NullFcmMessagingClient Instance = new();
    public bool IsConfigured => false;

    public Task<FcmSendResult> SendAsync(
        string deviceToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken = default)
        => Task.FromResult(FcmSendResult.Failed("FCM is not configured."));
}
