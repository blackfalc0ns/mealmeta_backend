using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class DispatcherQueueBridgeTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task Handle_ReturnsUnassignedSubscriptionDays_WhenTripsDoNotExist()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم الاختبار",
            TradeNameEn = "Test Restaurant",
            LegalNameAr = "شركة الاختبار",
            LegalNameEn = "Test Company"
        };
        db.RestaurantProfiles.Add(restaurant);

        var day = new SubscriptionDay
        {
            RestaurantId = restaurant.Id,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DeliveryAddressLine = "منطقة حولي قطعة 3"
        };
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);
        var messages = NullLocalizedMessageProvider.Instance;
        var recommendationEngine = Substitute.For<IDispatchRecommendationEngine>();

        var handler = new GetDispatcherOrderQueueQueryHandler(db, currentUser, messages, recommendationEngine);

        // Act
        var result = await handler.Handle(new GetDispatcherOrderQueueQuery(restaurant.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var queue = result.Value;
        queue.Counts.PendingCount.Should().Be(1);
        queue.Counts.TotalCount.Should().Be(1);
        queue.Boxes.Should().HaveCount(1);

        var box = queue.Boxes[0];
        box.BoxId.Should().Be(day.Id);
        box.BoxCode.Should().StartWith("#BX-");
        box.Status.Should().Be("Pending");
        box.ZoneName.Should().Contain("حولي");
    }

    [Fact]
    public async Task Handle_CombinesTripStopsAndUnassignedDays()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم الاختبار",
            TradeNameEn = "Test Restaurant",
            LegalNameAr = "شركة الاختبار",
            LegalNameEn = "Test Company"
        };
        db.RestaurantProfiles.Add(restaurant);

        // Trip with 1 stop
        var trip = new Trip
        {
            RestaurantId = restaurant.Id,
            Code = "TRIP-001",
            Status = TripStatus.Planned
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "ORD-1234",
            CustomerMaskedId = "CUST-001",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة صحية",
            MealSummaryEn = "Healthy Meal",
            SlotLabel = "09:30-10:30 ص",
            BoxCount = 1,
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);

        // Unassigned SubscriptionDay
        var day = new SubscriptionDay
        {
            RestaurantId = restaurant.Id,
            Status = SubscriptionDayStatus.Preparing,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DeliveryAddressLine = "منطقة الجابرية"
        };
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);
        var messages = NullLocalizedMessageProvider.Instance;
        var recommendationEngine = Substitute.For<IDispatchRecommendationEngine>();

        var handler = new GetDispatcherOrderQueueQueryHandler(db, currentUser, messages, recommendationEngine);

        // Act
        var result = await handler.Handle(new GetDispatcherOrderQueueQuery(restaurant.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var queue = result.Value;
        queue.Counts.PendingCount.Should().Be(2); // 1 trip stop + 1 unassigned day
        queue.Boxes.Should().HaveCount(2);

        var unassignedBox = queue.Boxes.FirstOrDefault(b => b.BoxId == day.Id);
        unassignedBox.Should().NotBeNull();
        unassignedBox!.Priority.Should().Be("Urgent");
    }
}
