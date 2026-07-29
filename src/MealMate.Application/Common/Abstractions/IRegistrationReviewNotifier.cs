namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Abstraction for notifying registration owners about review state changes
/// via realtime channels (e.g. SignalR). The application layer depends on
/// this interface; the API layer provides the concrete implementation.
/// </summary>
public interface IRegistrationReviewNotifier
{
    /// <summary>
    /// Notify the registration owner that their registration review state changed.
    /// </summary>
    /// <param name="userId">The owner user id to notify.</param>
    /// <param name="registrationId">The registration that changed.</param>
    /// <param name="changeType">
    /// A short machine-readable change type (e.g. "approved", "rejected",
    /// "changes-requested", "document-approved", "document-rejected").
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyRegistrationUpdated(
        Guid userId,
        Guid registrationId,
        string changeType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// No-op implementation used when no realtime notifier is registered
/// (e.g. in unit tests or when the API layer is not loaded).
/// </summary>
public sealed class NullRegistrationReviewNotifier : IRegistrationReviewNotifier
{
    public static readonly NullRegistrationReviewNotifier Instance = new();

    public Task NotifyRegistrationUpdated(
        Guid userId,
        Guid registrationId,
        string changeType,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
