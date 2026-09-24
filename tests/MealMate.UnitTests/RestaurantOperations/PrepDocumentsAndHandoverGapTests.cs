using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.PrepDocuments;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOperations;

public class PrepDocumentsAndHandoverGapTests
{
    private readonly Persistence.AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IPrepDocumentsGenerator _generator;
    private readonly RestaurantOrderService _orderService;
    private Guid _testRestaurantId;
    private readonly DateTime _baseNow = new(2026, 8, 25, 9, 0, 0, DateTimeKind.Utc);
    private readonly DateOnly _today = new(2026, 8, 25);

    public PrepDocumentsAndHandoverGapTests()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "مطعم الاختبار",
            LegalNameEn = "Test Restaurant",
            TradeNameAr = "مطعم الاختبار",
            TradeNameEn = "Test Restaurant",
            ContactPhone = "+96512345678"
        };
        _db.RestaurantProfiles.Add(restaurant);
        _db.SaveChanges();
        _testRestaurantId = restaurant.Id;

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.RestaurantId.Returns(_testRestaurantId);
        _currentUser.UserId.Returns(restaurant.UserId);
        _currentUser.UserType.Returns(UserType.Restaurant);

        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(_baseNow);
        _clock.TodayUtc.Returns(_today);

        var config = new ConfigurationBuilder().Build();
        _generator = new PrepDocumentsGenerator(_db, _clock, config, NullLogger<PrepDocumentsGenerator>.Instance);

        _orderService = new RestaurantOrderService(
            _db,
            _currentUser,
            _clock,
            prepDocumentsGenerator: _generator);
    }

    private async Task<(Guid DayId, SubscriptionDay Day)> SeedDayAsync(DateOnly date, SubscriptionDayStatus status = SubscriptionDayStatus.ConfirmedByRestaurant)
    {
        var program = new Program { Code = "PRG-FIT", NameAr = "لياقة", NameEn = "Fitness" };
        var bundle = new Bundle { Code = "BND-FULL", NameAr = "كاملة", NameEn = "Full", ProgramId = program.Id };
        _db.Programs.Add(program);
        _db.Bundles.Add(bundle);

        var subscription = new Subscription
        {
            ProgramId = program.Id,
            BundleId = bundle.Id,
            CustomerUserId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Platinum
        };
        _db.Subscriptions.Add(subscription);

        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = date,
            Status = status,
            LockAtUtc = _baseNow.AddHours(-12)
        };
        _db.SubscriptionDays.Add(day);

        var meal = new Meal
        {
            Code = "MEL-001",
            RestaurantId = _testRestaurantId,
            NameAr = "وجبة صحية",
            NameEn = "Healthy Meal",
            Slot = "Lunch",
            Calories = 500,
            ProteinG = 40,
            CarbsG = 45,
            FatG = 12
        };
        _db.Meals.Add(meal);

        _db.DailyMealSelections.Add(new DailyMealSelection
        {
            SubscriptionDayId = day.Id,
            MealId = meal.Id,
            SortOrder = 1
        });

        await _db.SaveChangesAsync();
        return (day.Id, day);
    }

    [Fact]
    public async Task GetPrepDocumentsAsync_WhenDocumentsDoNotExist_GeneratesDocumentsJustInTime()
    {
        // Arrange: Seed day without generating barcodes or labels beforehand
        var (dayId, _) = await SeedDayAsync(_today);

        // Act: Request prep documents directly
        var result = await _orderService.GetPrepDocumentsAsync(new GetRestaurantPrepDocumentsQuery(dayId), CancellationToken.None);

        // Assert: Documents must be generated JIT and returned, not null
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.BarcodeValue.Should().NotBeNullOrWhiteSpace();
        result.Value.Invoice.Should().NotBeNull();
        result.Value.BoxLabel.Should().NotBeNull();
        result.Value.MealLabels.Should().NotBeEmpty();

        // Database must now have persisted records
        var hasBarcode = await _db.MealBarcodes.AnyAsync(x => x.SubscriptionDayId == dayId);
        var hasInvoice = await _db.MealInvoices.AnyAsync(x => x.SubscriptionDayId == dayId);
        hasBarcode.Should().BeTrue();
        hasInvoice.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateDailyOrderPrepStatus_WhenMarkedReady_EnsuresBarcodeAndLabelsExist()
    {
        // Arrange
        var (dayId, _) = await SeedDayAsync(_today);

        // Act: Kitchen staff marks order as ready
        var result = await _orderService.UpdateDailyOrderPrepStatusAsync(
            new UpdateDailyOrderPrepStatusCommand(dayId, "ready"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Barcode and box label must now be generated so driver can scan it
        var barcode = await _db.MealBarcodes.FirstOrDefaultAsync(x => x.SubscriptionDayId == dayId);
        barcode.Should().NotBeNull();
        barcode!.BarcodeValue.Should().NotBeNullOrWhiteSpace();

        var dailyOrder = await _db.RestaurantDailyOrders.FirstOrDefaultAsync(r => r.SubscriptionDayId == dayId);
        dailyOrder.Should().NotBeNull();
        dailyOrder!.Status.Should().Be(OrderStatus.ReadyForPickup);
    }

    [Fact]
    public async Task GetUpcoming24h_WhenTargetDateOmitted_ReturnsBothTodayAndTomorrowOrders()
    {
        // Arrange: One order today, one order tomorrow
        var (todayDayId, _) = await SeedDayAsync(_today);
        var (tomorrowDayId, _) = await SeedDayAsync(_today.AddDays(1));

        // Act: Query upcoming 24h with no targetDate (just like clicking from labels page)
        var result = await _orderService.GetUpcoming24hAsync(
            new GetRestaurantUpcoming24hQuery(TargetDate: null),
            CancellationToken.None);

        // Assert: Both today and tomorrow are present in the 24h operational window
        result.IsSuccess.Should().BeTrue();
        result.Value.Orders.Should().Contain(o => o.Id == todayDayId);
        result.Value.Orders.Should().Contain(o => o.Id == tomorrowDayId);
    }

    [Fact]
    public async Task DriverPickup_WhenScannedConditionPhoto_RecordsOrderHandover()
    {
        // Arrange: Setup driver, trip, stop, daily order
        var (dayId, _) = await SeedDayAsync(_today);

        var dailyOrder = new RestaurantDailyOrder
        {
            RestaurantId = _testRestaurantId,
            SubscriptionDayId = dayId,
            ServiceDate = _today,
            Status = OrderStatus.ReadyForPickup,
            BoxCount = 1,
            CreatedAtUtc = _baseNow
        };
        _db.RestaurantDailyOrders.Add(dailyOrder);

        var driverUserId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            UserId = driverUserId,
            FullNameAr = "سائق التوصيل",
            FullNameEn = "Delivery Driver"
        };
        _db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            Code = "TRIP-001",
            Status = TripStatus.Assigned,
            CreatedAtUtc = _baseNow
        };
        _db.Trips.Add(trip);

        var assignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active",
            AssignedAtUtc = _baseNow
        };
        _db.DriverAssignments.Add(assignment);

        var stop = new TripStop
        {
            TripId = trip.Id,
            DailyOrderId = dailyOrder.Id,
            SubscriptionDayId = dayId,
            OrderCode = "BX-001",
            Status = TripStopStatus.Pending,
            CustomerName = "Ahmed",
            CustomerMaskedId = "CUS-•••123",
            MealSummaryAr = "وجبة صحية",
            MealSummaryEn = "Healthy Meal",
            SlotLabel = "12:00 - 14:00",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            SequenceNumber = 1
        };
        _db.TripStops.Add(stop);

        // Barcode
        var barcode = new MealBarcode
        {
            SubscriptionDayId = dayId,
            BarcodeValue = "MM-20260825-001",
            GeneratedAtUtc = _baseNow,
            CreatedAtUtc = _baseNow
        };
        _db.MealBarcodes.Add(barcode);

        await _db.SaveChangesAsync();

        var driverCurrentUser = Substitute.For<ICurrentUser>();
        driverCurrentUser.UserId.Returns(driverUserId);

        var messages = NullLocalizedMessageProvider.Instance;

        var handler = new DriverDeliveryOrderCommandHandlers(
            _db,
            driverCurrentUser,
            messages);

        // Act: Driver completes pickup with condition photo
        var pickupResult = await handler.Handle(
            new PickupBoxCommand(
                BoxId: stop.Id,
                BarcodeValue: barcode.BarcodeValue,
                ConditionPhotoStorageKey: "photos/box1.jpg",
                DriverId: driver.Id),
            CancellationToken.None);

        // Assert: Pickup succeeded
        pickupResult.IsSuccess.Should().BeTrue();

        // AND OrderHandover row must be recorded in the database
        var handover = await _db.OrderHandovers
            .FirstOrDefaultAsync(h => h.DailyOrderId == dailyOrder.Id);

        handover.Should().NotBeNull();
        handover!.TripId.Should().Be(trip.Id);
        handover.DriverProfileId.Should().Be(driver.Id);
        handover.Status.Should().Be("Completed");
        handover.HandedOverAtUtc.Should().NotBeNull();
    }
}
