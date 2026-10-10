using FluentAssertions;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

/// <summary>
/// Pure resolver contract: explicit cases decide every delivery status; real timestamps
/// pass through and unknown legacy times stay null; ambiguous attempts never get a box;
/// terminal delivered days never rewind.
/// </summary>
public sealed class CustomerDeliveryStatusResolverTests
{
    private static readonly DateTime T1 = new(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T3 = new(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime T4 = new(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);

    private static CustomerDeliveryStatusInput Input(
        SubscriptionDayStatus dayStatus = SubscriptionDayStatus.Preparing,
        OrderStatus? orderStatus = null,
        DateTime? readyAt = null,
        DateTime? pickedUpAt = null,
        CustomerDeliveryAttemptInput? attempt = null,
        bool ambiguous = false,
        string? ambiguityReason = null)
        => new(dayStatus, orderStatus, readyAt, pickedUpAt, attempt, ambiguous, ambiguityReason);

    private static CustomerDeliveryAttemptInput Attempt(
        TripStopStatus status,
        DateTime? arrivedAt = null,
        DateTime? deliveredAt = null,
        int sequence = 1)
        => new(Guid.NewGuid(), Guid.NewGuid(), sequence, status, arrivedAt, deliveredAt);

    [Fact]
    public void Ready_without_stop_is_ready_and_not_out_for_delivery()
    {
        var input = Input(
            dayStatus: SubscriptionDayStatus.Preparing,
            orderStatus: OrderStatus.ReadyForPickup,
            readyAt: T1);

        var result = CustomerDeliveryStatusResolver.Resolve(input);

        result.DeliveryStatus.Should().Be("ReadyForPickup");
        result.ReadyAtUtc.Should().Be(T1);
        result.PickedUpAtUtc.Should().BeNull();
        result.IsTrackable.Should().BeFalse();
        result.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Order_assigned_but_not_picked_up_stays_ready_for_pickup()
    {
        var result = CustomerDeliveryStatusResolver.Resolve(Input(
            dayStatus: SubscriptionDayStatus.Preparing,
            orderStatus: OrderStatus.AssignedToDriver,
            readyAt: T1,
            attempt: Attempt(TripStopStatus.Pending)));

        result.DeliveryStatus.Should().Be("ReadyForPickup");
        result.UnavailableReasonCode.Should().Be(CustomerDeliveryUnavailableReasons.DriverNotAssigned);
        result.IsTrackable.Should().BeFalse();
    }

    [Fact]
    public void Order_preparing_without_readiness_is_preparing()
    {
        var result = CustomerDeliveryStatusResolver.Resolve(Input(
            dayStatus: SubscriptionDayStatus.Preparing,
            orderStatus: OrderStatus.Preparing));

        result.DeliveryStatus.Should().Be("Preparing");
        result.ReadyAtUtc.Should().BeNull();
        result.IsTrackable.Should().BeFalse();
    }

    [Fact]
    public void Valid_day_without_operational_preparation_is_not_started()
    {
        var result = CustomerDeliveryStatusResolver.Resolve(Input(dayStatus: SubscriptionDayStatus.SentToRestaurant));

        result.DeliveryStatus.Should().Be("NotStarted");
        result.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Picked_up_is_out_for_delivery_and_trackable_with_handover_time()
    {
        var result = CustomerDeliveryStatusResolver.Resolve(Input(
            dayStatus: SubscriptionDayStatus.OutForDelivery,
            orderStatus: OrderStatus.OnTheWay,
            readyAt: T1,
            pickedUpAt: T2,
            attempt: Attempt(TripStopStatus.PickedUp)));

        result.DeliveryStatus.Should().Be("OutForDelivery");
        result.PickedUpAtUtc.Should().Be(T2);
        result.IsTrackable.Should().BeTrue();
        result.IsTerminal.Should().BeFalse();
        result.DeliveredAtUtc.Should().BeNull();
    }

    [Fact]
    public void Legacy_pickup_without_handover_time_stays_null_not_invented()
    {
        var result = CustomerDeliveryStatusResolver.Resolve(Input(
            dayStatus: SubscriptionDayStatus.OutForDelivery,
            orderStatus: OrderStatus.OnTheWay,
            readyAt: T1,
            pickedUpAt: null, // revoked or missing legacy handover
            attempt: Attempt(TripStopStatus.PickedUp)));

        result.DeliveryStatus.Should().Be("OutForDelivery");
        result.PickedUpAtUtc.Should().BeNull();
    }

    [Fact]
    public void Arrival_is_not_delivery_and_status_stays_out_for_delivery()
    {
        var result = CustomerDeliveryStatusResolver.Resolve(Input(
            dayStatus: SubscriptionDayStatus.OutForDelivery,
            orderStatus: OrderStatus.OnTheWay,
            readyAt: T1,
            pickedUpAt: T2,
            attempt: Attempt(TripStopStatus.PickedUp, arrivedAt: T3)));

        result.DeliveryStatus.Should().Be("OutForDelivery");
        result.ArrivedAtUtc.Should().Be(T3);
        result.DeliveredAtUtc.Should().BeNull();
        result.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Delivered_attempt_is_delivered_with_proof_timestamp()
    {
        var result = CustomerDeliveryStatusResolver.Resolve(Input(
            dayStatus: SubscriptionDayStatus.Delivered,
            orderStatus: OrderStatus.Delivered,
            readyAt: T1,
            pickedUpAt: T2,
            attempt: Attempt(TripStopStatus.Delivered, arrivedAt: T3, deliveredAt: T4)));

        result.DeliveryStatus.Should().Be("Delivered");
        result.IsTerminal.Should().BeTrue();
        result.IsTrackable.Should().BeFalse();
        result.DeliveredAtUtc.Should().Be(T4);
        result.ArrivedAtUtc.Should().Be(T3);
        result.InconsistencyReason.Should().BeNull();
    }

    [Fact]
    public void Delivered_day_is_never_rewound_by_a_stale_active_attempt()
    {
        var result = CustomerDeliveryStatusResolver.Resolve(Input(
            dayStatus: SubscriptionDayStatus.Delivered,
            orderStatus: OrderStatus.Delivered,
            readyAt: T1,
            pickedUpAt: T2,
            attempt: Attempt(TripStopStatus.PickedUp, arrivedAt: T3)));

        result.DeliveryStatus.Should().Be("Delivered");
        result.IsTerminal.Should().BeTrue();
        result.InconsistencyReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Failed_attempt_is_failed_and_never_completes_the_success_timeline()
    {
        var result = CustomerDeliveryStatusResolver.Resolve(Input(
            dayStatus: SubscriptionDayStatus.Failed,
            orderStatus: OrderStatus.DeliveryFailed,
            readyAt: T1,
            pickedUpAt: T2,
            attempt: Attempt(TripStopStatus.Failed)));

        result.DeliveryStatus.Should().Be("Failed");
        result.IsTerminal.Should().BeTrue();
        result.DeliveredAtUtc.Should().BeNull();
    }

    [Fact]
    public void Returned_to_kitchen_is_its_own_outcome()
    {
        var result = CustomerDeliveryStatusResolver.Resolve(Input(
            dayStatus: SubscriptionDayStatus.Preparing,
            orderStatus: OrderStatus.ReturnedToKitchen,
            readyAt: T1,
            attempt: Attempt(TripStopStatus.ReturnedToKitchen)));

        result.DeliveryStatus.Should().Be("ReturnedToKitchen");
        result.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Frozen_and_cancelled_days_are_terminal_regardless_of_attempt()
    {
        CustomerDeliveryStatusResolver.Resolve(Input(
                dayStatus: SubscriptionDayStatus.Frozen,
                attempt: Attempt(TripStopStatus.PickedUp)))
            .DeliveryStatus.Should().Be("Frozen");

        CustomerDeliveryStatusResolver.Resolve(Input(
                dayStatus: SubscriptionDayStatus.Cancelled,
                attempt: Attempt(TripStopStatus.PickedUp)))
            .DeliveryStatus.Should().Be("Cancelled");
    }

    [Fact]
    public void Ambiguous_attempts_yield_tracking_unavailable_without_timestamps()
    {
        var result = CustomerDeliveryStatusResolver.Resolve(Input(
            dayStatus: SubscriptionDayStatus.OutForDelivery,
            orderStatus: OrderStatus.OnTheWay,
            readyAt: T1,
            pickedUpAt: T2,
            attempt: Attempt(TripStopStatus.PickedUp),
            ambiguous: true,
            ambiguityReason: "multiple_active_stops:2"));

        result.DeliveryStatus.Should().Be("tracking_unavailable");
        result.IsTrackable.Should().BeFalse();
        result.PickedUpAtUtc.Should().BeNull();
        result.ReadyAtUtc.Should().BeNull();
        result.InconsistencyReason.Should().Be("multiple_active_stops:2");
        result.StatusTextAr.Should().NotBeNullOrWhiteSpace();
        result.StatusTextEn.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Both_language_texts_are_always_provided()
    {
        foreach (var status in new[]
                 {
                     Input(dayStatus: SubscriptionDayStatus.Preparing, orderStatus: OrderStatus.Preparing),
                     Input(dayStatus: SubscriptionDayStatus.Preparing, orderStatus: OrderStatus.ReadyForPickup, readyAt: T1),
                     Input(dayStatus: SubscriptionDayStatus.OutForDelivery, attempt: Attempt(TripStopStatus.PickedUp)),
                     Input(dayStatus: SubscriptionDayStatus.Delivered, orderStatus: OrderStatus.Delivered),
                     Input(dayStatus: SubscriptionDayStatus.Failed),
                     Input(dayStatus: SubscriptionDayStatus.Frozen),
                     Input(dayStatus: SubscriptionDayStatus.Cancelled),
                     Input(dayStatus: SubscriptionDayStatus.Open)
                 })
        {
            var snapshot = CustomerDeliveryStatusResolver.Resolve(status);
            snapshot.StatusTextAr.Should().NotBeNullOrWhiteSpace();
            snapshot.StatusTextEn.Should().NotBeNullOrWhiteSpace();
        }
    }
}
