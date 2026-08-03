using MealMate.Application.Common.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Realtime;

/// <summary>
/// SignalR-backed implementation of <see cref="IRegistrationReviewNotifier"/>.
/// Sends the <c>registration-review-updated</c> event to the <c>user:{userId}</c>
/// group. The payload contains only <c>registrationId</c> and <c>changeType</c> —
/// no sensitive fields are transmitted over the wire.
/// </summary>
public sealed class SignalRRegistrationReviewNotifier : IRegistrationReviewNotifier
{
    internal const string EventName = "registration-review-updated";

    private readonly IHubContext<RestaurantReviewHub> _hubContext;

    public SignalRRegistrationReviewNotifier(IHubContext<RestaurantReviewHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyRegistrationUpdated(
        Guid userId,
        Guid registrationId,
        string changeType,
        CancellationToken cancellationToken = default)
    {
        var group = RestaurantReviewHub.GroupForUser(userId.ToString());
        return _hubContext.Clients.Group(group).SendAsync(
            EventName,
            new { registrationId, changeType },
            cancellationToken);
    }
}
