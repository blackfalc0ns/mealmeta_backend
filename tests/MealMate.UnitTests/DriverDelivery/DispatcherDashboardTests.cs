using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
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

        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.CurrentCulture.Returns(SupportedCultures.ArabicCulture);
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

    [Fact]
    public async Task GetBoxAssignmentDetails_Returns_Box_BestSuggestion_And_Candidates()
    {
        // Arrange
        await using var db = CreateDb();
        var currentUser = Substitute.For<ICurrentUser>();
        var messages = new ArabicTestMessageProvider();
        var recommendationEngine = new DispatchRecommendationEngine(
            db,
            new GoogleRoutingService(new HttpClient(), Options.Create(new GoogleMapsOptions()), NullLogger<GoogleRoutingService>.Instance),
            messages);

        var handler = new GetBoxAssignmentDetailsQueryHandler(db, currentUser, messages, recommendationEngine);

        // Act
        var result = await handler.Handle(new GetBoxAssignmentDetailsQuery(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var details = result.Value;
        details.Box.Should().NotBeNull();
        details.Box.BoxCode.Should().Be("#BX-1256");
        details.Box.ZoneName.Should().Contain("السالمية");
        details.Box.MealCount.Should().Be(8);
        details.Box.Priority.Should().Be("HighPriority");

        // Best Suggestion (سالم الحربي)
        details.BestSuggestion.Should().NotBeNull();
        details.BestSuggestion!.FullName.Should().Be("سالم الحربي");
        details.BestSuggestion!.IsRecommended.Should().BeTrue();
        details.BestSuggestion!.DistanceKm.Should().Be(4.8);
        details.BestSuggestion!.CurrentLoadBoxes.Should().Be(4);
        details.BestSuggestion!.DriverStatus.Should().Be("Available");
        details.BestSuggestion!.StatusTag.Should().Be("الأقرب");
        details.BestSuggestion!.EstimatedFinishTimeText.Should().NotBeNullOrEmpty();

        // Candidates list
        details.Candidates.Should().HaveCount(4);
        details.Candidates[0].Rank.Should().Be(1);
        details.Candidates[0].FullName.Should().Be("أحمد إبراهيم");
        details.Candidates[0].StatusTag.Should().Be("الأقل ضغطاً");
        details.Candidates[0].CurrentLoadBoxes.Should().Be(0);

        details.Candidates[1].Rank.Should().Be(2);
        details.Candidates[1].FullName.Should().Be("محمد السعيد");
        details.Candidates[1].DriverStatus.Should().Be("Busy");
        details.Candidates[1].StatusTag.Should().Be("منشغل بتسليم");

        details.Candidates[2].Rank.Should().Be(3);
        details.Candidates[2].FullName.Should().Be("يوسف العتيبي");
        details.Candidates[2].DriverStatus.Should().Be("InDelivery");

        details.Candidates[3].Rank.Should().Be(4);
        details.Candidates[3].FullName.Should().Be("سالم الدوسري");
        details.Candidates[3].DriverStatus.Should().Be("Returning");
    }

    [Fact]
    public async Task GetBoxSummary_Returns_Meal_Items_And_Handover_Barcode()
    {
        // Arrange
        await using var db = CreateDb();
        var currentUser = Substitute.For<ICurrentUser>();
        var messages = new ArabicTestMessageProvider();
        var handler = new GetBoxContentSummaryQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetBoxContentSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var summary = result.Value;
        summary.BoxCode.Should().Be("#BX-1256");
        summary.Barcode.Should().Contain("BX-1256");
        summary.CustomerNameMasked.Should().Contain("***");
        summary.CustomerPhoneMasked.Should().Contain("***");
        summary.DeliveryNotes.Should().NotBeNullOrEmpty();
        summary.Allergies.Should().NotBeEmpty();
        summary.Meals.Should().HaveCount(4);
        summary.Meals.Should().Contain(m => m.MealName.Contains("سالمون"));
    }

    [Fact]
    public async Task GetDriverRoster_Returns_Area_Chips_Counts_And_Drivers()
    {
        // Arrange
        await using var db = CreateDb();
        var (restaurantId, _) = await SeedRealisticRosterDataAsync(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurantId);

        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherDriverRosterQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDriverRosterQuery(restaurantId, "ByArea", "السالمية"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var roster = result.Value;
        roster.Counts.TotalCount.Should().Be(13);
        roster.Counts.AvailableCount.Should().Be(8);
        roster.Counts.BusyCount.Should().Be(5);
        roster.SelectedArea.Should().Be("السالمية");
        roster.SectionTitle.Should().Contain("السالمية");
        roster.Areas.Should().HaveCount(5);
        roster.Areas.Should().Contain(a => a.Name == "السالمية" && a.IsSelected && a.DriverCount == 8);

        roster.Drivers.Should().HaveCount(8);
        var firstDriver = roster.Drivers[0];
        firstDriver.DriverCode.Should().Be("ID:D-1025");
        firstDriver.FullName.Should().Be("أحمد محمد");
        firstDriver.Status.Should().Be("Available");
        firstDriver.StatusText.Should().Be("متاح");
        firstDriver.StatusDotColor.Should().Be("Green");
        firstDriver.IsAvailableForSelection.Should().BeTrue();
        firstDriver.ActionText.Should().Be("+ اختيار");
        firstDriver.ActionEnabled.Should().BeTrue();
        firstDriver.ActiveOrdersCount.Should().Be(1);
        firstDriver.CompletedOrdersTodayCount.Should().Be(12);
        firstDriver.DistanceText.Should().Be("9 Km");

        // Verify unavailable driver
        var busyDriver = roster.Drivers.FirstOrDefault(d => d.Status == "OnTheWay");
        busyDriver.Should().NotBeNull();
        busyDriver!.IsAvailableForSelection.Should().BeFalse();
        busyDriver.ActionText.Should().Be("غير متاح");
        busyDriver.ActionEnabled.Should().BeFalse();
        busyDriver.StatusText.Should().Be("في طريقه");
        busyDriver.StatusDotColor.Should().Be("Orange");
    }

    [Fact]
    public async Task GetDriverRoster_AllView_Returns_All_13_Drivers()
    {
        // Arrange
        await using var db = CreateDb();
        var (restaurantId, _) = await SeedRealisticRosterDataAsync(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurantId);

        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherDriverRosterQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDriverRosterQuery(restaurantId, "All"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Drivers.Should().HaveCount(13);
        result.Value.SectionTitle.Should().Contain("13");
    }

    [Fact]
    public async Task GetDriverRoster_Filter_By_Status_And_SortBy()
    {
        // Arrange
        await using var db = CreateDb();
        var (restaurantId, _) = await SeedRealisticRosterDataAsync(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurantId);

        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherDriverRosterQueryHandler(db, currentUser, messages);

        // Act: Filter Available & Sort by distance
        var result = await handler.Handle(new GetDispatcherDriverRosterQuery(
            RestaurantId: restaurantId,
            View: "All",
            Status: "Available",
            SortBy: "distance"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Drivers.Should().OnlyContain(d => d.Status == "Available");
        result.Value.Drivers.Should().BeInAscendingOrder(d => d.DistanceKm);
    }

    [Fact]
    public async Task GetDriverRoster_Returns_Empty_When_No_Drivers_In_Db()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var messages = new ArabicTestMessageProvider();
        var currentUser = Substitute.For<ICurrentUser>();
        var handler = new GetDispatcherDriverRosterQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDriverRosterQuery(restaurantId), CancellationToken.None);

        // Assert: No mock fallback, returns clean empty roster
        result.IsSuccess.Should().BeTrue();
        result.Value.Counts.TotalCount.Should().Be(0);
        result.Value.Counts.AvailableCount.Should().Be(0);
        result.Value.Counts.BusyCount.Should().Be(0);
        result.Value.Drivers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLiveMonitoring_Returns_Correct_4_Kpis_And_Driver_Cards()
    {
        // Arrange
        await using var db = CreateDb();
        var (restaurantId, _) = await SeedRealisticRosterDataAsync(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurantId);

        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherLiveMonitoringQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherLiveMonitoringQuery(restaurantId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var data = result.Value;
        data.Kpis.ActiveDriversCount.Should().BeGreaterThan(0);
        data.Drivers.Should().NotBeEmpty();
        data.Drivers.Should().Contain(d => d.StatusCategory == "InDelivery");
        data.Drivers.Should().Contain(d => !string.IsNullOrWhiteSpace(d.LocationZone));
        data.Drivers.Should().Contain(d => !string.IsNullOrWhiteSpace(d.RemainingDistanceText));
    }

    [Fact]
    public async Task GetLiveMonitoring_When_No_Drivers_Returns_Zero_Kpis_And_Empty_List()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var messages = new ArabicTestMessageProvider();
        var currentUser = Substitute.For<ICurrentUser>();
        var handler = new GetDispatcherLiveMonitoringQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherLiveMonitoringQuery(restaurantId), CancellationToken.None);

        // Assert: 100% Zero Mock Data assertion
        result.IsSuccess.Should().BeTrue();
        result.Value.Kpis.ActiveDriversCount.Should().Be(0);
        result.Value.Kpis.InDeliveryCount.Should().Be(0);
        result.Value.Kpis.PausedCount.Should().Be(0);
        result.Value.Kpis.AttentionRequiredCount.Should().Be(0);
        result.Value.Drivers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLiveDriverLocations_When_No_Drivers_Returns_Empty_No_Mock_Fallback()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var messages = new ArabicTestMessageProvider();
        var currentUser = Substitute.For<ICurrentUser>();
        var handler = new GetDispatcherDriverLocationsQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDriverLocationsQuery(restaurantId), CancellationToken.None);

        // Assert: Removed mock fallback
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDriverDetails_Returns_Profile_Kpis_And_DailySummary()
    {
        // Arrange
        await using var db = CreateDb();
        var (_, drivers) = await SeedRealisticRosterDataAsync(db);
        var driver = drivers[0];

        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherDriverDetailsQueryHandler(db, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDriverDetailsQuery(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var details = result.Value;
        details.Driver.DriverId.Should().Be(driver.Id);
        details.Driver.FullName.Should().NotBeNullOrWhiteSpace();
        details.Driver.DriverCode.Should().NotBeNullOrWhiteSpace();
        details.Driver.PhoneNumber.Should().NotBeNullOrWhiteSpace();
        details.Kpis.Rating.Should().BeGreaterThan(0);
        details.DailySummary.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDriverActiveBoxes_Returns_Assigned_Boxes_With_Customer_Details()
    {
        // Arrange
        await using var db = CreateDb();
        var (_, drivers) = await SeedRealisticRosterDataAsync(db);
        var driver = drivers[0];

        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherDriverActiveBoxesQueryHandler(db, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDriverActiveBoxesQuery(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.DriverId.Should().Be(driver.Id);
        result.Value.Boxes.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDriverCurrentLocation_Returns_Coordinates_And_Address()
    {
        // Arrange
        await using var db = CreateDb();
        var (_, drivers) = await SeedRealisticRosterDataAsync(db);
        var driver = drivers[0];

        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherDriverCurrentLocationQueryHandler(db, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDriverCurrentLocationQuery(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var loc = result.Value;
        loc.DriverId.Should().Be(driver.Id);
        loc.Latitude.Should().BeGreaterThan(0);
        loc.Longitude.Should().BeGreaterThan(0);
        loc.DestinationLatitude.Should().NotBeNull();
        loc.DestinationLongitude.Should().NotBeNull();
        loc.StreetAddress.Should().NotBeNullOrWhiteSpace();
        loc.TimeAgoText.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetDriverDetails_Returns_NotFound_When_Invalid_Id()
    {
        // Arrange
        await using var db = CreateDb();
        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherDriverDetailsQueryHandler(db, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDriverDetailsQuery(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    private static async Task<(Guid restaurantId, List<DriverProfile> drivers)> SeedRealisticRosterDataAsync(AppDbContext db)
    {
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم ميل ميت",
            LegalNameEn = "MealMate Restaurant",
            TradeNameAr = "مطعم MealMate الكويت",
            TradeNameEn = "MealMate Restaurant Kuwait",
            UserId = Guid.NewGuid(),
            Latitude = 29.3374m,
            Longitude = 48.0761m
        };
        db.RestaurantProfiles.Add(restaurant);
        var restaurantId = restaurant.Id;

        // 1. Customer Addresses for 5 Kuwait areas from real customer orders
        var custSalmiya = new CustomerAddress { UserId = Guid.NewGuid(), Area = "السالمية", AddressLine = "السالمية، قطعة 4، جادة 12", Label = "المنزل" };
        var custHawally = new CustomerAddress { UserId = Guid.NewGuid(), Area = "حولي", AddressLine = "حولي، شارع تونس، مجمع 8", Label = "العمل" };
        var custHateen = new CustomerAddress { UserId = Guid.NewGuid(), Area = "حطين", AddressLine = "حطين، قطعة 2، شارع 10", Label = "المنزل" };
        var custFarwaniya = new CustomerAddress { UserId = Guid.NewGuid(), Area = "الفروانية", AddressLine = "الفروانية، قطعة 1، شارع حبيب مناور", Label = "المنزل" };
        var custCapital = new CustomerAddress { UserId = Guid.NewGuid(), Area = "العاصمة", AddressLine = "مدينة الكويت، شرق، برج الراية", Label = "العمل" };
        db.CustomerAddresses.AddRange(custSalmiya, custHawally, custHateen, custFarwaniya, custCapital);

        // Subscription days linking these customer addresses to the restaurant
        db.SubscriptionDays.AddRange(
            new SubscriptionDay { RestaurantId = restaurantId, DeliveryAddressId = custSalmiya.Id, DeliveryAddressLine = custSalmiya.AddressLine, ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow) },
            new SubscriptionDay { RestaurantId = restaurantId, DeliveryAddressId = custHawally.Id, DeliveryAddressLine = custHawally.AddressLine, ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow) },
            new SubscriptionDay { RestaurantId = restaurantId, DeliveryAddressId = custHateen.Id, DeliveryAddressLine = custHateen.AddressLine, ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow) },
            new SubscriptionDay { RestaurantId = restaurantId, DeliveryAddressId = custFarwaniya.Id, DeliveryAddressLine = custFarwaniya.AddressLine, ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow) },
            new SubscriptionDay { RestaurantId = restaurantId, DeliveryAddressId = custCapital.Id, DeliveryAddressLine = custCapital.AddressLine, ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow) }
        );

        // 2. Kuwait Regions for Driver zone lookup
        var regSalmiya = new Region { Code = "salmiya", NameAr = "السالمية", NameEn = "Salmiya", CountryId = Guid.NewGuid() };
        var regHawally = new Region { Code = "hawally", NameAr = "حولي", NameEn = "Hawally", CountryId = Guid.NewGuid() };
        var regHateen = new Region { Code = "hatteen", NameAr = "حطين", NameEn = "Hateen", CountryId = Guid.NewGuid() };
        var regFarwaniya = new Region { Code = "farwaniya", NameAr = "الفروانية", NameEn = "Farwaniya", CountryId = Guid.NewGuid() };
        var regCapital = new Region { Code = "capital", NameAr = "العاصمة", NameEn = "Capital", CountryId = Guid.NewGuid() };
        db.Regions.AddRange(regSalmiya, regHawally, regHateen, regFarwaniya, regCapital);

        // 3. 13 DriverProfiles
        // Salmiya (8 drivers)
        var d1 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1025", FullNameAr = "أحمد محمد", FullNameEn = "Ahmed Mohammed", IsAvailable = true, RegionId = regSalmiya.Id };
        var d2 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1044", FullNameAr = "محمد العتيبي", FullNameEn = "Mohammed Al-Otaibi", IsAvailable = true, RegionId = regSalmiya.Id };
        var d3 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1044", FullNameAr = "محمد علي", FullNameEn = "Mohammed Ali", IsAvailable = true, RegionId = regSalmiya.Id };
        var d4 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1044", FullNameAr = "محمد العتيبي", FullNameEn = "Mohammed Al-Otaibi", IsAvailable = true, RegionId = regSalmiya.Id };
        var d5 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1044", FullNameAr = "فهد العنزي", FullNameEn = "Fahad Al-Anzi", IsAvailable = false, RegionId = regSalmiya.Id };
        var d6 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1045", FullNameAr = "سالم الحربي", FullNameEn = "Salem Al-Harbi", IsAvailable = true, RegionId = regSalmiya.Id };
        var d7 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1046", FullNameAr = "يوسف العتيبي", FullNameEn = "Yousef Al-Otaibi", IsAvailable = true, RegionId = regSalmiya.Id };
        var d8 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1047", FullNameAr = "سالم الدوسري", FullNameEn = "Salem Al-Dossary", IsAvailable = true, RegionId = regSalmiya.Id };

        // Hawally (2 drivers)
        var d9 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1048", FullNameAr = "عبد الله الشمري", FullNameEn = "Abdullah Al-Shammari", IsAvailable = true, RegionId = regHawally.Id };
        var d10 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1049", FullNameAr = "جابر الفضلي", FullNameEn = "Jaber Al-Fadhli", IsAvailable = true, RegionId = regHawally.Id };

        // Hateen (1 driver)
        var d11 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1050", FullNameAr = "مبارك الصباح", FullNameEn = "Mubarak Al-Sabah", IsAvailable = true, RegionId = regHateen.Id };

        // Farwaniya (1 driver)
        var d12 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1051", FullNameAr = "طارق العنزي", FullNameEn = "Tariq Al-Anzi", IsAvailable = true, RegionId = regFarwaniya.Id };

        // Capital (1 driver)
        var d13 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "ID:D-1052", FullNameAr = "فيصل الرشيدي", FullNameEn = "Faisal Al-Rashidi", IsAvailable = false, RegionId = regCapital.Id };

        var allDrivers = new List<DriverProfile> { d1, d2, d3, d4, d5, d6, d7, d8, d9, d10, d11, d12, d13 };
        db.DriverProfiles.AddRange(allDrivers);

        // Trips & Assignments:
        // d1 has 1 active trip and completed deliveries today
        var trip1 = new Trip { RestaurantId = restaurantId, Status = TripStatus.InProgress, Code = "TRIP-01" };
        var assign1 = new DriverAssignment { TripId = trip1.Id, DriverProfileId = d1.Id, Status = "Available", Trip = trip1, AssignedAtUtc = DateTime.UtcNow };
        trip1.Stops.Add(new TripStop
        {
            TripId = trip1.Id,
            Status = TripStopStatus.Pending,
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            OrderCode = "ORD-ACT-1",
            CustomerMaskedId = "CUST-ACT-1",
            MealSummaryAr = "وجبة نشطة",
            MealSummaryEn = "Active Meal",
            SlotLabel = "الغداء"
        });
        db.Trips.Add(trip1);
        db.DriverAssignments.Add(assign1);

        // 12 completed trip stops today for d1
        for (int i = 1; i <= 12; i++)
        {
            var compTrip = new Trip { RestaurantId = restaurantId, Status = TripStatus.Completed, Code = $"TRIP-C-{i}" };
            var compAssign = new DriverAssignment { TripId = compTrip.Id, DriverProfileId = d1.Id, Status = "Completed", Trip = compTrip, AssignedAtUtc = DateTime.UtcNow.AddMinutes(-i * 10) };
            compTrip.Stops.Add(new TripStop
            {
                TripId = compTrip.Id,
                Status = TripStopStatus.Delivered,
                DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-i * 10),
                ZoneNameAr = "السالمية",
                ZoneNameEn = "Salmiya",
                OrderCode = $"ORD-{i}",
                CustomerMaskedId = $"CUST-{i}",
                MealSummaryAr = "وجبة غداء",
                MealSummaryEn = "Lunch Meal",
                SlotLabel = "الغداء"
            });
            db.Trips.Add(compTrip);
            db.DriverAssignments.Add(compAssign);
        }

        // d3 is OnTheWay (active trip in progress with status OnTheWay)
        var trip3 = new Trip { RestaurantId = restaurantId, Status = TripStatus.InProgress, Code = "TRIP-03" };
        var assign3 = new DriverAssignment { TripId = trip3.Id, DriverProfileId = d3.Id, Status = "OnTheWay", Trip = trip3, AssignedAtUtc = DateTime.UtcNow };
        db.Trips.Add(trip3);
        db.DriverAssignments.Add(assign3);

        // d10 is OnTheWay
        var trip10 = new Trip { RestaurantId = restaurantId, Status = TripStatus.InProgress, Code = "TRIP-10" };
        var assign10 = new DriverAssignment { TripId = trip10.Id, DriverProfileId = d10.Id, Status = "OnTheWay", Trip = trip10, AssignedAtUtc = DateTime.UtcNow };
        db.Trips.Add(trip10);
        db.DriverAssignments.Add(assign10);

        // d12 is OnTheWay
        var trip12 = new Trip { RestaurantId = restaurantId, Status = TripStatus.InProgress, Code = "TRIP-12" };
        var assign12 = new DriverAssignment { TripId = trip12.Id, DriverProfileId = d12.Id, Status = "OnTheWay", Trip = trip12, AssignedAtUtc = DateTime.UtcNow };
        db.Trips.Add(trip12);
        db.DriverAssignments.Add(assign12);

        // Deterministic tracking points for distance
        foreach (var drv in allDrivers)
        {
            db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
            {
                DriverProfileId = drv.Id,
                Latitude = 29.3374m,
                Longitude = 48.1661m, // ~9 km away
                RecordedAtUtc = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return (restaurantId, allDrivers);
    }

    private sealed class ArabicTestMessageProvider : ILocalizedMessageProvider
    {
        public System.Globalization.CultureInfo CurrentCulture => new("ar-KW");
        public string GetMessage(string code, string? fallback = null, params object[] arguments) =>
            fallback != null ? (arguments.Length == 0 ? fallback : string.Format(fallback, arguments)) : code;
        public string GetMessage(string code, System.Globalization.CultureInfo culture, string? fallback = null, params object[] arguments) =>
            fallback != null ? (arguments.Length == 0 ? fallback : string.Format(culture, fallback, arguments)) : code;
    }
}


