using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Common;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class RestaurantRegistrationLifecycleHandlerTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IRegistrationPushNotificationService _pushNotifier = Substitute.For<IRegistrationPushNotificationService>();
    private readonly IRegistrationReviewNotifier _notifier = Substitute.For<IRegistrationReviewNotifier>();
    private readonly DateTime _now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    public RestaurantRegistrationLifecycleHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);
    }

    [Fact]
    public async Task RegistrationSubmitted_WhenNewEvent_CallsNotifyAdminsAndRecordsInboxEvent()
    {
        var handler = new RestaurantRegistrationLifecycleHandler(_context, _clock, _pushNotifier, _notifier);
        var messageId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            RegistrationId = registrationId,
            RestaurantName = "مطعم الورد",
            SubmittedByUserId = userId
        });
        var notification = new OutboxLifecycleNotification(messageId, "RestaurantRegistrationSubmitted", "RestaurantRegistration", registrationId.ToString(), null, null, payload, _now);

        await handler.Handle(notification, CancellationToken.None);

        await _pushNotifier.Received(1).NotifyAdminsNewRegistrationAsync(
            registrationId,
            "مطعم الورد",
            Arg.Any<CancellationToken>());

        var inbox = await _context.InboxProcessedEvents.FirstOrDefaultAsync(
            e => e.ConsumerName == RestaurantRegistrationLifecycleHandler.SubmittedConsumerName && e.EventId == messageId);
        inbox.Should().NotBeNull();
        inbox!.ProcessedAtUtc.Should().Be(_now);
    }

    [Fact]
    public async Task RegistrationSubmitted_WhenAlreadyProcessed_SkipsExecution()
    {
        var handler = new RestaurantRegistrationLifecycleHandler(_context, _clock, _pushNotifier, _notifier);
        var messageId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();

        _context.InboxProcessedEvents.Add(new InboxProcessedEvent
        {
            ConsumerName = RestaurantRegistrationLifecycleHandler.SubmittedConsumerName,
            EventId = messageId,
            ProcessedAtUtc = _now.AddMinutes(-5)
        });
        await _context.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new
        {
            RegistrationId = registrationId,
            RestaurantName = "مطعم الورد"
        });
        var notification = new OutboxLifecycleNotification(messageId, "RestaurantRegistrationSubmitted", "RestaurantRegistration", registrationId.ToString(), null, null, payload, _now);

        await handler.Handle(notification, CancellationToken.None);

        await _pushNotifier.DidNotReceive().NotifyAdminsNewRegistrationAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrationApproved_WhenNewEvent_CallsNotifierAndPushServiceAndRecordsInboxEvent()
    {
        var handler = new RestaurantRegistrationLifecycleHandler(_context, _clock, _pushNotifier, _notifier);
        var messageId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            RegistrationId = registrationId,
            RestaurantId = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            RestaurantName = "مطعم النور"
        });
        var notification = new OutboxLifecycleNotification(messageId, "RestaurantRegistrationApproved", "RestaurantRegistration", registrationId.ToString(), null, null, payload, _now);

        await handler.Handle(notification, CancellationToken.None);

        await _notifier.Received(1).NotifyRegistrationUpdated(
            ownerUserId,
            registrationId,
            "approved",
            Arg.Any<CancellationToken>());

        await _pushNotifier.Received(1).NotifyRestaurantRegistrationApprovedAsync(
            ownerUserId,
            Arg.Any<CancellationToken>());

        var inbox = await _context.InboxProcessedEvents.FirstOrDefaultAsync(
            e => e.ConsumerName == RestaurantRegistrationLifecycleHandler.ApprovedConsumerName && e.EventId == messageId);
        inbox.Should().NotBeNull();
    }

    [Fact]
    public async Task RegistrationRejected_WhenNewEvent_CallsNotifierAndPushServiceAndRecordsInboxEvent()
    {
        var handler = new RestaurantRegistrationLifecycleHandler(_context, _clock, _pushNotifier, _notifier);
        var messageId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var reason = "Invalid licenses";
        var payload = JsonSerializer.Serialize(new
        {
            RegistrationId = registrationId,
            OwnerUserId = ownerUserId,
            Reason = reason
        });
        var notification = new OutboxLifecycleNotification(messageId, "RestaurantRegistrationRejected", "RestaurantRegistration", registrationId.ToString(), null, null, payload, _now);

        await handler.Handle(notification, CancellationToken.None);

        await _notifier.Received(1).NotifyRegistrationUpdated(
            ownerUserId,
            registrationId,
            "rejected",
            Arg.Any<CancellationToken>());

        await _pushNotifier.Received(1).NotifyRestaurantRegistrationRejectedAsync(
            ownerUserId,
            reason,
            Arg.Any<CancellationToken>());

        var inbox = await _context.InboxProcessedEvents.FirstOrDefaultAsync(
            e => e.ConsumerName == RestaurantRegistrationLifecycleHandler.RejectedConsumerName && e.EventId == messageId);
        inbox.Should().NotBeNull();
    }

    [Fact]
    public async Task RegistrationChangesRequested_WhenNewEvent_CallsNotifierAndPushServiceAndRecordsInboxEvent()
    {
        var handler = new RestaurantRegistrationLifecycleHandler(_context, _clock, _pushNotifier, _notifier);
        var messageId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var reason = "Please update commercial license";
        var payload = JsonSerializer.Serialize(new
        {
            RegistrationId = registrationId,
            OwnerUserId = ownerUserId,
            Reason = reason
        });
        var notification = new OutboxLifecycleNotification(messageId, "RestaurantRegistrationChangesRequested", "RestaurantRegistration", registrationId.ToString(), null, null, payload, _now);

        await handler.Handle(notification, CancellationToken.None);

        await _notifier.Received(1).NotifyRegistrationUpdated(
            ownerUserId,
            registrationId,
            "changes-requested",
            Arg.Any<CancellationToken>());

        await _pushNotifier.Received(1).NotifyRestaurantRegistrationChangesRequestedAsync(
            ownerUserId,
            reason,
            Arg.Any<CancellationToken>());

        var inbox = await _context.InboxProcessedEvents.FirstOrDefaultAsync(
            e => e.ConsumerName == RestaurantRegistrationLifecycleHandler.ChangesRequestedConsumerName && e.EventId == messageId);
        inbox.Should().NotBeNull();
    }
}
