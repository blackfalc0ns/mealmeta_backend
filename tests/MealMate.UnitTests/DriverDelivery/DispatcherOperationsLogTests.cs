using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class DispatcherOperationsLogTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static TripStop CreateStop(
        string orderCode,
        string customerName,
        string zoneAr,
        string zoneEn,
        TripStopStatus status,
        DateTime? deliveredAtUtc = null,
        string? failureReason = null)
    {
        return new TripStop
        {
            OrderCode = orderCode,
            CustomerMaskedId = "CUST-***" + (Math.Abs(orderCode.GetHashCode()) % 900 + 100),
            CustomerName = customerName,
            CustomerPhone = "+96551234567",
            ZoneNameAr = zoneAr,
            ZoneNameEn = zoneEn,
            MealSummaryAr = "وجبة متوازنة",
            MealSummaryEn = "Balanced Meal",
            SlotLabel = "الغداء",
            Status = status,
            DeliveredAtUtc = deliveredAtUtc,
            FailureReason = failureReason
        };
    }

    private static DriverAssignment CreateAssignment(Guid driverId, DateTime assignedAtUtc, string status = "Active")
    {
        return new DriverAssignment
        {
            DriverProfileId = driverId,
            AssignedAtUtc = assignedAtUtc,
            Status = status
        };
    }

    [Fact]
    public async Task GetOperationsLog_Counters_Are_Isolated_From_StatusFilter()
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

        var driver1 = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "أحمد السعيد",
            FullNameEn = "Ahmed Al-Saeed",
            NationalId = "290010101231",
            LicenseNumber = "DR-101",
            IsAvailable = true
        };
        var driver2 = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "شهد المطيري",
            FullNameEn = "Shahad Al-Mutairi",
            NationalId = "290010101232",
            LicenseNumber = "DR-102",
            IsAvailable = true
        };
        db.DriverProfiles.AddRange(driver1, driver2);

        var now = DateTime.UtcNow;

        // 1. Completed trip stop
        var trip1 = new Trip
        {
            RestaurantId = restaurant.Id,
            Code = "TRP-001",
            Status = TripStatus.Completed,
            PlannedDate = DateOnly.FromDateTime(now)
        };
        trip1.DriverAssignments.Add(CreateAssignment(driver1.Id, now.AddHours(-3)));
        trip1.Stops.Add(CreateStop("BX-10256", "أحمد العتيبي", "حي الياسمين، الرياض", "Al-Yasmin, Riyadh", TripStopStatus.Delivered, now.AddMinutes(-30)));

        // 2. Failed trip stop
        var trip2 = new Trip
        {
            RestaurantId = restaurant.Id,
            Code = "TRP-002",
            Status = TripStatus.Completed,
            PlannedDate = DateOnly.FromDateTime(now)
        };
        trip2.DriverAssignments.Add(CreateAssignment(driver1.Id, now.AddHours(-2)));
        trip2.Stops.Add(CreateStop("BX-10257", "محمد العنزي", "السالمية", "Salmiya", TripStopStatus.Failed, now.AddMinutes(-20), "العميل لا يجيب"));

        // 3. Cancelled trip stop
        var trip3 = new Trip
        {
            RestaurantId = restaurant.Id,
            Code = "TRP-003",
            Status = TripStatus.Cancelled,
            PlannedDate = DateOnly.FromDateTime(now)
        };
        trip3.Stops.Add(CreateStop("BX-10258", "سالم الدوسري", "حولي", "Hawally", TripStopStatus.Pending));

        // 4. Reassigned trip stop (two driver assignments)
        var trip4 = new Trip
        {
            RestaurantId = restaurant.Id,
            Code = "TRP-004",
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(now)
        };
        trip4.DriverAssignments.Add(CreateAssignment(driver1.Id, now.AddHours(-4)));
        trip4.DriverAssignments.Add(CreateAssignment(driver2.Id, now.AddMinutes(-10)));
        trip4.Stops.Add(CreateStop("BX-10259", "عبدالله الشهري", "الفروانية", "Farwaniya", TripStopStatus.PickedUp));

        db.Trips.AddRange(trip1, trip2, trip3, trip4);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);
        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherOperationsLogQueryHandler(db, currentUser, messages);

        // Act: Filter by Status = "Completed"
        var query = new GetDispatcherOperationsLogQuery(
            RestaurantId: restaurant.Id,
            Status: "Completed",
            PageNumber: 1,
            PageSize: 10);

        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Counters must reflect all 4 statuses
        result.Value.Counters.AllCount.Should().Be(4);
        result.Value.Counters.CompletedCount.Should().Be(1);
        result.Value.Counters.FailedCount.Should().Be(1);
        result.Value.Counters.CancelledCount.Should().Be(1);
        result.Value.Counters.ReassignedCount.Should().Be(1);

        // Operations list must only contain the filtered Completed item
        result.Value.Operations.Should().HaveCount(1);
        result.Value.Operations.Single().Type.Should().Be("Completed");
        result.Value.Operations.Single().BoxCode.Should().Be("#BX-10256");
        result.Value.Operations.Single().Driver!.Name.Should().Be("أحمد السعيد");
    }

    [Fact]
    public async Task GetOperationsLog_Search_Filters_Across_Box_Driver_And_Customer()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم تجريبي",
            LegalNameEn = "Test Restaurant",
            TradeNameAr = "مطعم تجريبي",
            TradeNameEn = "Test Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "فهد المطوع",
            FullNameEn = "Fahad Al-Mutawa",
            NationalId = "290010101233",
            LicenseNumber = "DR-103",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var now = DateTime.UtcNow;
        var trip = new Trip
        {
            RestaurantId = restaurant.Id,
            Code = "TRP-100",
            Status = TripStatus.Completed,
            PlannedDate = DateOnly.FromDateTime(now)
        };
        trip.DriverAssignments.Add(CreateAssignment(driver.Id, now));
        trip.Stops.Add(CreateStop("BX-998811", "خالد الراشد", "اليرموك", "Yarmouk", TripStopStatus.Delivered, now));
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);
        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherOperationsLogQueryHandler(db, currentUser, messages);

        // Act & Assert 1: Search by BoxCode
        var searchBox = await handler.Handle(new GetDispatcherOperationsLogQuery(RestaurantId: restaurant.Id, Search: "998811"), CancellationToken.None);
        searchBox.IsSuccess.Should().BeTrue();
        searchBox.Value.Operations.Should().HaveCount(1);

        // Act & Assert 2: Search by Driver Name
        var searchDriver = await handler.Handle(new GetDispatcherOperationsLogQuery(RestaurantId: restaurant.Id, Search: "المطوع"), CancellationToken.None);
        searchDriver.IsSuccess.Should().BeTrue();
        searchDriver.Value.Operations.Should().HaveCount(1);

        // Act & Assert 3: Search by Customer Name
        var searchCust = await handler.Handle(new GetDispatcherOperationsLogQuery(RestaurantId: restaurant.Id, Search: "الراشد"), CancellationToken.None);
        searchCust.IsSuccess.Should().BeTrue();
        searchCust.Value.Operations.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetOperationsLog_Pagination_And_Empty_State_Handled_Correctly()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم تجريبي",
            LegalNameEn = "Test Restaurant",
            TradeNameAr = "مطعم تجريبي",
            TradeNameEn = "Test Restaurant",
            UserId = Guid.NewGuid()
        };
        db.RestaurantProfiles.Add(restaurant);

        var now = DateTime.UtcNow;
        var trip = new Trip
        {
            RestaurantId = restaurant.Id,
            Code = "TRP-200",
            Status = TripStatus.Completed,
            PlannedDate = DateOnly.FromDateTime(now)
        };

        for (int i = 1; i <= 5; i++)
        {
            trip.Stops.Add(CreateStop($"BX-2000{i}", $"عميل {i}", "السالمية", "Salmiya", TripStopStatus.Delivered, now.AddMinutes(-i)));
        }
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);
        var messages = new ArabicTestMessageProvider();
        var handler = new GetDispatcherOperationsLogQueryHandler(db, currentUser, messages);

        // Test Pagination: pageSize = 2, pageNumber = 1
        var pagedResult = await handler.Handle(new GetDispatcherOperationsLogQuery(
            RestaurantId: restaurant.Id,
            PageNumber: 1,
            PageSize: 2), CancellationToken.None);

        pagedResult.IsSuccess.Should().BeTrue();
        pagedResult.Value.Pagination.PageNumber.Should().Be(1);
        pagedResult.Value.Pagination.PageSize.Should().Be(2);
        pagedResult.Value.Pagination.TotalItems.Should().Be(5);
        pagedResult.Value.Pagination.TotalPages.Should().Be(3);
        pagedResult.Value.Pagination.HasPreviousPage.Should().BeFalse();
        pagedResult.Value.Pagination.HasNextPage.Should().BeTrue();
        pagedResult.Value.Operations.Should().HaveCount(2);

        // Test Empty State (e.g. non-existent search query)
        var emptyResult = await handler.Handle(new GetDispatcherOperationsLogQuery(
            RestaurantId: restaurant.Id,
            Search: "NonExistentSearchTerm12345"), CancellationToken.None);

        emptyResult.IsSuccess.Should().BeTrue();
        emptyResult.Value.Operations.Should().BeEmpty();
        emptyResult.Value.Pagination.TotalItems.Should().Be(0);
        emptyResult.Value.Pagination.TotalPages.Should().Be(0);
        emptyResult.Value.Pagination.HasPreviousPage.Should().BeFalse();
        emptyResult.Value.Pagination.HasNextPage.Should().BeFalse();
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
