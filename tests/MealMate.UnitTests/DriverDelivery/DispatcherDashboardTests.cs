using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
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

    [Fact]
    public async Task GetBoxTracking_Returns_Complete_Timeline_Driver_And_Details()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();

        var driver = new DriverProfile
        {
            RestaurantId = restaurantId,
            LicenseNumber = "DR-1025",
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            IsAvailable = true,
            UserId = Guid.NewGuid()
        };
        db.DriverProfiles.Add(driver);

        db.DriverRegistrations.Add(new DriverRegistration
        {
            ResultingDriverProfileId = driver.Id,
            Phone = "+965 50 123 4567",
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            NationalId = "290010112345",
            Nationality = "Kuwaiti"
        });

        var program = new Program
        {
            Code = "BALANCED",
            NameAr = "دايت متوازن",
            NameEn = "Balanced Diet",
            DescriptionAr = "وصف",
            DescriptionEn = "Desc"
        };
        db.Programs.Add(program);

        var sub = new Subscription
        {
            ProgramId = program.Id,
            Program = program,
            CustomerUserId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active
        };
        db.Subscriptions.Add(sub);

        var subDay = new SubscriptionDay
        {
            SubscriptionId = sub.Id,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DeliveryAddressLine = "السالمية، قطعة 4، شارع 12"
        };
        db.SubscriptionDays.Add(subDay);

        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-101",
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTime.UtcNow
        };
        db.Trips.Add(trip);

        var assignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            AssignedAtUtc = DateTime.UtcNow.AddMinutes(-20),
            Status = "Active"
        };
        db.DriverAssignments.Add(assignment);
        trip.DriverAssignments.Add(assignment);

        var stop = new TripStop
        {
            TripId = trip.Id,
            Trip = trip,
            SubscriptionDayId = subDay.Id,
            OrderCode = "BX-10256",
            CustomerMaskedId = "CUST-99",
            CustomerName = "محمد الفضلي",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات (يوم كامل)",
            MealSummaryEn = "3 meals (full day)",
            SlotLabel = "12:30 م",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherBoxTrackingQueryHandler(db, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherBoxTrackingQuery(stop.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var data = result.Value;
        data.Box.BoxCode.Should().Be("#BX-10256");
        data.Box.CustomerName.Should().Be("محمد الفضلي");
        data.Box.Status.Should().Be("InDelivery");
        data.Box.StatusText.Should().Be("في الطريق");

        data.Timeline.Should().HaveCount(4);
        data.Timeline[0].Step.Should().Be(1);
        data.Timeline[0].State.Should().Be("Completed");
        data.Timeline[1].Step.Should().Be(2);
        data.Timeline[1].State.Should().Be("Completed");
        data.Timeline[2].Step.Should().Be(3);
        data.Timeline[2].State.Should().Be("Active");
        data.Timeline[3].Step.Should().Be(4);
        data.Timeline[3].State.Should().Be("Pending");

        data.Driver.Should().NotBeNull();
        data.Driver!.FullName.Should().Be("أحمد السعيد");
        data.Driver.DriverCode.Should().Be("DR-1025");
        data.Driver.PhoneNumber.Should().Be("+965 50 123 4567");

        data.Details.ProgramType.Should().Be("دايت متوازن");
        data.Details.MealsSummary.Should().Be("3 وجبات (يوم كامل)");
    }

    [Fact]
    public async Task GetBoxTracking_Returns_NotFound_When_Box_DoesNotExist()
    {
        // Arrange
        await using var db = CreateDb();
        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherBoxTrackingQueryHandler(db, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherBoxTrackingQuery(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task ReportBoxIssue_Records_Audit_And_Updates_Stop_FailureReason()
    {
        // Arrange
        await using var db = CreateDb();
        var stop = new TripStop
        {
            TripId = Guid.NewGuid(),
            OrderCode = "BX-9999",
            CustomerMaskedId = "C-1",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "12:00 م"
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var messages = new ArabicTestMessageProvider();
        var audit = Substitute.For<IAuditLogger>();
        var handler = new ReportBoxIssueCommandHandler(db, messages, audit);

        var command = new ReportBoxIssueCommand(
            BoxId: stop.Id,
            IssueType: "DamagedBox",
            Description: "البوكس تعرض للتلف أثناء النقل",
            Severity: "High");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.BoxId.Should().Be(stop.Id);
        result.Value.IssueId.Should().NotBeEmpty();

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.FailureReason.Should().Contain("DamagedBox");
        updatedStop.FailureReason.Should().Contain("البوكس تعرض للتلف أثناء النقل");

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "BoxIssueReported" && e.EntityId == stop.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportBoxIssue_Returns_Validation_Error_When_Required_Fields_Empty()
    {
        // Arrange
        await using var db = CreateDb();
        var messages = new ArabicTestMessageProvider();
        var audit = Substitute.For<IAuditLogger>();
        var handler = new ReportBoxIssueCommandHandler(db, messages, audit);

        // Act
        var result = await handler.Handle(new ReportBoxIssueCommand(Guid.NewGuid(), "", ""), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task GetPerformanceOverview_Returns_Complete_Kpis_Distribution_And_TopDrivers()
    {
        // Arrange
        await using var db = CreateDb();
        var rest = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "مطعم ميل ميت",
            LegalNameEn = "MealMate Restaurant",
            TradeNameAr = "مطعم ميل ميت",
            TradeNameEn = "MealMate Restaurant"
        };
        db.RestaurantProfiles.Add(rest);
        var restaurantId = rest.Id;

        var d1 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "DR-1025", FullNameAr = "أحمد السعيد", FullNameEn = "Ahmed Al-Saeed", IsAvailable = true };
        var d2 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "DR-1032", FullNameAr = "محمد العنزي", FullNameEn = "Mohammed Al-Anzi", IsAvailable = true };
        var d3 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "DR-1018", FullNameAr = "يوسف خالد", FullNameEn = "Yousef Khaled", IsAvailable = true };
        db.DriverProfiles.AddRange(d1, d2, d3);

        db.DriverRatings.AddRange(
            new DriverRating { DriverProfileId = d1.Id, Score = 5, CustomerUserId = Guid.NewGuid() },
            new DriverRating { DriverProfileId = d2.Id, Score = 4, CustomerUserId = Guid.NewGuid() },
            new DriverRating { DriverProfileId = d3.Id, Score = 4, CustomerUserId = Guid.NewGuid() }
        );

        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-2026-PERF",
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTime.UtcNow
        };
        db.Trips.Add(trip);

        var assign1 = new DriverAssignment { TripId = trip.Id, DriverProfileId = d1.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow };
        var assign2 = new DriverAssignment { TripId = trip.Id, DriverProfileId = d2.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow };
        db.DriverAssignments.AddRange(assign1, assign2);
        trip.DriverAssignments.Add(assign1);
        trip.DriverAssignments.Add(assign2);

        // Stops: 3 delivered, 1 failed
        var stop1 = new TripStop { TripId = trip.Id, Status = TripStopStatus.Delivered, DeliveredAtUtc = DateTime.UtcNow, OrderCode = "B1", CustomerMaskedId = "C1", ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "12:00" };
        var stop2 = new TripStop { TripId = trip.Id, Status = TripStopStatus.Delivered, DeliveredAtUtc = DateTime.UtcNow.AddMinutes(50), OrderCode = "B2", CustomerMaskedId = "C2", ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "12:00" };
        var stop3 = new TripStop { TripId = trip.Id, Status = TripStopStatus.Delivered, DeliveredAtUtc = DateTime.UtcNow, OrderCode = "B3", CustomerMaskedId = "C3", ZoneNameAr = "حولي", ZoneNameEn = "Hawally", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "12:00" };
        var stop4 = new TripStop { TripId = trip.Id, Status = TripStopStatus.Failed, OrderCode = "B4", CustomerMaskedId = "C4", ZoneNameAr = "حولي", ZoneNameEn = "Hawally", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "12:00" };
        db.TripStops.AddRange(stop1, stop2, stop3, stop4);
        trip.Stops.Add(stop1);
        trip.Stops.Add(stop2);
        trip.Stops.Add(stop3);
        trip.Stops.Add(stop4);

        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurantId);
        var messages = new ArabicTestMessageProvider();

        var handler = new GetDispatcherPerformanceOverviewQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherPerformanceOverviewQuery(restaurantId, "Last7Days"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var data = result.Value;
        data.Kpis.TotalBoxes.Should().Be(4);
        data.Kpis.DeliveredCount.Should().Be(3);
        data.Kpis.FailedCount.Should().Be(1);
        data.Kpis.DeliveredPercentage.Should().Be(75.0m);
        data.Kpis.FailedPercentage.Should().Be(25.0m);

        data.Distribution.Segments.Should().HaveCount(4);
        data.Distribution.Segments.Select(s => s.Key).Should().Contain(new[] { "OnTime", "Delayed", "Failed", "Cancelled" });

        data.TopDrivers.Should().NotBeEmpty();
        data.TopDrivers[0].Rank.Should().Be(1);
        data.TopDrivers[0].IsHighlighted.Should().BeTrue();
        data.TopDrivers[0].FullName.Should().Be("أحمد السعيد");

        data.DriversTable.Should().HaveCount(3);
        data.DriversTable.Select(d => d.FullName).Should().Contain("أحمد السعيد");
        data.FromDate.Should().NotBeNullOrWhiteSpace();
        data.ToDate.Should().NotBeNullOrWhiteSpace();
        data.DriversTable[0].AvgDelayLevel.Should().BeOneOf("Good", "Warning", "Critical");
    }

    [Fact]
    public async Task GetDriverComparison_Returns_Driver_Comparison_Metrics()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();

        var d1 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "DR-1025", FullNameAr = "أحمد السعيد", FullNameEn = "Ahmed Al-Saeed", IsAvailable = true };
        var d2 = new DriverProfile { RestaurantId = restaurantId, LicenseNumber = "DR-1032", FullNameAr = "محمد العنزي", FullNameEn = "Mohammed Al-Anzi", IsAvailable = true };
        db.DriverProfiles.AddRange(d1, d2);

        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-COMP",
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Trips.Add(trip);

        var assign = new DriverAssignment { TripId = trip.Id, DriverProfileId = d1.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow };
        db.DriverAssignments.Add(assign);
        trip.DriverAssignments.Add(assign);

        var stop = new TripStop { TripId = trip.Id, Status = TripStopStatus.Delivered, DeliveredAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30), OrderCode = "BX-C1", CustomerMaskedId = "C1", ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "12:00" };
        db.TripStops.Add(stop);
        trip.Stops.Add(stop);

        db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint { TripId = trip.Id, DriverProfileId = d1.Id, Latitude = 29.3374m, Longitude = 48.0781m, RecordedAtUtc = DateTime.UtcNow.AddMinutes(-10) });
        db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint { TripId = trip.Id, DriverProfileId = d1.Id, Latitude = 29.3400m, Longitude = 48.0800m, RecordedAtUtc = DateTime.UtcNow });

        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurantId);
        var messages = new ArabicTestMessageProvider();

        var handler = new GetDispatcherDriverComparisonQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDriverComparisonQuery(restaurantId, "Last7Days"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Drivers.Should().HaveCount(2);
        result.Value.DateRangeText.Should().NotBeNullOrWhiteSpace();
        result.Value.FromDate.Should().NotBeNullOrWhiteSpace();
        result.Value.ToDate.Should().NotBeNullOrWhiteSpace();
        var driver1Metric = result.Value.Drivers.FirstOrDefault(d => d.DriverId == d1.Id);
        driver1Metric.Should().NotBeNull();
        driver1Metric!.DeliveredCount.Should().Be(1);
        driver1Metric.TotalAssigned.Should().Be(1);
        driver1Metric.DeliveredPercentage.Should().Be(100.0m);
        driver1Metric.FailedPercentage.Should().Be(0.0m);
        driver1Metric.AvgDelayLevel.Should().Be("Good");
        driver1Metric.AvgDelayColor.Should().Be("Green");
        driver1Metric.TotalDistanceKm.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetSupportIssues_Returns_Valid_Counters_Chips_And_Issues()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم ميل ميت",
            LegalNameEn = "MealMate Restaurant",
            TradeNameAr = "مطعم MealMate",
            TradeNameEn = "MealMate Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            NationalId = "290010101234",
            LicenseNumber = "DR-1025",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var reg = new DriverRegistration
        {
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            Phone = "+96550123456",
            NationalId = "290010101234",
            VehicleType = "تويوتا كامري",
            VehicleColor = "أبيض",
            VehiclePlate = "12345",
            Status = VerificationStatus.Approved,
            ResultingDriverProfileId = driver.Id
        };
        db.DriverRegistrations.Add(reg);

        var c1 = new Complaint
        {
            RestaurantId = restaurant.Id,
            DriverProfileId = driver.Id,
            CustomerUserId = Guid.NewGuid(),
            Title = "العميل لم يستجب للاتصال",
            Description = "انتظار 10 دقائق دون رد",
            Category = "DeliveryIssue",
            Status = ComplaintStatus.Submitted,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-15)
        };
        db.Complaints.Add(c1);

        var c2 = new Complaint
        {
            RestaurantId = restaurant.Id,
            DriverProfileId = driver.Id,
            CustomerUserId = Guid.NewGuid(),
            Title = "تلف في كرتون الوجبات",
            Description = "تسرب سوائل",
            Category = "DamagedItem",
            Status = ComplaintStatus.Resolved,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        db.Complaints.Add(c2);

        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);
        var messages = new ArabicTestMessageProvider();

        var handler = new GetDispatcherSupportIssuesQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherSupportIssuesQuery(restaurant.Id, null, null, null), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Counters.OpenCount.Should().Be(1);
        result.Value.Counters.ResolvedCount.Should().Be(1);
        result.Value.Issues.Should().NotBeEmpty();
        var issue = result.Value.Issues.First();
        issue.DriverName.Should().Be("أحمد السعيد");
        issue.VehicleText.Should().Contain("تويوتا كامري");
    }

    [Fact]
    public async Task GetSupportIssues_Pagination_Search_And_Isolation_Rules_Work_Correctly()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم دايت كير",
            LegalNameEn = "Diet Care Restaurant",
            TradeNameAr = "مطعم دايت كير",
            TradeNameEn = "Diet Care Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "محمد عبد الله",
            FullNameEn = "Mohammed Abdullah",
            NationalId = "290010101235",
            LicenseNumber = "DR-2050",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        // Add 5 complaints with different dates, categories, titles, statuses
        var now = DateTime.UtcNow;
        for (int i = 1; i <= 5; i++)
        {
            db.Complaints.Add(new Complaint
            {
                RestaurantId = restaurant.Id,
                CustomerUserId = Guid.NewGuid(),
                DriverProfileId = driver.Id,
                Category = i % 2 == 0 ? "تأخير" : "بوكس تالف",
                Title = $"مشكلة رقم {i} في البوكس",
                Description = $"تفاصيل البلاغ {i}",
                Status = i switch { 1 or 2 => ComplaintStatus.Submitted, 3 => ComplaintStatus.UnderReview, _ => ComplaintStatus.Resolved },
                SubmittedAtUtc = now.AddDays(-i)
            });
        }

        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);
        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherSupportIssuesQueryHandler(db, currentUser, messages);

        // 1. Test Pagination: pageSize = 2, pageNumber = 1
        var pagedResult = await handler.Handle(new GetDispatcherSupportIssuesQuery(
            RestaurantId: restaurant.Id,
            Status: "All",
            PageNumber: 1,
            PageSize: 2), CancellationToken.None);

        pagedResult.IsSuccess.Should().BeTrue();
        pagedResult.Value.Pagination.PageNumber.Should().Be(1);
        pagedResult.Value.Pagination.PageSize.Should().Be(2);
        pagedResult.Value.Pagination.TotalItems.Should().Be(5);
        pagedResult.Value.Pagination.TotalPages.Should().Be(3);
        pagedResult.Value.Pagination.HasPreviousPage.Should().BeFalse();
        pagedResult.Value.Pagination.HasNextPage.Should().BeTrue();
        pagedResult.Value.Issues.Should().HaveCount(2);

        // 2. Test Counters isolation (Status filter should NOT alter counters)
        var filteredByStatus = await handler.Handle(new GetDispatcherSupportIssuesQuery(
            RestaurantId: restaurant.Id,
            Status: "Open",
            PageNumber: 1,
            PageSize: 20), CancellationToken.None);

        filteredByStatus.IsSuccess.Should().BeTrue();
        // Counters still show all 3 statuses even though issues are filtered by Open
        filteredByStatus.Value.Counters.OpenCount.Should().Be(2);
        filteredByStatus.Value.Counters.InProgressCount.Should().Be(1);
        filteredByStatus.Value.Counters.ResolvedCount.Should().Be(2);
        filteredByStatus.Value.Issues.Should().OnlyContain(i => i.Status == "Open");

        // 3. Test Search across driverName, title, category
        var searchResult = await handler.Handle(new GetDispatcherSupportIssuesQuery(
            RestaurantId: restaurant.Id,
            Status: "All",
            Search: "محمد عبد الله",
            PageNumber: 1,
            PageSize: 20), CancellationToken.None);

        searchResult.IsSuccess.Should().BeTrue();
        searchResult.Value.Issues.Should().NotBeEmpty();
        searchResult.Value.Issues.Should().OnlyContain(i => i.DriverName.Contains("محمد عبد الله"));

        // 4. Test DatePreset = Last7Days vs Today
        var last7DaysResult = await handler.Handle(new GetDispatcherSupportIssuesQuery(
            RestaurantId: restaurant.Id,
            Status: "All",
            DatePreset: "Last7Days",
            PageNumber: 1,
            PageSize: 20), CancellationToken.None);

        last7DaysResult.IsSuccess.Should().BeTrue();
        last7DaysResult.Value.Pagination.TotalItems.Should().Be(5);

        // 5. Test Empty Result returns 200 with issues: [] and totalItems: 0
        var emptyResult = await handler.Handle(new GetDispatcherSupportIssuesQuery(
            RestaurantId: restaurant.Id,
            Status: "All",
            Search: "NonExistentKeyword9999",
            PageNumber: 1,
            PageSize: 20), CancellationToken.None);

        emptyResult.IsSuccess.Should().BeTrue();
        emptyResult.Value.Issues.Should().BeEmpty();
        emptyResult.Value.Pagination.TotalItems.Should().Be(0);
        emptyResult.Value.Pagination.TotalPages.Should().Be(0);
        emptyResult.Value.Pagination.HasPreviousPage.Should().BeFalse();
        emptyResult.Value.Pagination.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetProblemDetails_Returns_Complaint_With_Photos_Driver_And_Customer_Details()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم دايت كير",
            LegalNameEn = "Diet Care Restaurant",
            TradeNameAr = "مطعم دايت كير",
            TradeNameEn = "Diet Care Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            NationalId = "290010101234",
            LicenseNumber = "DR-1025",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            RestaurantId = restaurant.Id,
            Code = "TRP-2026-999",
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-10256",
            CustomerMaskedId = "CUST-99",
            CustomerName = "محمد الفضلي",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبتين دايت",
            MealSummaryEn = "2 Diet Meals",
            SlotLabel = "12:30 م - 02:00 م",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        trip.Stops.Add(stop);

        var complaint = new Complaint
        {
            RestaurantId = restaurant.Id,
            DriverProfileId = driver.Id,
            CustomerUserId = Guid.NewGuid(),
            OrderId = stop.Id,
            Title = "العميل لم يستجب للاتصال",
            Description = "انتظار 10 دقائق دون رد",
            Category = "DeliveryIssue",
            Status = ComplaintStatus.Submitted
        };
        complaint.EvidencePhotos.Add(new ComplaintEvidencePhoto
        {
            ComplaintId = complaint.Id,
            StoragePath = "https://cdn.mealmate.local/evidence/photo1.jpg",
            ContentType = "image/jpeg",
            UploadedAtUtc = DateTime.UtcNow
        });
        db.Complaints.Add(complaint);

        await db.SaveChangesAsync();

        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherProblemDetailsQueryHandler(db, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherProblemDetailsQuery(complaint.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Metadata.BoxCode.Should().Be("#BX-10256");
        result.Value.Driver.FullName.Should().Be("أحمد السعيد");
        result.Value.Driver.Status.Should().Be("OnTask");
        result.Value.Driver.VehicleInfo.Should().Contain("تويوتا كامري");
        result.Value.EvidencePhotos.Should().HaveCount(1);
        result.Value.EvidencePhotos[0].Url.Should().Be("https://cdn.mealmate.local/evidence/photo1.jpg");
        result.Value.TripInfo.CustomerName.Should().Be("محمد الفضلي");
        result.Value.Category.Should().Be("DeliveryIssue");
        result.Value.CategoryLabel.Should().Be("مشكلة توصيل");
        result.Value.CategoryColor.Should().Be("#EF4444");
        result.Value.Status.Should().Be("Open");
        result.Value.StatusLabel.Should().Be("مفتوحة");
        result.Value.Resolution.Should().BeNull();
        result.Value.CreatedAtUtc.Should().NotBe(default);
        result.Value.ReportedTimeText.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetReplacementCandidates_Returns_Ranked_Drivers_By_Proximity_And_Area()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم دايت كير",
            LegalNameEn = "Diet Care Restaurant",
            TradeNameAr = "مطعم دايت كير",
            TradeNameEn = "Diet Care Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var currentDriver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            NationalId = "290010101234",
            LicenseNumber = "DR-1025",
            IsAvailable = false
        };
        db.DriverProfiles.Add(currentDriver);

        // Candidate 1 (near, Salmiya)
        var cand1 = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "فهد الدوسري",
            FullNameEn = "Fahad Al-Dosari",
            NationalId = "290010101235",
            LicenseNumber = "DR-1012",
            IsAvailable = true
        };
        db.DriverProfiles.Add(cand1);

        // Candidate 2 (further, Hawally)
        var cand2 = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "جاسم المطيري",
            FullNameEn = "Jassim Al-Mutairi",
            NationalId = "290010101236",
            LicenseNumber = "DR-1038",
            IsAvailable = true
        };
        db.DriverProfiles.Add(cand2);

        var trip1 = new Trip { RestaurantId = restaurant.Id, Code = "T-C1", Status = TripStatus.InProgress, PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        trip1.Stops.Add(new TripStop { TripId = trip1.Id, OrderCode = "BX-S1", CustomerMaskedId = "C1", ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "12:00", Status = TripStopStatus.Pending });
        db.Trips.Add(trip1);
        db.DriverAssignments.Add(new DriverAssignment { TripId = trip1.Id, DriverProfileId = cand1.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow });

        var complaint = new Complaint
        {
            RestaurantId = restaurant.Id,
            DriverProfileId = currentDriver.Id,
            CustomerUserId = Guid.NewGuid(),
            Title = "تعطل المركبة",
            Description = "المركبة معطلة في السالمية",
            Category = "VehicleBreakdown",
            Status = ComplaintStatus.Submitted
        };
        db.Complaints.Add(complaint);

        await db.SaveChangesAsync();

        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherReplacementCandidatesQueryHandler(db, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherReplacementCandidatesQuery(complaint.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentDriver.FullName.Should().Be("أحمد السعيد");
        result.Value.Candidates.Should().NotBeEmpty();
        var top = result.Value.Candidates.First();
        top.DriverId.Should().Be(cand1.Id);
        top.FullName.Should().Be("فهد الدوسري");
    }

    [Fact]
    public async Task ReassignReplacementDriver_Updates_TripStop_And_Creates_Assignment_And_AuditLog()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم دايت كير",
            LegalNameEn = "Diet Care Restaurant",
            TradeNameAr = "مطعم دايت كير",
            TradeNameEn = "Diet Care Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var oldDriver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            NationalId = "290010101234",
            LicenseNumber = "DR-1025",
            IsAvailable = false
        };
        db.DriverProfiles.Add(oldDriver);

        var newDriver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "فهد الدوسري",
            FullNameEn = "Fahad Al-Dosari",
            NationalId = "290010101235",
            LicenseNumber = "DR-1012",
            IsAvailable = true
        };
        db.DriverProfiles.Add(newDriver);

        var trip = new Trip
        {
            RestaurantId = restaurant.Id,
            Code = "TRP-2026-100",
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-10256",
            CustomerMaskedId = "CUST-99",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبتين",
            MealSummaryEn = "2 Meals",
            SlotLabel = "12:30",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        trip.Stops.Add(stop);

        var oldAssignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = oldDriver.Id,
            Status = "Active",
            AssignedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        db.DriverAssignments.Add(oldAssignment);
        trip.DriverAssignments.Add(oldAssignment);

        var complaint = new Complaint
        {
            RestaurantId = restaurant.Id,
            DriverProfileId = oldDriver.Id,
            CustomerUserId = Guid.NewGuid(),
            OrderId = stop.Id,
            Title = "تعطل المركبة",
            Description = "عطل مفاجئ",
            Category = "DeliveryIssue",
            Status = ComplaintStatus.Submitted
        };
        db.Complaints.Add(complaint);

        await db.SaveChangesAsync();

        var auditLogger = Substitute.For<IAuditLogger>();
        var messages = new ArabicTestMessageProvider();

        var handler = new ReassignReplacementDriverCommandHandler(db, messages, auditLogger);

        // Act
        var command = new ReassignReplacementDriverCommand(
            complaint.Id,
            newDriver.Id,
            "تعطل مركبة السائق الحالي");

        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.PreviousDriverId.Should().Be(oldDriver.Id);
        result.Value.ReplacementDriverId.Should().Be(newDriver.Id);
        result.Value.DriverName.Should().Be("فهد الدوسري");
        result.Value.IssueStatus.Should().Be("InProgress");
        result.Value.Message.Should().NotBeNullOrWhiteSpace();
        result.Value.ReplacementDriver.Should().NotBeNull();
        result.Value.ReplacementDriver.FullName.Should().Be("فهد الدوسري");
        result.Value.ReplacementDriver.Status.Should().Be("OnTask");
        result.Value.ReplacementDriver.VehicleInfo.Should().Contain("هيونداي النترا");
        result.Value.ReplacementDriver.Vehicle.Should().NotBeNull();
        result.Value.ReplacementDriver.Vehicle!.Model.Should().Be("هيونداي النترا");

        var updatedComplaint = await db.Complaints.FindAsync(complaint.Id);
        updatedComplaint!.DriverProfileId.Should().Be(newDriver.Id);
        updatedComplaint.Status.Should().Be(ComplaintStatus.UnderReview);

        var newAssignment = await db.DriverAssignments.FirstOrDefaultAsync(a => a.DriverProfileId == newDriver.Id && a.TripId == trip.Id);
        newAssignment.Should().NotBeNull();
        newAssignment!.Status.Should().Be("Active");

        await auditLogger.Received(1).LogAsync(
            Arg.Any<AuditEntry>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetReplacementCandidates_Returns_EmptyCandidates_And_Pagination_When_No_Available_Drivers()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم دايت كير",
            LegalNameEn = "Diet Care Restaurant",
            TradeNameAr = "مطعم دايت كير",
            TradeNameEn = "Diet Care Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var currentDriver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            NationalId = "290010101234",
            LicenseNumber = "DR-1025",
            IsAvailable = false
        };
        db.DriverProfiles.Add(currentDriver);

        var complaint = new Complaint
        {
            RestaurantId = restaurant.Id,
            DriverProfileId = currentDriver.Id,
            CustomerUserId = Guid.NewGuid(),
            Title = "تعطل المركبة",
            Description = "المركبة معطلة",
            Category = "VehicleBreakdown",
            Status = ComplaintStatus.Submitted
        };
        db.Complaints.Add(complaint);
        await db.SaveChangesAsync();

        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherReplacementCandidatesQueryHandler(db, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherReplacementCandidatesQuery(complaint.Id, PageNumber: 1, PageSize: 20), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().BeEmpty();
        result.Value.Pagination.Should().NotBeNull();
        result.Value.Pagination.PageNumber.Should().Be(1);
        result.Value.Pagination.PageSize.Should().Be(20);
        result.Value.Pagination.TotalItems.Should().Be(0);
        result.Value.Pagination.TotalPages.Should().Be(0);
        result.Value.Pagination.HasPreviousPage.Should().BeFalse();
        result.Value.Pagination.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task ReassignReplacementDriver_Returns_Conflict_When_ReplacementDriver_Is_Not_Available()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم دايت كير",
            LegalNameEn = "Diet Care Restaurant",
            TradeNameAr = "مطعم دايت كير",
            TradeNameEn = "Diet Care Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var oldDriver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            NationalId = "290010101234",
            LicenseNumber = "DR-1025",
            IsAvailable = false
        };
        db.DriverProfiles.Add(oldDriver);

        var busyDriver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "فهد الدوسري",
            FullNameEn = "Fahad Al-Dosari",
            NationalId = "290010101235",
            LicenseNumber = "DR-1012",
            IsAvailable = false // already taken / busy
        };
        db.DriverProfiles.Add(busyDriver);

        var complaint = new Complaint
        {
            RestaurantId = restaurant.Id,
            DriverProfileId = oldDriver.Id,
            CustomerUserId = Guid.NewGuid(),
            Title = "تعطل المركبة",
            Description = "عطل مفاجئ",
            Category = "DeliveryIssue",
            Status = ComplaintStatus.Submitted
        };
        db.Complaints.Add(complaint);
        await db.SaveChangesAsync();

        var auditLogger = Substitute.For<IAuditLogger>();
        var messages = new ArabicTestMessageProvider();
        var handler = new ReassignReplacementDriverCommandHandler(db, messages, auditLogger);

        // Act
        var result = await handler.Handle(
            new ReassignReplacementDriverCommand(complaint.Id, busyDriver.Id, "تعطل مركبة السائق"),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("REPLACEMENT_DRIVER_NO_LONGER_AVAILABLE");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task ReassignReplacementDriver_Returns_Validation_Error_When_Reassigning_To_Same_Driver()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم دايت كير",
            LegalNameEn = "Diet Care Restaurant",
            TradeNameAr = "مطعم دايت كير",
            TradeNameEn = "Diet Care Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var currentDriver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            NationalId = "290010101234",
            LicenseNumber = "DR-1025",
            IsAvailable = true
        };
        db.DriverProfiles.Add(currentDriver);

        var complaint = new Complaint
        {
            RestaurantId = restaurant.Id,
            DriverProfileId = currentDriver.Id,
            CustomerUserId = Guid.NewGuid(),
            Title = "تعطل المركبة",
            Description = "عطل مفاجئ",
            Category = "DeliveryIssue",
            Status = ComplaintStatus.Submitted
        };
        db.Complaints.Add(complaint);
        await db.SaveChangesAsync();

        var auditLogger = Substitute.For<IAuditLogger>();
        var messages = new ArabicTestMessageProvider();
        var handler = new ReassignReplacementDriverCommandHandler(db, messages, auditLogger);

        // Act
        var result = await handler.Handle(
            new ReassignReplacementDriverCommand(complaint.Id, currentDriver.Id, "تعيين نفس السائق"),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CANNOT_REASSIGN_TO_SAME_DRIVER");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task ResolveSupportIssue_Marks_Complaint_Resolved_And_Logs_Audit()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم دايت كير",
            LegalNameEn = "Diet Care Restaurant",
            TradeNameAr = "مطعم دايت كير",
            TradeNameEn = "Diet Care Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var complaint = new Complaint
        {
            RestaurantId = restaurant.Id,
            CustomerUserId = Guid.NewGuid(),
            Title = "مشكلة تسليم البوكس",
            Description = "تأخير في الرد",
            Category = "DeliveryIssue",
            Status = ComplaintStatus.Submitted
        };
        db.Complaints.Add(complaint);
        await db.SaveChangesAsync();

        var auditLogger = Substitute.For<IAuditLogger>();
        var messages = new ArabicTestMessageProvider();

        var handler = new ResolveSupportIssueCommandHandler(db, messages, auditLogger);

        // Act
        var command = new ResolveSupportIssueCommand(
            complaint.Id,
            "تم التواصل مع العميل وتسليم الوجبات بنجاح");

        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IssueId.Should().Be(complaint.Id);
        result.Value.Status.Should().Be("Resolved");
        result.Value.StatusLabel.Should().Be("تم الحل");
        result.Value.Resolution.Should().NotBeNull();
        result.Value.Resolution.Notes.Should().Be("تم التواصل مع العميل وتسليم الوجبات بنجاح");

        var updatedComplaint = await db.Complaints.FindAsync(complaint.Id);
        updatedComplaint!.Status.Should().Be(ComplaintStatus.Resolved);

        await auditLogger.Received(1).LogAsync(
            Arg.Any<AuditEntry>(),
            Arg.Any<CancellationToken>());

        // Also verify GetDispatcherProblemDetails returns the resolution
        db.AuditLogs.Add(new AuditLog
        {
            Action = "SupportIssueResolved",
            EntityType = nameof(Complaint),
            EntityId = complaint.Id,
            Reason = "تم التواصل مع العميل وتسليم الوجبات بنجاح",
            OccurredAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var queryHandler = new GetDispatcherProblemDetailsQueryHandler(db, messages);
        var detailsResult = await queryHandler.Handle(new GetDispatcherProblemDetailsQuery(complaint.Id), CancellationToken.None);
        detailsResult.IsSuccess.Should().BeTrue();
        detailsResult.Value.Status.Should().Be("Resolved");
        detailsResult.Value.StatusLabel.Should().Be("تم الحل");
        detailsResult.Value.Resolution.Should().NotBeNull();
        detailsResult.Value.Resolution!.Notes.Should().Be("تم التواصل مع العميل وتسليم الوجبات بنجاح");
    }

    [Fact]
    public async Task ResolveSupportIssue_Fails_When_Notes_Invalid()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم دايت كير",
            LegalNameEn = "Diet Care Restaurant",
            TradeNameAr = "مطعم دايت كير",
            TradeNameEn = "Diet Care Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var complaint = new Complaint
        {
            RestaurantId = restaurant.Id,
            CustomerUserId = Guid.NewGuid(),
            Title = "مشكلة تسليم البوكس",
            Description = "تأخير في الرد",
            Category = "DeliveryIssue",
            Status = ComplaintStatus.Submitted
        };
        db.Complaints.Add(complaint);
        await db.SaveChangesAsync();

        var auditLogger = Substitute.For<IAuditLogger>();
        var messages = new ArabicTestMessageProvider();
        var handler = new ResolveSupportIssueCommandHandler(db, messages, auditLogger);

        // Act - empty notes
        var resultEmpty = await handler.Handle(new ResolveSupportIssueCommand(complaint.Id, "   "), CancellationToken.None);
        // Act - less than 3 chars
        var resultShort = await handler.Handle(new ResolveSupportIssueCommand(complaint.Id, "تم"), CancellationToken.None);

        // Assert
        resultEmpty.IsFailure.Should().BeTrue();
        resultEmpty.Error.Code.Should().Be("NOTES_INVALID");
        resultShort.IsFailure.Should().BeTrue();
        resultShort.Error.Code.Should().Be("NOTES_INVALID");
    }

    [Fact]
    public async Task ResolveSupportIssue_Returns_Conflict_When_Already_Resolved()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم دايت كير",
            LegalNameEn = "Diet Care Restaurant",
            TradeNameAr = "مطعم دايت كير",
            TradeNameEn = "Diet Care Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var complaint = new Complaint
        {
            RestaurantId = restaurant.Id,
            CustomerUserId = Guid.NewGuid(),
            Title = "مشكلة تسليم البوكس",
            Description = "تأخير في الرد",
            Category = "DeliveryIssue",
            Status = ComplaintStatus.Resolved // already resolved
        };
        db.Complaints.Add(complaint);
        await db.SaveChangesAsync();

        var auditLogger = Substitute.For<IAuditLogger>();
        var messages = new ArabicTestMessageProvider();
        var handler = new ResolveSupportIssueCommandHandler(db, messages, auditLogger);

        // Act
        var result = await handler.Handle(
            new ResolveSupportIssueCommand(complaint.Id, "محاولة حل ثانية"),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ISSUE_ALREADY_RESOLVED");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task SupportIssuesSeeder_Populates_Database_And_Matches_Flutter_Capital_Query()
    {
        // Arrange
        await using var db = CreateDb();
        var messages = new ArabicTestMessageProvider();
        var currentUser = Substitute.For<ICurrentUser>();

        // Act - 1. Run Seeder
        await MealMate.Persistence.Seeding.SupportIssuesSeeder.SeedAsync(db, NullLogger.Instance);

        // Assert - 2. Database contains seeded data
        var complaints = await db.Complaints.ToListAsync();
        complaints.Should().NotBeEmpty();
        complaints.Count.Should().BeGreaterOrEqualTo(12);

        var capitalComplaints = complaints.Where(c => c.Title.Contains("العاصمة")).ToList();
        capitalComplaints.Should().NotBeEmpty();

        var drivers = await db.DriverProfiles.ToListAsync();
        drivers.Should().NotBeEmpty();

        var stops = await db.TripStops.ToListAsync();
        stops.Should().NotBeEmpty();

        // Act - 3. Execute exact query requested by Flutter client:
        // ?area=capital&status=Open&datePreset=Last7Days&pageNumber=1&pageSize=20
        var handler = new GetDispatcherSupportIssuesQueryHandler(db, currentUser, messages);
        var query = new GetDispatcherSupportIssuesQuery(
            RestaurantId: null,
            Status: "Open",
            Area: "capital",
            DatePreset: "Last7Days",
            PageNumber: 1,
            PageSize: 20);

        var queryResult = await handler.Handle(query, CancellationToken.None);

        // Assert - 4. Query returns the seeded Capital Open issues
        queryResult.IsSuccess.Should().BeTrue();
        var response = queryResult.Value;

        response.Issues.Should().NotBeEmpty();
        response.Issues.Should().OnlyContain(i => i.Status == "Open");
        response.Issues.Should().OnlyContain(i => i.Area.Contains("العاصمة") || i.Area.Equals("Capital", StringComparison.OrdinalIgnoreCase));

        // Verify chips and counters
        response.Counters.OpenCount.Should().BeGreaterThan(0);
        response.AreaChips.Should().Contain(c => c.AreaKey == "capital" && c.Count > 0);
        response.AreaChips.Should().Contain(c => c.AreaKey == "salmiya" && c.Count > 0);
        response.AreaChips.Should().Contain(c => c.AreaKey == "hawally" && c.Count > 0);
        response.AreaChips.Should().Contain(c => c.AreaKey == "farwaniya" && c.Count > 0);

        // Act - 5. Fetch Problem Details for one of the seeded issues
        var firstIssue = response.Issues.First();
        var detailsHandler = new GetDispatcherProblemDetailsQueryHandler(db, messages);
        var detailsResult = await detailsHandler.Handle(new GetDispatcherProblemDetailsQuery(firstIssue.Id), CancellationToken.None);

        // Assert - 6. Problem details are fully populated
        detailsResult.IsSuccess.Should().BeTrue();
        var details = detailsResult.Value;
        details.IssueId.Should().Be(firstIssue.Id);
        details.Status.Should().Be("Open");
        details.Metadata.Area.Should().Contain("العاصمة");
        details.Driver.Should().NotBeNull();
        details.Driver.FullName.Should().NotBeNullOrWhiteSpace();
        details.VehicleInfo.Should().NotBeNullOrWhiteSpace();
        details.TripInfo.DeliveryAddress.Should().Contain("العاصمة");
        details.EvidencePhotos.Should().NotBeEmpty();
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


