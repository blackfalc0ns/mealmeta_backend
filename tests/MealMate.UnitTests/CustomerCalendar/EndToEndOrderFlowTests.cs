using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCalendar;

public class EndToEndOrderFlowTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task CompleteLifecycle_FromCalendarLock_ToDriverDispatch_AndFallbackReplacement()
    {
        // -------------------------------------------------------------
        // Arrange Environment & Dependencies
        // -------------------------------------------------------------
        await using var db = CreateDb();
        var now = DateTime.UtcNow;

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var deliveryAddresses = Substitute.For<ICustomerDeliveryAddressService>();
        var restaurantNotifier = Substitute.For<IRestaurantOrderNotifier>();
        var customerPush = Substitute.For<ICustomerPushNotifier>();
        var driverPush = Substitute.For<IDriverPushNotifier>();
        var messages = NullLocalizedMessageProvider.Instance;
        var recommendationEngine = Substitute.For<IDispatchRecommendationEngine>();

        // Create Restaurants
        var primaryRest = new RestaurantProfile
        {
            TradeNameAr = "مطعم دايت بريميوم",
            TradeNameEn = "Diet Premium Restaurant",
            LegalNameAr = "شركة دايت بريميوم",
            LegalNameEn = "Diet Premium Co",
            IsOperationallyActive = true
        };
        var backupRest = new RestaurantProfile
        {
            TradeNameAr = "مطعم هيلثي لايف",
            TradeNameEn = "Healthy Life Restaurant",
            LegalNameAr = "شركة هيلثي لايف",
            LegalNameEn = "Healthy Life Co",
            IsOperationallyActive = true
        };
        db.RestaurantProfiles.AddRange(primaryRest, backupRest);

        // Create Driver
        var driver = new DriverProfile
        {
            FullNameAr = "خالد السالم",
            FullNameEn = "Khaled Al-Salem",
            VehiclePlate = "KW-98765",
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(driver);

        // Create Customer Subscription
        var customerId = Guid.NewGuid();
        var subscription = new Subscription
        {
            CustomerUserId = customerId,
            Status = SubscriptionStatus.Active,
            DurationDays = 30
        };
        db.Subscriptions.Add(subscription);

        // Snapshots for primary and backup restaurant
        db.SubscriptionRestaurantSnapshots.AddRange(
            new SubscriptionRestaurantSnapshot
            {
                SubscriptionId = subscription.Id,
                RestaurantId = primaryRest.Id,
                NameAr = primaryRest.TradeNameAr,
                NameEn = primaryRest.TradeNameEn,
                IsInitiallySelected = true
            },
            new SubscriptionRestaurantSnapshot
            {
                SubscriptionId = subscription.Id,
                RestaurantId = backupRest.Id,
                NameAr = backupRest.TradeNameAr,
                NameEn = backupRest.TradeNameEn,
                IsInitiallySelected = false
            }
        );

        // Customer SubscriptionDay
        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = primaryRest.Id,
            ServiceDate = DateOnly.FromDateTime(now.AddDays(3)),
            Status = SubscriptionDayStatus.CustomerSelected,
            DeliveryAddressLine = "السالمية شارع سالم المبارك"
        };
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();

        // -------------------------------------------------------------
        // Step 1: 72h Lock Dispatches Order & Auto-Provisions Daily Order
        // -------------------------------------------------------------
        var dispatchService = new SubscriptionDayDispatchService(
            db,
            clock,
            deliveryAddresses,
            restaurantNotifier,
            customerPush);

        await dispatchService.LockAndSendToRestaurantAsync(day, subscription.Id, CancellationToken.None);
        await db.SaveChangesAsync();

        day.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);

        var dailyOrder = await db.RestaurantDailyOrders
            .FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id);
        dailyOrder.Should().NotBeNull();
        dailyOrder!.RestaurantId.Should().Be(primaryRest.Id);
        dailyOrder.Status.Should().Be(OrderStatus.AwaitingRestaurantConfirmation);

        await restaurantNotifier.Received(1).NotifyOrderSentToRestaurantAsync(
            primaryRest.Id,
            day.Id,
            day.ServiceDate,
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        // -------------------------------------------------------------
        // Step 2: Kitchen Prepares & Sets Status to "ready"
        // -------------------------------------------------------------
        dailyOrder.TransitionTo(OrderStatus.RestaurantConfirmed);
        dailyOrder.TransitionTo(OrderStatus.Preparing);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(primaryRest.Id);

        // Ready-for-pickup is only valid after the real preparation documents exist.
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = day.Id,
            OrderId = dailyOrder.Id,
            BarcodeValue = $"MM-{day.Id:N}"[..23],
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = now,
            CreatedAtUtc = now
        });
        db.MealInvoices.Add(new MealInvoice
        {
            RestaurantId = primaryRest.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            InvoiceNumber = $"INV-{day.Id:N}"[..24],
            TotalBoxes = 1,
            CreatedAtUtc = now
        });
        db.MealLabels.Add(new MealLabel
        {
            SubscriptionDayId = day.Id,
            Kind = MealLabelKind.Box,
            LabelPayloadJson = "{}",
            GeneratedAtUtc = now,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync();

        var restaurantOrderService = new RestaurantOrderService(
            db,
            currentUser,
            clock,
            customerPush,
            restaurantNotifier);

        var prepResult = await restaurantOrderService.UpdateDailyOrderPrepStatusAsync(
            new UpdateDailyOrderPrepStatusCommand(day.Id, "ready"),
            CancellationToken.None);

        prepResult.IsSuccess.Should().BeTrue();
        dailyOrder.Status.Should().Be(OrderStatus.ReadyForPickup);

        await restaurantNotifier.Received(1).NotifyKitchenBatchReadyAsync(
            primaryRest.Id,
            day.Id,
            dailyOrder.BoxCount,
            Arg.Any<CancellationToken>());

        // -------------------------------------------------------------
        // Step 3: Dispatcher Order Queue Dynamically Bridges the Box
        // -------------------------------------------------------------
        var queueHandler = new GetDispatcherOrderQueueQueryHandler(
            db,
            currentUser,
            messages,
            recommendationEngine);

        var queueResult = await queueHandler.Handle(
            new GetDispatcherOrderQueueQuery(primaryRest.Id),
            CancellationToken.None);

        queueResult.IsSuccess.Should().BeTrue();
        queueResult.Value.Counts.PendingCount.Should().Be(1);
        queueResult.Value.Boxes.Should().HaveCount(1);

        var boxItem = queueResult.Value.Boxes[0];
        boxItem.BoxId.Should().Be(day.Id);
        boxItem.Priority.Should().Be("Urgent"); // Marked urgent because ready for pickup
        boxItem.Status.Should().Be("Pending");

        // -------------------------------------------------------------
        // Step 4: Dispatcher Assigns Box Directly to Driver
        // -------------------------------------------------------------
        var assignHandler = new AssignBoxToDriverCommandHandler(
            db,
            messages,
            driverPush);

        var assignResult = await assignHandler.Handle(
            new AssignBoxToDriverCommand(day.Id, driver.Id, "إسناد أولوية"),
            CancellationToken.None);

        assignResult.IsSuccess.Should().BeTrue();
        assignResult.Value.DriverId.Should().Be(driver.Id);

        // Verifies trip was provisioned and driver was assigned
        var createdTrip = await db.Trips
            .Include(t => t.Stops)
            .Include(t => t.DriverAssignments)
            .FirstOrDefaultAsync(t => t.RestaurantId == primaryRest.Id);

        createdTrip.Should().NotBeNull();
        createdTrip!.Status.Should().Be(TripStatus.Assigned);
        createdTrip.Stops.Should().ContainSingle(s => s.SubscriptionDayId == day.Id);
        createdTrip.DriverAssignments.Should().ContainSingle(da => da.DriverProfileId == driver.Id);

        dailyOrder.Status.Should().Be(OrderStatus.AssignedToDriver);

        // Verifies Outbox message was persisted for asynchronous driver push delivery
        var outboxMsg = await db.OutboxMessages.FirstOrDefaultAsync(m => m.EventType == "DriverBoxAssigned");
        outboxMsg.Should().NotBeNull();

        // -------------------------------------------------------------
        // Step 5: Automatic Re-routing / Fallback Replacement
        // -------------------------------------------------------------
        var orchestrator = new OrderReplacementOrchestrator(
            db,
            clock,
            restaurantNotifier,
            customerPush);

        var replacementResult = await orchestrator.AttemptAutoReplacementAsync(day.Id, CancellationToken.None);

        replacementResult.Status.Should().Be(ReplacementStatus.ReplacedWithAlternative);
        replacementResult.NewRestaurantId.Should().Be(backupRest.Id);

        day.RestaurantId.Should().Be(backupRest.Id);
        day.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        dailyOrder.RestaurantId.Should().Be(backupRest.Id);
        dailyOrder.Status.Should().Be(OrderStatus.AwaitingRestaurantConfirmation);

        await customerPush.Received(1).NotifyOrderReplacedAsync(
            customerId,
            day.Id,
            day.ServiceDate,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
