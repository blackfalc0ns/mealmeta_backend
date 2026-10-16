using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using MealMate.Domain.Common;
using MealMate.Persistence;
using MealMate.Persistence.Outbox;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Outbox;

public class OutboxPipelineTests
{
    private static IServiceProvider BuildServiceProvider(DbContextOptions<AppDbContext> options, IMediator mediator)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAppDbContext>(_ => new AppDbContext(options, Substitute.For<IDomainEventDispatcher>()));
        services.AddScoped<IMediator>(_ => mediator);
        services.AddScoped<OutboxBackgroundJob>();
        return services.BuildServiceProvider();
    }

    private static DbContextOptions<AppDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task OutboxJob_DispatchesPendingEvents_AndMarksProcessed()
    {
        // Arrange
        var options = CreateOptions();
        await using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        var mediator = Substitute.For<IMediator>();

        var message = new OutboxMessage
        {
            EventType = "OrderHandoverCompleted",
            AggregateType = "OrderHandover",
            AggregateId = Guid.NewGuid().ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            DeduplicationKey = Guid.NewGuid().ToString(),
            PayloadJson = "{\"HandoverId\":\"111\"}",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();

        var sp = BuildServiceProvider(options, mediator);
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var job = new OutboxBackgroundJob(scopeFactory);

        // Act
        var processedCount = await job.ProcessPendingMessagesAsync(CancellationToken.None);

        // Assert
        processedCount.Should().Be(1);
        await mediator.Received(1).Publish(Arg.Is<OutboxLifecycleNotification>(n => n.EventType == "OrderHandoverCompleted"), Arg.Any<CancellationToken>());

        await using var verifyDb = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        var reloaded = await verifyDb.OutboxMessages.FindAsync(message.Id);
        reloaded.Should().NotBeNull();
        reloaded!.ProcessedAtUtc.Should().NotBeNull();
        reloaded.LastError.Should().BeNull();
    }

    [Fact]
    public async Task OutboxJob_RoutesCustomerDeliveryToTypedNotification_WithoutResolvingGenericHandlers()
    {
        var options = CreateOptions();
        await using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        var mediator = Substitute.For<IMediator>();
        var message = new OutboxMessage
        {
            EventType = CustomerDeliveryChangedV1.OutboxEventType,
            PayloadJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
        };
        var dayId = Guid.NewGuid();
        message.PayloadJson = System.Text.Json.JsonSerializer.Serialize(
            new CustomerDeliveryChangedV1(message.Id, dayId, DateTime.UtcNow));
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();

        var sp = BuildServiceProvider(options, mediator);
        var job = new OutboxBackgroundJob(sp.GetRequiredService<IServiceScopeFactory>());

        var processedCount = await job.ProcessPendingMessagesAsync(CancellationToken.None);

        processedCount.Should().Be(1);
        await mediator.Received(1).Publish(
            Arg.Is<CustomerDeliveryChangedOutboxNotification>(notification =>
                notification.Message.MessageId == message.Id
                && notification.Message.EventType == CustomerDeliveryChangedV1.OutboxEventType),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().Publish(
            Arg.Any<OutboxLifecycleNotification>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutboxJob_WhenHandlerThrows_SchedulesRetryWithBackoff()
    {
        // Arrange
        var options = CreateOptions();
        await using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<OutboxLifecycleNotification>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("SignalR broadcast network timeout"));

        var message = new OutboxMessage
        {
            EventType = "OrderHandoverCompleted",
            PayloadJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();

        var sp = BuildServiceProvider(options, mediator);
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var job = new OutboxBackgroundJob(scopeFactory);

        // Act
        var processedCount = await job.ProcessPendingMessagesAsync(CancellationToken.None);

        // Assert
        processedCount.Should().Be(0);

        await using var verifyDb = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        var reloaded = await verifyDb.OutboxMessages.FindAsync(message.Id);
        reloaded.Should().NotBeNull();
        reloaded!.ProcessedAtUtc.Should().BeNull();
        reloaded.RetryCount.Should().Be(1);
        reloaded.NextAttemptAtUtc.Should().BeAfter(DateTime.UtcNow.AddSeconds(-1));
        reloaded.PoisonedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task OutboxJob_WhenMaxRetriesExceeded_QuarantinesAsPoison()
    {
        // Arrange
        var options = CreateOptions();
        await using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        var mediator = Substitute.For<IMediator>();
        mediator.Publish(Arg.Any<OutboxLifecycleNotification>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Permanent fatal failure"));

        var message = new OutboxMessage
        {
            EventType = "OrderHandoverCompleted",
            PayloadJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 4 // next failure reaches 5
        };
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();

        var sp = BuildServiceProvider(options, mediator);
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var job = new OutboxBackgroundJob(scopeFactory);

        // Act
        var processedCount = await job.ProcessPendingMessagesAsync(CancellationToken.None);

        // Assert
        processedCount.Should().Be(0);

        await using var verifyDb = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        var reloaded = await verifyDb.OutboxMessages.FindAsync(message.Id);
        reloaded.Should().NotBeNull();
        reloaded!.ProcessedAtUtc.Should().BeNull();
        reloaded.RetryCount.Should().Be(5);
        reloaded.PoisonedAtUtc.Should().NotBeNull("Message must be quarantined as poison after 5 failures");
    }
}
