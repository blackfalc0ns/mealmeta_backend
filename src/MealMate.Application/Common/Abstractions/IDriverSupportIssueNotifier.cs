namespace MealMate.Application.Common.Abstractions;

public sealed record DriverSupportIssueEvent(Guid EventId, DateTime OccurredAtUtc, Guid IssueId,
    Guid RestaurantId, Guid? DriverProfileId, string Category, string Event);

public interface IDriverSupportIssueNotifier
{
    Task PublishAsync(DriverSupportIssueEvent message, CancellationToken cancellationToken);
}
