using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.RestaurantMeals;
using MealMate.Domain.Common;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantMeals;

public class MealNotificationLifecycleHandlerTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IMealPushNotifier _pushNotifier = Substitute.For<IMealPushNotifier>();
    private readonly DateTime _now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    public MealNotificationLifecycleHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);
    }

    [Fact]
    public async Task MealSubmitted_WhenNewEvent_CallsMealPushNotifierAndRecordsInboxEvent()
    {
        var handler = new MealNotificationLifecycleHandler(_context, _clock, _pushNotifier);
        var messageId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            MealId = mealId,
            RestaurantId = restaurantId,
            MealNameAr = "وجبة صحية",
            MealNameEn = "Healthy Meal"
        });
        var notification = new OutboxLifecycleNotification(messageId, "MealSubmitted", "Meal", mealId.ToString(), null, null, payload, _now);

        await handler.Handle(notification, CancellationToken.None);

        await _pushNotifier.Received(1).NotifyAdminsMealSubmittedAsync(
            mealId,
            restaurantId,
            "وجبة صحية",
            "Healthy Meal",
            Arg.Any<CancellationToken>());

        var inbox = await _context.InboxProcessedEvents.FirstOrDefaultAsync(
            e => e.ConsumerName == MealNotificationLifecycleHandler.SubmittedConsumerName && e.EventId == messageId);
        inbox.Should().NotBeNull();
        inbox!.ProcessedAtUtc.Should().Be(_now);
    }

    [Fact]
    public async Task MealSubmitted_WhenAlreadyProcessed_SkipsExecution()
    {
        var handler = new MealNotificationLifecycleHandler(_context, _clock, _pushNotifier);
        var messageId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        _context.InboxProcessedEvents.Add(new InboxProcessedEvent
        {
            ConsumerName = MealNotificationLifecycleHandler.SubmittedConsumerName,
            EventId = messageId,
            ProcessedAtUtc = _now.AddMinutes(-5)
        });
        await _context.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new
        {
            MealId = mealId,
            RestaurantId = restaurantId,
            MealNameAr = "وجبة صحية",
            MealNameEn = "Healthy Meal"
        });
        var notification = new OutboxLifecycleNotification(messageId, "MealSubmitted", "Meal", mealId.ToString(), null, null, payload, _now);

        await handler.Handle(notification, CancellationToken.None);

        await _pushNotifier.DidNotReceive().NotifyAdminsMealSubmittedAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MealApproved_WhenNewEvent_CallsMealPushNotifierAndRecordsInboxEvent()
    {
        var handler = new MealNotificationLifecycleHandler(_context, _clock, _pushNotifier);
        var messageId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            MealId = mealId,
            RestaurantId = restaurantId,
            MealNameAr = "وجبة صحية",
            MealNameEn = "Healthy Meal"
        });
        var notification = new OutboxLifecycleNotification(messageId, "MealApproved", "Meal", mealId.ToString(), null, null, payload, _now);

        await handler.Handle(notification, CancellationToken.None);

        await _pushNotifier.Received(1).NotifyRestaurantMealApprovedAsync(
            mealId,
            restaurantId,
            "وجبة صحية",
            "Healthy Meal",
            Arg.Any<CancellationToken>());

        var inbox = await _context.InboxProcessedEvents.FirstOrDefaultAsync(
            e => e.ConsumerName == MealNotificationLifecycleHandler.ApprovedConsumerName && e.EventId == messageId);
        inbox.Should().NotBeNull();
    }

    [Fact]
    public async Task MealApproved_WhenAlreadyProcessed_SkipsExecution()
    {
        var handler = new MealNotificationLifecycleHandler(_context, _clock, _pushNotifier);
        var messageId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        _context.InboxProcessedEvents.Add(new InboxProcessedEvent
        {
            ConsumerName = MealNotificationLifecycleHandler.ApprovedConsumerName,
            EventId = messageId,
            ProcessedAtUtc = _now.AddMinutes(-5)
        });
        await _context.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new
        {
            MealId = mealId,
            RestaurantId = restaurantId,
            MealNameAr = "وجبة صحية",
            MealNameEn = "Healthy Meal"
        });
        var notification = new OutboxLifecycleNotification(messageId, "MealApproved", "Meal", mealId.ToString(), null, null, payload, _now);

        await handler.Handle(notification, CancellationToken.None);

        await _pushNotifier.DidNotReceive().NotifyRestaurantMealApprovedAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MealRejected_WhenNewEvent_CallsMealPushNotifierAndRecordsInboxEvent()
    {
        var handler = new MealNotificationLifecycleHandler(_context, _clock, _pushNotifier);
        var messageId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var reason = "Incomplete ingredients list";
        var payload = JsonSerializer.Serialize(new
        {
            MealId = mealId,
            RestaurantId = restaurantId,
            MealNameAr = "وجبة صحية",
            MealNameEn = "Healthy Meal",
            RejectionReason = reason
        });
        var notification = new OutboxLifecycleNotification(messageId, "MealRejected", "Meal", mealId.ToString(), null, null, payload, _now);

        await handler.Handle(notification, CancellationToken.None);

        await _pushNotifier.Received(1).NotifyRestaurantMealRejectedAsync(
            mealId,
            restaurantId,
            "وجبة صحية",
            "Healthy Meal",
            reason,
            Arg.Any<CancellationToken>());

        var inbox = await _context.InboxProcessedEvents.FirstOrDefaultAsync(
            e => e.ConsumerName == MealNotificationLifecycleHandler.RejectedConsumerName && e.EventId == messageId);
        inbox.Should().NotBeNull();
    }
}
