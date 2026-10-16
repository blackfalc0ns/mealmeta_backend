using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace MealMate.UnitTests.CustomerDelivery;

public sealed class CustomerDeliveryChangedOutboxHandlerTests
{
    [Fact]
    public async Task MatchingEvent_PublishesStableOutboxIdAndSubscriptionDay()
    {
        var transport = Substitute.For<ICustomerDeliveryChangeTransport>();
        var handler = new CustomerDeliveryChangedOutboxHandler(
            transport,
            Substitute.For<ILogger<CustomerDeliveryChangedOutboxHandler>>());
        var eventId = Guid.NewGuid();
        var dayId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 10, 16, 8, 30, 0, DateTimeKind.Utc);
        var payload = System.Text.Json.JsonSerializer.Serialize(new CustomerDeliveryChangedV1(
            eventId, dayId, occurredAtUtc));

        await handler.Handle(Notification(eventId, CustomerDeliveryChangedV1.OutboxEventType, payload), CancellationToken.None);

        await transport.Received(1).PublishAsync(
            Arg.Is<CustomerDeliveryChangedV1>(change => change.EventId == eventId
                && change.SubscriptionDayId == dayId
                && change.OccurredAtUtc == occurredAtUtc
                && change.SchemaVersion == 1),
            CancellationToken.None);
    }

    [Fact]
    public async Task NonMatchingEvent_IsIgnored()
    {
        var transport = Substitute.For<ICustomerDeliveryChangeTransport>();
        var handler = new CustomerDeliveryChangedOutboxHandler(
            transport,
            Substitute.For<ILogger<CustomerDeliveryChangedOutboxHandler>>());

        await handler.Handle(Notification(Guid.NewGuid(), "OtherEvent", "{}"), CancellationToken.None);

        await transport.DidNotReceive().PublishAsync(Arg.Any<CustomerDeliveryChangedV1>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnsupportedSchemaOrMismatchedMessageId_IsIgnored()
    {
        var transport = Substitute.For<ICustomerDeliveryChangeTransport>();
        var handler = new CustomerDeliveryChangedOutboxHandler(
            transport,
            Substitute.For<ILogger<CustomerDeliveryChangedOutboxHandler>>());
        var messageId = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.Serialize(new CustomerDeliveryChangedV1(
            Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, SchemaVersion: 2));

        await handler.Handle(Notification(messageId, CustomerDeliveryChangedV1.OutboxEventType, payload), CancellationToken.None);

        await transport.DidNotReceive().PublishAsync(Arg.Any<CustomerDeliveryChangedV1>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransportFailure_BubblesForOutboxRetry()
    {
        var transport = Substitute.For<ICustomerDeliveryChangeTransport>();
        var eventId = Guid.NewGuid();
        var dayId = Guid.NewGuid();
        transport.PublishAsync(Arg.Any<CustomerDeliveryChangedV1>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("transport unavailable"));
        var handler = new CustomerDeliveryChangedOutboxHandler(
            transport,
            Substitute.For<ILogger<CustomerDeliveryChangedOutboxHandler>>());
        var payload = System.Text.Json.JsonSerializer.Serialize(new CustomerDeliveryChangedV1(eventId, dayId, DateTime.UtcNow));

        var act = () => handler.Handle(Notification(eventId, CustomerDeliveryChangedV1.OutboxEventType, payload), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    private static CustomerDeliveryChangedOutboxNotification Notification(Guid messageId, string type, string payload)
        => new(new OutboxLifecycleNotification(messageId, type, null, null, null, null, payload, DateTime.UtcNow));
}
