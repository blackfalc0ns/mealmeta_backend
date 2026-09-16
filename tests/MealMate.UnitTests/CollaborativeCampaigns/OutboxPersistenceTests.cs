using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class OutboxPersistenceTests
{
    private readonly AppDbContext _context;

    public OutboxPersistenceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
    }

    [Fact]
    public void Model_ContainsOutboxMessage_WithCorrectTableAndProperties()
    {
        var entityType = _context.Model.FindEntityType(typeof(OutboxMessage));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("OutboxMessages");

        var primaryKey = entityType.FindPrimaryKey();
        primaryKey.Should().NotBeNull();
        primaryKey!.Properties.Select(p => p.Name).Should().Contain("Id");

        var eventTypeProp = entityType.FindProperty(nameof(OutboxMessage.EventType));
        eventTypeProp.Should().NotBeNull();
        eventTypeProp!.IsNullable.Should().BeFalse();
        eventTypeProp.GetMaxLength().Should().Be(256);

        var payloadProp = entityType.FindProperty(nameof(OutboxMessage.PayloadJson));
        payloadProp.Should().NotBeNull();
        payloadProp!.IsNullable.Should().BeFalse();

        var createdAtProp = entityType.FindProperty(nameof(OutboxMessage.CreatedAtUtc));
        createdAtProp.Should().NotBeNull();
        createdAtProp!.IsNullable.Should().BeFalse();

        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(OutboxMessage.ProcessedAtUtc))
                              && i.Properties.Any(p => p.Name == nameof(OutboxMessage.CreatedAtUtc)));
        index.Should().NotBeNull();
    }

    [Fact]
    public void Model_ContainsInboxProcessedEvent_WithUniqueIndexOnConsumerAndEventId()
    {
        var entityType = _context.Model.FindEntityType(typeof(InboxProcessedEvent));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("InboxProcessedEvents");

        var primaryKey = entityType.FindPrimaryKey();
        primaryKey.Should().NotBeNull();
        primaryKey!.Properties.Select(p => p.Name).Should().Contain("Id");

        var consumerProp = entityType.FindProperty(nameof(InboxProcessedEvent.ConsumerName));
        consumerProp.Should().NotBeNull();
        consumerProp!.IsNullable.Should().BeFalse();
        consumerProp.GetMaxLength().Should().Be(256);

        var eventIdProp = entityType.FindProperty(nameof(InboxProcessedEvent.EventId));
        eventIdProp.Should().NotBeNull();
        eventIdProp!.IsNullable.Should().BeFalse();

        var uniqueIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(InboxProcessedEvent.ConsumerName))
                              && i.Properties.Any(p => p.Name == nameof(InboxProcessedEvent.EventId)));
        uniqueIndex.Should().NotBeNull();
        uniqueIndex!.IsUnique.Should().BeTrue();
    }

    [Fact]
    public async Task OutboxMessage_CanBePersistedAndRetrieved()
    {
        var msg = new OutboxMessage
        {
            EventType = "CampaignStatusChanged",
            PayloadJson = "{\"CampaignId\":\"11111111-1111-1111-1111-111111111111\"}",
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };

        _context.OutboxMessages.Add(msg);
        await _context.SaveChangesAsync();

        var retrieved = await _context.OutboxMessages.FindAsync(msg.Id);
        retrieved.Should().NotBeNull();
        retrieved!.EventType.Should().Be("CampaignStatusChanged");
        retrieved.PayloadJson.Should().Contain("11111111-1111-1111-1111-111111111111");
        retrieved.ProcessedAtUtc.Should().BeNull();
        retrieved.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task InboxProcessedEvent_CanBePersistedAndRetrieved()
    {
        var eventId = Guid.NewGuid();
        var inboxEvent = new InboxProcessedEvent
        {
            ConsumerName = "CampaignInvitationNotificationConsumer",
            EventId = eventId,
            ProcessedAtUtc = DateTime.UtcNow
        };

        _context.InboxProcessedEvents.Add(inboxEvent);
        await _context.SaveChangesAsync();

        var retrieved = await _context.InboxProcessedEvents.FindAsync(inboxEvent.Id);
        retrieved.Should().NotBeNull();
        retrieved!.ConsumerName.Should().Be("CampaignInvitationNotificationConsumer");
        retrieved.EventId.Should().Be(eventId);
    }

    [Fact]
    public async Task OutboxMessage_MarksProcessedAndIncrementsRetry()
    {
        var msg = new OutboxMessage
        {
            EventType = "CampaignCreated",
            PayloadJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };
        _context.OutboxMessages.Add(msg);
        await _context.SaveChangesAsync();

        // Simulate failure then success
        msg.RetryCount++;
        msg.LastError = "Temporary network timeout";
        await _context.SaveChangesAsync();

        var failedState = await _context.OutboxMessages.FindAsync(msg.Id);
        failedState!.RetryCount.Should().Be(1);
        failedState.LastError.Should().Be("Temporary network timeout");
        failedState.ProcessedAtUtc.Should().BeNull();

        // Simulate success
        msg.ProcessedAtUtc = DateTime.UtcNow;
        msg.LastError = null;
        await _context.SaveChangesAsync();

        var processedState = await _context.OutboxMessages.FindAsync(msg.Id);
        processedState!.ProcessedAtUtc.Should().NotBeNull();
        processedState.LastError.Should().BeNull();
    }
}
