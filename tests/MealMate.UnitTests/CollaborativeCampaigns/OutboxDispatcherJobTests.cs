using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.Common;
using MealMate.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class OutboxDispatcherJobTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly OutboxDispatcherService _service;
    private readonly DateTime _now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    public OutboxDispatcherJobTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _service = new OutboxDispatcherService(_context, _clock, _mediator);
    }

    private OutboxMessage CreateMessage(string eventType, string payload, int retryCount = 0, DateTime? processedAt = null)
    {
        var msg = new OutboxMessage
        {
            EventType = eventType,
            PayloadJson = payload,
            CreatedAtUtc = _now.AddMinutes(-5),
            RetryCount = retryCount,
            ProcessedAtUtc = processedAt
        };
        _context.OutboxMessages.Add(msg);
        return msg;
    }

    [Fact]
    public async Task DispatchPendingMessagesAsync_WhenPendingMessagesExist_DispatchesAndMarksProcessed()
    {
        // Arrange
        var msg = CreateMessage("CampaignCreated", "{\"CampaignId\":\"11111111-1111-1111-1111-111111111111\"}");
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.DispatchPendingMessagesAsync();

        // Assert
        result.ProcessedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.DeadLetteredCount.Should().Be(0);

        await _mediator.Received(1).Publish(
            Arg.Is<OutboxMessageNotification>(n => n.MessageId == msg.Id && n.EventType == "CampaignCreated"),
            Arg.Any<CancellationToken>());

        var updated = await _context.OutboxMessages.FindAsync(msg.Id);
        updated!.ProcessedAtUtc.Should().Be(_now);
        updated.LastError.Should().BeNull();
    }

    [Fact]
    public async Task DispatchPendingMessagesAsync_WhenDispatchFails_IncrementsRetryCountAndRecordsLastError()
    {
        // Arrange
        var msg = CreateMessage("CampaignInvitationSent", "{}");
        await _context.SaveChangesAsync();

        _mediator.Publish(Arg.Any<OutboxMessageNotification>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Gateway timeout"));

        // Act
        var result = await _service.DispatchPendingMessagesAsync();

        // Assert
        result.ProcessedCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.DeadLetteredCount.Should().Be(0);

        var updated = await _context.OutboxMessages.FindAsync(msg.Id);
        updated!.ProcessedAtUtc.Should().BeNull();
        updated.RetryCount.Should().Be(1);
        updated.LastError.Should().Be("Gateway timeout");
        updated.LastAttemptedAtUtc.Should().Be(_now);
    }

    [Fact]
    public async Task DispatchPendingMessagesAsync_WhenMaxRetriesExceeded_MarksDeadLettered()
    {
        // Arrange
        var msg = CreateMessage("CampaignLaunchAlert", "{}", retryCount: 4);
        await _context.SaveChangesAsync();

        _mediator.Publish(Arg.Any<OutboxMessageNotification>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Fatal consumer exception"));

        // Act
        var result = await _service.DispatchPendingMessagesAsync();

        // Assert
        result.ProcessedCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.DeadLetteredCount.Should().Be(1);

        var updated = await _context.OutboxMessages.FindAsync(msg.Id);
        updated!.RetryCount.Should().Be(5);
        updated.LastError.Should().Be("Fatal consumer exception");
        updated.ProcessedAtUtc.Should().BeNull();

        // Next run should ignore dead-lettered message
        var nextResult = await _service.DispatchPendingMessagesAsync();
        nextResult.ProcessedCount.Should().Be(0);
        nextResult.FailedCount.Should().Be(0);
        nextResult.DeadLetteredCount.Should().Be(0);
    }

    [Fact]
    public async Task DispatchPendingMessagesAsync_RespectsExponentialBackoff()
    {
        // Arrange: retryCount = 1 -> delay = 2^1 = 2 seconds
        var msg = CreateMessage("CampaignCapacityAlert", "{}", retryCount: 1);
        msg.LastAttemptedAtUtc = _now.AddSeconds(-1); // only 1 second elapsed, needs 2 seconds
        await _context.SaveChangesAsync();

        // Act 1: Attempt at _now (too soon)
        var result1 = await _service.DispatchPendingMessagesAsync();

        // Assert 1: Skipped due to backoff
        result1.ProcessedCount.Should().Be(0);
        await _mediator.DidNotReceive().Publish(Arg.Any<OutboxMessageNotification>(), Arg.Any<CancellationToken>());

        // Act 2: Advance clock by 3 seconds (now 4 seconds after last attempt > 2 seconds backoff)
        _clock.UtcNow.Returns(_now.AddSeconds(3));
        var result2 = await _service.DispatchPendingMessagesAsync();

        // Assert 2: Dispatched
        result2.ProcessedCount.Should().Be(1);
        await _mediator.Received(1).Publish(
            Arg.Is<OutboxMessageNotification>(n => n.MessageId == msg.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingMessagesAsync_DoesNotReProcessAlreadyProcessedMessages()
    {
        // Arrange
        CreateMessage("CampaignEnded", "{}", processedAt: _now.AddMinutes(-10));
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.DispatchPendingMessagesAsync();

        // Assert
        result.ProcessedCount.Should().Be(0);
        await _mediator.DidNotReceive().Publish(Arg.Any<OutboxMessageNotification>(), Arg.Any<CancellationToken>());
    }
}
