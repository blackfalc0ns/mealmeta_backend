using FluentAssertions;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Xunit;

namespace MealMate.UnitTests.Domain;

public class OrderLifecycleTransitionMatrixTests
{
    // ==========================================
    // 1. Subscription State Machine Tests
    // ==========================================
    [Theory]
    [InlineData(SubscriptionStatus.Draft, SubscriptionStatus.PendingPayment)]
    [InlineData(SubscriptionStatus.PendingPayment, SubscriptionStatus.Paid)]
    [InlineData(SubscriptionStatus.PendingPayment, SubscriptionStatus.PaymentFailed)]
    [InlineData(SubscriptionStatus.Paid, SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Paid, SubscriptionStatus.Scheduled)]
    [InlineData(SubscriptionStatus.Paid, SubscriptionStatus.SuspendedForReview)]
    [InlineData(SubscriptionStatus.Scheduled, SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Scheduled, SubscriptionStatus.Frozen)]
    [InlineData(SubscriptionStatus.Scheduled, SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Active, SubscriptionStatus.Frozen)]
    [InlineData(SubscriptionStatus.Active, SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Active, SubscriptionStatus.Completed)]
    [InlineData(SubscriptionStatus.Frozen, SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Frozen, SubscriptionStatus.Cancelled)]
    public void Subscription_ValidTransitions_ReturnSuccess(SubscriptionStatus from, SubscriptionStatus to)
    {
        var entity = new Subscription { Status = from };
        entity.CanTransitionTo(to).Should().BeTrue();

        var result = entity.TransitionTo(to);
        result.IsSuccess.Should().BeTrue();
        entity.Status.Should().Be(to);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Draft, SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Draft, SubscriptionStatus.Completed)]
    [InlineData(SubscriptionStatus.Completed, SubscriptionStatus.Draft)]
    [InlineData(SubscriptionStatus.Cancelled, SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Cancelled, SubscriptionStatus.Paid)]
    public void Subscription_InvalidTransitions_ReturnFailure_AndPreserveStatus(SubscriptionStatus from, SubscriptionStatus to)
    {
        var entity = new Subscription { Status = from };
        entity.CanTransitionTo(to).Should().BeFalse();

        var result = entity.TransitionTo(to);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().StartWith("INVALID_SUBSCRIPTION_TRANSITION");
        entity.Status.Should().Be(from);
    }

    // ==========================================
    // 2. CapacityHold State Machine Tests
    // ==========================================
    [Theory]
    [InlineData(CapacityHoldStatus.PendingPayment, CapacityHoldStatus.PaymentConfirmed)]
    [InlineData(CapacityHoldStatus.PendingPayment, CapacityHoldStatus.Expired)]
    [InlineData(CapacityHoldStatus.PendingPayment, CapacityHoldStatus.Released)]
    [InlineData(CapacityHoldStatus.PaymentConfirmed, CapacityHoldStatus.Confirmed)]
    [InlineData(CapacityHoldStatus.PaymentConfirmed, CapacityHoldStatus.Released)]
    [InlineData(CapacityHoldStatus.Confirmed, CapacityHoldStatus.Released)]
    public void CapacityHold_ValidTransitions_ReturnSuccess(CapacityHoldStatus from, CapacityHoldStatus to)
    {
        var entity = new CapacityHold { Status = from };
        entity.CanTransitionTo(to).Should().BeTrue();

        var result = entity.TransitionTo(to);
        result.IsSuccess.Should().BeTrue();
        entity.Status.Should().Be(to);
    }

    [Theory]
    [InlineData(CapacityHoldStatus.Confirmed, CapacityHoldStatus.PendingPayment)]
    [InlineData(CapacityHoldStatus.Released, CapacityHoldStatus.Confirmed)]
    [InlineData(CapacityHoldStatus.Expired, CapacityHoldStatus.Confirmed)]
    [InlineData(CapacityHoldStatus.Expired, CapacityHoldStatus.PaymentConfirmed)]
    public void CapacityHold_InvalidTransitions_ReturnFailure_AndPreserveStatus(CapacityHoldStatus from, CapacityHoldStatus to)
    {
        var entity = new CapacityHold { Status = from };
        entity.CanTransitionTo(to).Should().BeFalse();

        var result = entity.TransitionTo(to);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().StartWith("INVALID_CAPACITY_HOLD_TRANSITION");
        entity.Status.Should().Be(from);
    }

    // ==========================================
    // 3. RestaurantDailyOrder State Machine Tests
    // ==========================================
    [Theory]
    [InlineData(OrderStatus.Created, OrderStatus.AwaitingRestaurantConfirmation)]
    [InlineData(OrderStatus.AwaitingRestaurantConfirmation, OrderStatus.RestaurantConfirmed)]
    [InlineData(OrderStatus.AwaitingRestaurantConfirmation, OrderStatus.RestaurantRejected)]
    [InlineData(OrderStatus.AwaitingRestaurantConfirmation, OrderStatus.ReplacementPending)]
    [InlineData(OrderStatus.RestaurantConfirmed, OrderStatus.Preparing)]
    [InlineData(OrderStatus.Preparing, OrderStatus.ReadyForPickup)]
    [InlineData(OrderStatus.ReadyForPickup, OrderStatus.AssignedToDriver)]
    [InlineData(OrderStatus.AssignedToDriver, OrderStatus.OnTheWay)]
    [InlineData(OrderStatus.OnTheWay, OrderStatus.Delivered)]
    [InlineData(OrderStatus.OnTheWay, OrderStatus.DeliveryFailed)]
    [InlineData(OrderStatus.DeliveryFailed, OrderStatus.SecondAttemptScheduled)]
    [InlineData(OrderStatus.DeliveryFailed, OrderStatus.ReturnedToKitchen)]
    public void RestaurantDailyOrder_ValidTransitions_ReturnSuccess(OrderStatus from, OrderStatus to)
    {
        var entity = new RestaurantDailyOrder { Status = from };
        entity.CanTransitionTo(to).Should().BeTrue();

        var result = entity.TransitionTo(to);
        result.IsSuccess.Should().BeTrue();
        entity.Status.Should().Be(to);
    }

    [Theory]
    [InlineData(OrderStatus.Created, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Created)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Preparing)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.OnTheWay)]
    public void RestaurantDailyOrder_InvalidTransitions_ReturnFailure_AndPreserveStatus(OrderStatus from, OrderStatus to)
    {
        var entity = new RestaurantDailyOrder { Status = from };
        entity.CanTransitionTo(to).Should().BeFalse();

        var result = entity.TransitionTo(to);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().StartWith("INVALID_ORDER_TRANSITION");
        entity.Status.Should().Be(from);
    }

    // ==========================================
    // 4. Trip State Machine Tests
    // ==========================================
    [Theory]
    [InlineData(TripStatus.Draft, TripStatus.Planned)]
    [InlineData(TripStatus.Planned, TripStatus.Assigned)]
    [InlineData(TripStatus.Assigned, TripStatus.InProgress)]
    [InlineData(TripStatus.InProgress, TripStatus.Completed)]
    [InlineData(TripStatus.Draft, TripStatus.Cancelled)]
    [InlineData(TripStatus.Planned, TripStatus.Cancelled)]
    [InlineData(TripStatus.Assigned, TripStatus.Cancelled)]
    [InlineData(TripStatus.InProgress, TripStatus.Cancelled)]
    public void Trip_ValidTransitions_ReturnSuccess(TripStatus from, TripStatus to)
    {
        var entity = new Trip { Status = from };
        entity.CanTransitionTo(to).Should().BeTrue();

        var result = entity.TransitionTo(to);
        result.IsSuccess.Should().BeTrue();
        entity.Status.Should().Be(to);
    }

    [Theory]
    [InlineData(TripStatus.Draft, TripStatus.Completed)]
    [InlineData(TripStatus.Draft, TripStatus.InProgress)]
    [InlineData(TripStatus.Completed, TripStatus.Draft)]
    [InlineData(TripStatus.Completed, TripStatus.InProgress)]
    [InlineData(TripStatus.Cancelled, TripStatus.InProgress)]
    public void Trip_InvalidTransitions_ReturnFailure_AndPreserveStatus(TripStatus from, TripStatus to)
    {
        var entity = new Trip { Status = from };
        entity.CanTransitionTo(to).Should().BeFalse();

        var result = entity.TransitionTo(to);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().StartWith("INVALID_TRIP_TRANSITION");
        entity.Status.Should().Be(from);
    }

    // ==========================================
    // 5. TripStop State Machine Tests
    // ==========================================
    [Theory]
    [InlineData(TripStopStatus.Pending, TripStopStatus.PickedUp)]
    [InlineData(TripStopStatus.Pending, TripStopStatus.Failed)]
    [InlineData(TripStopStatus.PickedUp, TripStopStatus.Delivered)]
    [InlineData(TripStopStatus.PickedUp, TripStopStatus.Failed)]
    [InlineData(TripStopStatus.Failed, TripStopStatus.ReturnedToKitchen)]
    public void TripStop_ValidTransitions_ReturnSuccess(TripStopStatus from, TripStopStatus to)
    {
        var entity = new TripStop { Status = from };
        entity.CanTransitionTo(to).Should().BeTrue();

        var result = entity.TransitionTo(to);
        result.IsSuccess.Should().BeTrue();
        entity.Status.Should().Be(to);
    }

    [Theory]
    [InlineData(TripStopStatus.Pending, TripStopStatus.Delivered)]
    [InlineData(TripStopStatus.Pending, TripStopStatus.ReturnedToKitchen)]
    [InlineData(TripStopStatus.Delivered, TripStopStatus.Pending)]
    [InlineData(TripStopStatus.Delivered, TripStopStatus.PickedUp)]
    [InlineData(TripStopStatus.ReturnedToKitchen, TripStopStatus.Pending)]
    public void TripStop_InvalidTransitions_ReturnFailure_AndPreserveStatus(TripStopStatus from, TripStopStatus to)
    {
        var entity = new TripStop { Status = from };
        entity.CanTransitionTo(to).Should().BeFalse();

        var result = entity.TransitionTo(to);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().StartWith("INVALID_TRIP_STOP_TRANSITION");
        entity.Status.Should().Be(from);
    }

    // ==========================================
    // 6. MealBarcode State Machine Tests
    // ==========================================
    [Theory]
    [InlineData(MealBarcodeStatus.Generated, MealBarcodeStatus.Printed)]
    [InlineData(MealBarcodeStatus.Generated, MealBarcodeStatus.Revoked)]
    [InlineData(MealBarcodeStatus.Printed, MealBarcodeStatus.Scanned)]
    [InlineData(MealBarcodeStatus.Printed, MealBarcodeStatus.Revoked)]
    [InlineData(MealBarcodeStatus.Scanned, MealBarcodeStatus.Archived)]
    public void MealBarcode_ValidTransitions_ReturnSuccess(MealBarcodeStatus from, MealBarcodeStatus to)
    {
        var entity = new MealBarcode { Status = from };
        entity.CanTransitionTo(to).Should().BeTrue();

        var result = entity.TransitionTo(to);
        result.IsSuccess.Should().BeTrue();
        entity.Status.Should().Be(to);
    }

    [Theory]
    [InlineData(MealBarcodeStatus.Generated, MealBarcodeStatus.Scanned)]
    [InlineData(MealBarcodeStatus.Generated, MealBarcodeStatus.Archived)]
    [InlineData(MealBarcodeStatus.Scanned, MealBarcodeStatus.Generated)]
    [InlineData(MealBarcodeStatus.Scanned, MealBarcodeStatus.Revoked)]
    [InlineData(MealBarcodeStatus.Revoked, MealBarcodeStatus.Generated)]
    [InlineData(MealBarcodeStatus.Revoked, MealBarcodeStatus.Printed)]
    [InlineData(MealBarcodeStatus.Archived, MealBarcodeStatus.Generated)]
    public void MealBarcode_InvalidTransitions_ReturnFailure_AndPreserveStatus(MealBarcodeStatus from, MealBarcodeStatus to)
    {
        var entity = new MealBarcode { Status = from };
        entity.CanTransitionTo(to).Should().BeFalse();

        var result = entity.TransitionTo(to);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().StartWith("INVALID_BARCODE_TRANSITION");
        entity.Status.Should().Be(from);
    }
}
