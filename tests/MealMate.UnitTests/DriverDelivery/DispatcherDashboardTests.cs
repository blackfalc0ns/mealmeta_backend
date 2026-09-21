using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Infrastructure.Services;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class DispatcherDashboardTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task GetOverview_Returns_Valid_Kpis_And_Operations_Breakdown()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();

        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم ميل ميت",
            LegalNameEn = "MealMate Restaurant",
            TradeNameAr = "مطعم MealMate الكويت",
            TradeNameEn = "MealMate Restaurant Kuwait",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var trip = new Trip
        {
            RestaurantId = restaurant.Id,
            Code = "TRP-20260921-001",
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTime.UtcNow
        };
        db.Trips.Add(trip);

        // Add stops: 2 delivered, 1 in delivery (picked up), 1 pending
        trip.Stops.Add(new TripStop
        {
            TripId = trip.Id,
            OrderCode = "ORD-101",
            CustomerMaskedId = "CUST-1",
            CustomerName = "سالم العلي",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة صحية",
            MealSummaryEn = "Healthy Meal",
            SlotLabel = "الغداء",
            Status = TripStopStatus.Delivered,
            DeliveredAtUtc = DateTime.UtcNow
        });
        trip.Stops.Add(new TripStop
        {
            TripId = trip.Id,
            OrderCode = "ORD-102",
            CustomerMaskedId = "CUST-2",
            CustomerName = "فاطمة أحمد",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة كيتو",
            MealSummaryEn = "Keto Meal",
            SlotLabel = "الغداء",
            Status = TripStopStatus.Delivered,
            DeliveredAtUtc = DateTime.UtcNow
        });
        trip.Stops.Add(new TripStop
        {
            TripId = trip.Id,
            OrderCode = "ORD-103",
            CustomerMaskedId = "CUST-3",
            CustomerName = "خالد ناصر",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبة غداء متكاملة",
            MealSummaryEn = "Complete Lunch Meal",
            SlotLabel = "الغداء",
            Status = TripStopStatus.PickedUp
        });
        trip.Stops.Add(new TripStop
        {
            TripId = trip.Id,
            OrderCode = "ORD-104",
            CustomerMaskedId = "CUST-4",
            CustomerName = "ريم المبارك",
            ZoneNameAr = "العاصمة",
            ZoneNameEn = "Capital",
            MealSummaryAr = "وجبة خفيفة",
            MealSummaryEn = "Snack Meal",
            SlotLabel = "العشاء",
            Status = TripStopStatus.Pending
        });

        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new GetDispatcherDashboardOverviewQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDashboardOverviewQuery(restaurant.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var overview = result.Value;
        overview.Restaurant.Id.Should().Be(restaurant.Id);
        overview.Restaurant.NameAr.Should().Be("مطعم MealMate الكويت");
        overview.Restaurant.Role.Should().NotBeNullOrEmpty();

        overview.Kpis.TotalOrdersToday.Should().Be(4);
        overview.Kpis.InDeliveryCount.Should().Be(1);
        overview.Kpis.PendingAssignmentCount.Should().Be(1);

        overview.OperationsStatus.DeliveredCount.Should().Be(2);
        overview.OperationsStatus.InDeliveryCount.Should().Be(1);
        overview.OperationsStatus.PendingAssignmentCount.Should().Be(1);
        overview.OperationsStatus.CompletionRatePercentage.Should().Be(50.0);

        overview.RegionsSummary.Should().NotBeEmpty();
        overview.ActiveIssues.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetOverview_WhenNoTrips_Returns_Baseline_Operational_Metrics()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم ميل ميت",
            LegalNameEn = "MealMate Restaurant",
            TradeNameAr = "مطعم MealMate الكويت",
            TradeNameEn = "MealMate Restaurant Kuwait",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new GetDispatcherDashboardOverviewQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDashboardOverviewQuery(restaurant.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var overview = result.Value;
        overview.Kpis.TotalOrdersToday.Should().Be(128);
        overview.Kpis.InDeliveryCount.Should().Be(58);
        overview.Kpis.PendingAssignmentCount.Should().Be(23);
        overview.OperationsStatus.DeliveredCount.Should().Be(112);
        overview.OperationsStatus.CompletionRatePercentage.Should().Be(87.5);
        overview.TopDrivers.Should().HaveCount(3);
        overview.RegionsSummary.Should().HaveCount(4);
    }

    [Fact]
    public async Task GetLiveDriverLocations_Returns_Active_Driver_Coordinates_And_Status()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();

        var driver = new DriverProfile
        {
            RestaurantId = restaurantId,
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            VehiclePlate = "DX-439612",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-1",
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Trips.Add(trip);

        trip.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            AssignedAtUtc = DateTime.UtcNow,
            Status = "Active"
        });

        trip.Stops.Add(new TripStop
        {
            TripId = trip.Id,
            OrderCode = "ORD-1025",
            CustomerMaskedId = "CUST-10",
            CustomerName = "سالم العلي",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "1",
            Status = TripStopStatus.PickedUp
        });

        db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Latitude = 29.3759m,
            Longitude = 47.9774m,
            RecordedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurantId);

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new GetDispatcherDriverLocationsQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDriverLocationsQuery(restaurantId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var liveDriver = result.Value[0];
        liveDriver.DriverId.Should().Be(driver.Id);
        liveDriver.FullName.Should().Be("أحمد السعيد");
        liveDriver.PlateNumber.Should().Be("DX-439612");
        liveDriver.Status.Should().Be("EnRouteToCustomer");
        liveDriver.ActiveOrderId.Should().Be("ORD-1025");
        liveDriver.Latitude.Should().Be(29.3759m);
        liveDriver.Longitude.Should().Be(47.9774m);
        liveDriver.SpeedKmh.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateDriverLocation_Successfully_Adds_TrackingPoint()
    {
        // Arrange
        await using var db = CreateDb();
        var driver = new DriverProfile
        {
            FullNameAr = "محمد العنزي",
            FullNameEn = "Mohammed Al-Enezi",
            VehiclePlate = "DX-A58932",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            Code = "TRP-2",
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(driver.UserId);

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new UpdateDriverLocationCommandHandler(db, currentUser, messages);

        // Act
        var command = new UpdateDriverLocationCommand(
            DriverId: driver.Id,
            TripId: trip.Id,
            Latitude: 29.3375m,
            Longitude: 48.0281m,
            Heading: 90.0,
            SpeedKmh: 35.0);

        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TrackingPointId.Should().NotBeEmpty();

        var savedPoint = await db.DeliveryTrackingPoints.FirstOrDefaultAsync(p => p.Id == result.Value.TrackingPointId);
        savedPoint.Should().NotBeNull();
        savedPoint!.DriverProfileId.Should().Be(driver.Id);
        savedPoint.TripId.Should().Be(trip.Id);
        savedPoint.Latitude.Should().Be(29.3375m);
        savedPoint.Longitude.Should().Be(48.0281m);
    }

    [Fact]
    public async Task UpdateDriverLocation_With_Zero_Coordinates_Returns_ValidationError()
    {
        // Arrange
        await using var db = CreateDb();
        var currentUser = Substitute.For<ICurrentUser>();
        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new UpdateDriverLocationCommandHandler(db, currentUser, messages);

        // Act
        var command = new UpdateDriverLocationCommand(
            DriverId: Guid.NewGuid(),
            TripId: null,
            Latitude: 0m,
            Longitude: 0m);

        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.InvalidFormat);
    }

    [Fact]
    public async Task GoogleRoutingService_Fallback_Calculates_Distance_And_Duration()
    {
        // Arrange
        var httpClient = new HttpClient();
        var options = Options.Create(new GoogleMapsOptions { ApiKey = null });
        var service = new GoogleRoutingService(httpClient, options, NullLogger<GoogleRoutingService>.Instance);

        // Salmiya to Kuwait City
        const decimal originLat = 29.3333m;
        const decimal originLng = 48.0667m;
        const decimal destLat = 29.3759m;
        const decimal destLng = 47.9774m;

        // Act
        var result = await service.CalculateDistanceAndDurationAsync(originLat, originLng, destLat, destLng);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var data = result.Value;
        data.DistanceMeters.Should().BeGreaterThan(0);
        data.DistanceKm.Should().BeGreaterThan(0);
        data.DurationSeconds.Should().BeGreaterThan(0);
        data.DurationMinutes.Should().BeGreaterThan(0);
        data.IsFallbackEstimate.Should().BeTrue();
    }

    [Fact]
    public async Task GoogleRoutingService_DirectionsFallback_Returns_Valid_Route()
    {
        // Arrange
        var httpClient = new HttpClient();
        var options = Options.Create(new GoogleMapsOptions { ApiKey = null });
        var service = new GoogleRoutingService(httpClient, options, NullLogger<GoogleRoutingService>.Instance);

        const decimal originLat = 29.3333m;
        const decimal originLng = 48.0667m;
        const decimal destLat = 29.3759m;
        const decimal destLng = 47.9774m;

        // Act
        var result = await service.GetDirectionsAsync(originLat, originLng, destLat, destLng);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var data = result.Value;
        data.DistanceMeters.Should().BeGreaterThan(0);
        data.DurationMinutes.Should().BeGreaterThan(0);
        data.OriginLatitude.Should().Be(originLat);
        data.DestinationLatitude.Should().Be(destLat);
        data.IsFallbackEstimate.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrderQueue_Returns_Expected_Counts_And_Boxes()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم MealMate الكويت",
            LegalNameEn = "MealMate Restaurant Kuwait",
            TradeNameAr = "مطعم MealMate الكويت",
            TradeNameEn = "MealMate Restaurant Kuwait",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);

        var messages = NullLocalizedMessageProvider.Instance;
        var recommendationEngine = new DispatchRecommendationEngine(
            db,
            new GoogleRoutingService(new HttpClient(), Options.Create(new GoogleMapsOptions()), NullLogger<GoogleRoutingService>.Instance),
            messages);

        var handler = new GetDispatcherOrderQueueQueryHandler(db, currentUser, messages, recommendationEngine);

        // Act
        var result = await handler.Handle(new GetDispatcherOrderQueueQuery(restaurant.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var queue = result.Value;
        queue.Restaurant.NameAr.Should().Be("مطعم MealMate الكويت");
        queue.Counts.PendingCount.Should().Be(23);
        queue.Counts.AssignedCount.Should().Be(37);
        queue.Counts.InDeliveryCount.Should().Be(58);
        queue.Counts.IssuesCount.Should().Be(2);
        queue.Boxes.Should().HaveCount(5);

        var firstBox = queue.Boxes[0];
        firstBox.BoxCode.Should().Be("#BX-1256");
        firstBox.ZoneName.Should().Be("منطقة السالمية");
        firstBox.MealCount.Should().Be(8);
        firstBox.DeliveryTimeWindow.Should().Be("09:30-10:30 ص");
        firstBox.DistanceKm.Should().Be(6.2);
        firstBox.Suggestion.Should().NotBeNull();
        firstBox.Suggestion!.DriverName.Should().Be("أحمد");
    }

    [Fact]
    public async Task GetOrderQueue_Filter_By_Status()
    {
        // Arrange
        await using var db = CreateDb();
        var currentUser = Substitute.For<ICurrentUser>();
        var messages = NullLocalizedMessageProvider.Instance;
        var recommendationEngine = Substitute.For<IDispatchRecommendationEngine>();
        var handler = new GetDispatcherOrderQueueQueryHandler(db, currentUser, messages, recommendationEngine);

        // Act
        var result = await handler.Handle(new GetDispatcherOrderQueueQuery(null, "Pending"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Boxes.Should().OnlyContain(b => b.Status == "Pending");
    }

    [Fact]
    public async Task GetBoxCandidates_Returns_Ranked_Drivers()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم التجربة",
            LegalNameEn = "Test Restaurant",
            TradeNameAr = "مطعم التجربة",
            TradeNameEn = "Test Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        var messages = NullLocalizedMessageProvider.Instance;
        var recommendationEngine = new DispatchRecommendationEngine(
            db,
            new GoogleRoutingService(new HttpClient(), Options.Create(new GoogleMapsOptions()), NullLogger<GoogleRoutingService>.Instance),
            messages);

        var handler = new GetBoxCandidateDriversQueryHandler(db, currentUser, recommendationEngine);

        // Act
        var result = await handler.Handle(new GetBoxCandidateDriversQuery(Guid.NewGuid(), restaurant.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        result.Value[0].IsRecommended.Should().BeTrue();
        result.Value[0].DistanceKm.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AssignBoxToDriver_Links_Driver_And_Returns_Success()
    {
        // Arrange
        await using var db = CreateDb();
        var driver = new DriverProfile
        {
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            VehiclePlate = "DX-439612"
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            Code = "TRP-1",
            Status = TripStatus.Planned,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-1256",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة غداء",
            MealSummaryEn = "Lunch Meal",
            CustomerMaskedId = "CUST-1",
            SlotLabel = "1",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new AssignBoxToDriverCommandHandler(db, messages);

        // Act
        var result = await handler.Handle(new AssignBoxToDriverCommand(stop.Id, driver.Id, "إسناد سريع"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.DriverId.Should().Be(driver.Id);
        result.Value.DriverName.Should().Be("أحمد السعيد");

        var assignment = await db.DriverAssignments.FirstOrDefaultAsync(da => da.TripId == trip.Id && da.DriverProfileId == driver.Id);
        assignment.Should().NotBeNull();
        assignment!.Status.Should().Be("Active");
    }
}
