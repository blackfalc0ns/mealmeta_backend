using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.EventHandlers;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.Common;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using MediatR;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignNotificationEventHandlersTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ICampaignNotificationSender _sender = Substitute.For<ICampaignNotificationSender>();
    private readonly DateTime _now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    public CampaignNotificationEventHandlersTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);
    }

    [Fact]
    public async Task InvitationHandler_WhenNewEvent_SendsNotificationAndRecordsInboxEvent()
    {
        var handler = new CampaignInvitationNotificationHandler(_context, _clock, _sender);
        var messageId = Guid.NewGuid();
        var payload = "{\"CampaignId\":\"11111111-1111-1111-1111-111111111111\",\"RestaurantId\":\"22222222-2222-2222-2222-222222222222\",\"RecipientEmail\":\"owner@restaurant.com\"}";
        var notification = new OutboxMessageNotification(messageId, "CampaignInvitationSent", payload, _now);

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert
        await _sender.Received(1).SendEmailAsync(
            "owner@restaurant.com",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        var inboxEvent = await _context.InboxProcessedEvents.FirstOrDefaultAsync(
            e => e.ConsumerName == CampaignInvitationNotificationHandler.ConsumerName && e.EventId == messageId);
        inboxEvent.Should().NotBeNull();
        inboxEvent!.ProcessedAtUtc.Should().Be(_now);
    }

    [Fact]
    public async Task InvitationHandler_WhenAlreadyProcessed_DoesNotSendDuplicateNotification()
    {
        var handler = new CampaignInvitationNotificationHandler(_context, _clock, _sender);
        var messageId = Guid.NewGuid();

        _context.InboxProcessedEvents.Add(new InboxProcessedEvent
        {
            ConsumerName = CampaignInvitationNotificationHandler.ConsumerName,
            EventId = messageId,
            ProcessedAtUtc = _now.AddMinutes(-10)
        });
        await _context.SaveChangesAsync();

        var notification = new OutboxMessageNotification(
            messageId,
            "CampaignInvitationSent",
            "{\"RecipientEmail\":\"owner@restaurant.com\"}",
            _now);

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert
        await _sender.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CapacityAlertHandler_WhenNewEvent_SendsAlertAndRecordsInboxEvent()
    {
        var handler = new CapacityChangeAlertNotificationHandler(_context, _clock, _sender);
        var messageId = Guid.NewGuid();
        var payload = "{\"CampaignId\":\"11111111-1111-1111-1111-111111111111\",\"RecipientPhone\":\"+96590000000\",\"Message\":\"Capacity change requested\"}";
        var notification = new OutboxMessageNotification(messageId, "CampaignCapacityChangeAlert", payload, _now);

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert
        await _sender.Received(1).SendSmsAsync(
            "+96590000000",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        var inboxEvent = await _context.InboxProcessedEvents.FirstOrDefaultAsync(
            e => e.ConsumerName == CapacityChangeAlertNotificationHandler.ConsumerName && e.EventId == messageId);
        inboxEvent.Should().NotBeNull();
    }

    [Fact]
    public async Task CapacityAlertHandler_WhenAlreadyProcessed_SkipsExecution()
    {
        var handler = new CapacityChangeAlertNotificationHandler(_context, _clock, _sender);
        var messageId = Guid.NewGuid();

        _context.InboxProcessedEvents.Add(new InboxProcessedEvent
        {
            ConsumerName = CapacityChangeAlertNotificationHandler.ConsumerName,
            EventId = messageId,
            ProcessedAtUtc = _now.AddMinutes(-5)
        });
        await _context.SaveChangesAsync();

        var notification = new OutboxMessageNotification(
            messageId,
            "CampaignCapacityChangeAlert",
            "{\"RecipientPhone\":\"+96590000000\"}",
            _now);

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert
        await _sender.DidNotReceive().SendSmsAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LaunchNoticeHandler_WhenNewEvent_SendsPushAndRecordsInboxEvent()
    {
        var handler = new CampaignLaunchNoticeNotificationHandler(_context, _clock, _sender);
        var messageId = Guid.NewGuid();
        var payload = "{\"CampaignId\":\"11111111-1111-1111-1111-111111111111\",\"RecipientUserId\":\"user_123\",\"Title\":\"Campaign Launched!\",\"Body\":\"A new discount campaign is now active.\"}";
        var notification = new OutboxMessageNotification(messageId, "CampaignLaunchNotice", payload, _now);

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert
        await _sender.Received(1).SendPushNotificationAsync(
            "user_123",
            "Campaign Launched!",
            "A new discount campaign is now active.",
            Arg.Any<CancellationToken>());

        var inboxEvent = await _context.InboxProcessedEvents.FirstOrDefaultAsync(
            e => e.ConsumerName == CampaignLaunchNoticeNotificationHandler.ConsumerName && e.EventId == messageId);
        inboxEvent.Should().NotBeNull();
    }

    [Fact]
    public async Task LaunchNoticeHandler_WhenAlreadyProcessed_SkipsExecution()
    {
        var handler = new CampaignLaunchNoticeNotificationHandler(_context, _clock, _sender);
        var messageId = Guid.NewGuid();

        _context.InboxProcessedEvents.Add(new InboxProcessedEvent
        {
            ConsumerName = CampaignLaunchNoticeNotificationHandler.ConsumerName,
            EventId = messageId,
            ProcessedAtUtc = _now.AddMinutes(-5)
        });
        await _context.SaveChangesAsync();

        var notification = new OutboxMessageNotification(
            messageId,
            "CampaignLaunchNotice",
            "{\"RecipientUserId\":\"user_123\"}",
            _now);

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert
        await _sender.DidNotReceive().SendPushNotificationAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handlers_WhenEventTypeDoesNotMatch_IgnoresMessage()
    {
        var handler = new CampaignInvitationNotificationHandler(_context, _clock, _sender);
        var messageId = Guid.NewGuid();
        var notification = new OutboxMessageNotification(messageId, "UnrelatedEvent", "{}", _now);

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert
        await _sender.DidNotReceive().SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _context.InboxProcessedEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InvitationHandler_WhenLifecycleNotification_ProcessesSuccessfully()
    {
        var handler = new CampaignInvitationNotificationHandler(_context, _clock, _sender);
        var messageId = Guid.NewGuid();
        var payload = "{\"CampaignId\":\"11111111-1111-1111-1111-111111111111\",\"RestaurantId\":\"22222222-2222-2222-2222-222222222222\",\"RecipientEmail\":\"unified@restaurant.com\"}";
        var notification = new MealMate.Application.Common.Outbox.OutboxLifecycleNotification(
            messageId,
            "CampaignInvitationSent",
            "Campaign",
            "11111111-1111-1111-1111-111111111111",
            null,
            null,
            payload,
            _now);

        // Act: Must implement INotificationHandler<OutboxLifecycleNotification> for unified dispatch
        INotificationHandler<MealMate.Application.Common.Outbox.OutboxLifecycleNotification> handlerInterface = handler;
        await handlerInterface.Handle(notification, CancellationToken.None);

        // Assert
        await _sender.Received(1).SendEmailAsync(
            "unified@restaurant.com",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        var inboxEvent = await _context.InboxProcessedEvents.FirstOrDefaultAsync(
            e => e.ConsumerName == CampaignInvitationNotificationHandler.ConsumerName && e.EventId == messageId);
        inboxEvent.Should().NotBeNull();
        inboxEvent!.ProcessedAtUtc.Should().Be(_now);
    }
}

