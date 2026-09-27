# Dispatcher Drivers Screens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build production-grade backend endpoints, CQRS queries/commands, and real-time SignalR notifications for the Dispatcher Drivers List, Driver Details, and Driver Availability Toggle screens in MealMate Backend with zero mock data and strict restaurant isolation.

**Architecture:** Extend existing Dispatcher queries (`GetDispatcherDriverRosterQuery` and `GetDispatcherDriverDetailsQuery`) and add `UpdateDispatcherDriverAvailabilityCommand` with MediatR. Return projected domain data directly from EF Core / PostgreSQL without N+1 queries. Emit real-time availability changes via `DispatcherHub` to restaurant groups (`driver-availability-updated`). Enforce authentication and RBAC (`DeliveryManager`, `Admin`, `SuperAdmin` with `delivery.dispatch.manage` and `delivery.drivers.manage`). Record system audit trails via `IAuditLogger`.

**Tech Stack:** ASP.NET Core 9, MediatR, EF Core (PostgreSQL), SignalR, xUnit, FluentAssertions, NSubstitute.

**Spec:** `docs/superpowers/specs/2026-09-27-dispatcher-drivers-screens-design.md`

## Global Constraints

- Spec: `docs/superpowers/specs/2026-09-27-dispatcher-drivers-screens-design.md`
- Strict Zero Mock Data: No fake, mocked, placeholder, hardcoded, simulated, or fallback business data (no hardcoded phone numbers, no 4.8/4.9 fallback ratings, no fake CDN URLs, no hash driver codes, no dummy distances, no fake static delays).
- If data does not exist, return `null`, empty list, 404, or actual count 0.
- All new and modified operations require authentication (`[Authorize]`) with appropriate role and permission checks (`delivery.dispatch.manage` for reads, `delivery.drivers.manage` for availability updates).
- Strict restaurant isolation: Drivers must belong to the authorized restaurant. Delivery managers cannot access or mutate drivers of another restaurant.
- Setting a driver to unavailable does not cancel or stop active trips or current assignments. The driver finishes active deliveries (`operationalStatus = InDelivery`) but accepts no new assignments.
- Backward compatibility: Retain existing route templates (`drivers/roster`, `drivers/{driverId}/details`) alongside the new canonical routes (`drivers`, `drivers/{driverId}`).
- Database indexes: Existing indexes on `DriverProfiles.RestaurantId`, `DriverAssignments.DriverProfileId`, `DeliveryTrackingPoints(DriverProfileId, RecordedAtUtc)`, `DriverRatings.DriverProfileId`, and `Documents(OwnerType, OwnerId, DocumentType)` are already present in persistence configurations; do not create unnecessary migrations.
- Kuwait timezone: Today's boundaries for statistics must be computed against `Asia/Kuwait` (or Windows `Arab Standard Time`) converted to UTC.

---

## File Structure

### Modified Files
- `src/MealMate.Application/Features/DriverDelivery/Dispatcher/DispatcherDriverRosterDtos.cs`
  - Defines the drivers list response DTOs (`DispatcherDriversResponseDto`, `DriverListItemDto`, `DriverCountsDto`, `DriverPaginationDto`), keeping legacy DTOs for backward compatibility.
- `src/MealMate.Application/Features/DriverDelivery/Dispatcher/GetDispatcherDriverRosterQuery.cs`
  - Replaces fallback and hardcoded logic with real PostgreSQL projections, multi-field search (Arabic/English name, phone, license/code, plate), operational status prioritization (`InDelivery` > `Available` > `Unavailable`), pre-filter counts, and sorting.
- `src/MealMate.Application/Features/DriverDelivery/Dispatcher/DispatcherDriverDetailsDtos.cs`
  - Defines the comprehensive driver details response DTOs (`DispatcherDriverDetailsResponseDto`, `DriverDetailsHeaderDto`, `DriverTodayStatsDto`, `DriverVehicleDto`, `DriverLocationDto`, `DriverPerformanceDto`, `DriverDocumentDto`).
- `src/MealMate.Application/Features/DriverDelivery/Dispatcher/GetDispatcherDriverDetailsQuery.cs`
  - Replaces fake fallback phone, ratings, CDN URLs, and distance formulas with real queries from `DriverProfiles`, `DriverRegistrations`, `DriverRatings`, `DriverAssignments`, `TripStops`, `DeliveryTrackingPoints`, and `Documents`. Uses Kuwait timezone for today's statistics.
- `src/MealMate.Api/Realtime/DispatcherHub.cs`
  - Adds the `driver-availability-updated` event constant.
- `src/MealMate.Api/Controllers/V1/DispatcherDashboardController.cs`
  - Adds canonical routes (`GET /api/v1/dispatcher/drivers`, `GET /api/v1/dispatcher/drivers/{driverId}`, `PATCH /api/v1/dispatcher/drivers/{driverId}/availability`) while preserving existing route templates. Enforces `[Authorize]` and restaurant scoping.
- `tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs`
  - Adds unit test coverage for list filtering, status priority, counts, search, details aggregation, Kuwait timezone boundaries, document status calculation, and availability command.
- `tests/MealMate.ApiTests/Controllers/DispatcherDashboardControllerTests.cs`
  - Adds action and route attribute assertions for the canonical and legacy routes.

### Created Files
- `src/MealMate.Application/Features/DriverDelivery/Dispatcher/UpdateDispatcherDriverAvailabilityCommand.cs`
  - MediatR command and handler for toggling `IsAvailable`, checking active assignments, writing `AuditEntry`, and returning the updated operational state.
- `tests/MealMate.IntegrationTests/DispatcherDriversControllerTests.cs`
  - Integration tests verifying authorization (401/403), restaurant scoping (404 for foreign restaurant), JSON contracts, and availability persistence.

---

### Task 1: Update DTOs for Drivers List and Driver Details

**Files:**
- Modify: `src/MealMate.Application/Features/DriverDelivery/Dispatcher/DispatcherDriverRosterDtos.cs`
- Modify: `src/MealMate.Application/Features/DriverDelivery/Dispatcher/DispatcherDriverDetailsDtos.cs`

**Interfaces:**
- Produces: `DispatcherDriversResponseDto`, `DriverCountsDto`, `DriverListItemDto`, `DriverPaginationDto`
- Produces: `DispatcherDriverDetailsResponseDto`, `DriverDetailsHeaderDto`, `DriverTodayStatsDto`, `DriverVehicleDto`, `DriverLocationDto`, `DriverPerformanceDto`, `DriverDocumentDto`
- Consumes: `MealMate.Domain.Enums.VerificationStatus`

- [ ] **Step 1: Write the failing test for DTO contract serialization**

Create a test in `tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs`:
```csharp
[Fact]
public void DriversListResponseDto_Matches_Expected_Spec_Contract()
{
    var counts = new DriverCountsDto(Total: 10, Available: 5, InDelivery: 3, Unavailable: 2);
    var item = new DriverListItemDto(
        DriverId: Guid.NewGuid(),
        DriverCode: "DRV-100",
        FullName: "خالد ناصر",
        PhoneNumber: "+96590001122",
        AvatarStorageKey: "avatars/d1.jpg",
        IsAvailable: true,
        OperationalStatus: "Available",
        Rating: 4.75m,
        RatingsCount: 12,
        VehicleType: "Motorcycle",
        VehicleModel: "Honda 2024",
        VehiclePlate: "12/3456");

    var pagination = new DriverPaginationDto(
        PageNumber: 1,
        PageSize: 20,
        TotalItems: 1,
        TotalPages: 1,
        HasPreviousPage: false,
        HasNextPage: false);

    var response = new DispatcherDriversResponseDto(counts, new[] { item }, pagination);

    response.Counts.Total.Should().Be(10);
    response.Items.Should().HaveCount(1);
    response.Items[0].FullName.Should().Be("خالد ناصر");
    response.Pagination.PageSize.Should().Be(20);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriversListResponseDto_Matches_Expected_Spec_Contract"
```
Expected: FAIL due to missing DTO type `DispatcherDriversResponseDto`.

- [ ] **Step 3: Implement the new DTO contracts**

In `src/MealMate.Application/Features/DriverDelivery/Dispatcher/DispatcherDriverRosterDtos.cs`:
```csharp
namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record DispatcherDriversResponseDto(
    DriverCountsDto Counts,
    IReadOnlyList<DriverListItemDto> Items,
    DriverPaginationDto Pagination);

public sealed record DriverCountsDto(
    int Total,
    int Available,
    int InDelivery,
    int Unavailable);

public sealed record DriverListItemDto(
    Guid DriverId,
    string? DriverCode,
    string FullName,
    string? PhoneNumber,
    string? AvatarStorageKey,
    bool IsAvailable,
    string OperationalStatus,
    decimal? Rating,
    int RatingsCount,
    string? VehicleType,
    string? VehicleModel,
    string? VehiclePlate);

public sealed record DriverPaginationDto(
    int PageNumber,
    int PageSize,
    int TotalItems,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

// Retain legacy DTOs below for backward-compatibility with existing roster callers:
public sealed record DispatcherDriverRosterDto(
    Guid? BoxId,
    DriverRosterCountsDto Counts,
    string SelectedView,
    string SelectedArea,
    string SectionTitle,
    IReadOnlyList<DriverRosterAreaChipDto> Areas,
    IReadOnlyList<DriverRosterItemDto> Drivers);

public sealed record DriverRosterCountsDto(
    int TotalCount,
    int AvailableCount,
    int BusyCount);

public sealed record DriverRosterAreaChipDto(
    string AreaKey,
    string Name,
    int DriverCount,
    bool IsSelected);

public sealed record DriverRosterItemDto(
    Guid DriverId,
    string DriverCode,
    string FullName,
    string? AvatarUrl,
    decimal Rating,
    string Status,
    string StatusText,
    string StatusDotColor,
    bool IsAvailableForSelection,
    string ActionText,
    bool ActionEnabled,
    int ActiveOrdersCount,
    string ActiveOrdersText,
    int CompletedOrdersTodayCount,
    string CompletedOrdersTodayText,
    double DistanceKm,
    string DistanceText,
    string CurrentZoneName);
```

In `src/MealMate.Application/Features/DriverDelivery/Dispatcher/DispatcherDriverDetailsDtos.cs`:
```csharp
namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record DispatcherDriverDetailsResponseDto(
    DriverDetailsHeaderDto Driver,
    DriverTodayStatsDto Today,
    DriverVehicleDto Vehicle,
    DriverLocationDto CurrentLocation,
    DriverPerformanceDto Performance,
    IReadOnlyList<DriverDocumentDto> Documents);

public sealed record DriverDetailsHeaderDto(
    Guid DriverId,
    string? DriverCode,
    string FullName,
    string? PhoneNumber,
    string? AvatarStorageKey,
    bool IsAvailable,
    string OperationalStatus,
    DateTime? LastLocationAtUtc);

public sealed record DriverTodayStatsDto(
    int CompletedDeliveries,
    int ActiveDeliveries,
    int FailedDeliveries,
    double DistanceKm);

public sealed record DriverVehicleDto(
    string? Type,
    string? Model,
    int? Year,
    string? Color,
    string? PlateNumber);

public sealed record DriverLocationDto(
    decimal? Latitude,
    decimal? Longitude,
    DateTime? RecordedAtUtc,
    double? Heading,
    double? SpeedKmh);

public sealed record DriverPerformanceDto(
    int TotalDelivered,
    decimal? AverageRating,
    int RatingsCount,
    double? OnTimeRatePercent,
    int ViolationsCount);

public sealed record DriverDocumentDto(
    string Type,
    string Status,
    string VerificationStatus,
    DateTime? ExpiresAtUtc);

// Retain legacy DTOs below for backward-compatibility with existing details callers:
public sealed record DriverHeaderDto(
    Guid DriverId,
    string DriverCode,
    string FullName,
    string? PhoneNumber,
    string? AvatarUrl,
    string Status,
    string StatusText,
    string StatusDotColor,
    string LastUpdatedText);

public sealed record DriverKpisDto(
    decimal Rating,
    int AvgDelayMinutes,
    int CompletedTodayCount,
    int ActiveBoxesCount);

public sealed record DriverDailySummaryDto(
    int ApproxDistanceKm,
    int AvgDelayMinutes,
    int FailedDeliveriesCount,
    int CompletedDeliveriesCount);

public sealed record DispatcherDriverDetailsDto(
    DriverHeaderDto Driver,
    DriverKpisDto Kpis,
    DriverDailySummaryDto DailySummary);

public sealed record DriverActiveBoxDto(
    Guid BoxId,
    string BoxCode,
    string Status,
    string StatusText,
    string StatusColor,
    string CustomerName,
    string ScheduledTimeText,
    string DeliveryAddress);

public sealed record DriverActiveBoxesResponseDto(
    Guid DriverId,
    int TotalCount,
    IReadOnlyList<DriverActiveBoxDto> Boxes);

public sealed record DriverCurrentLocationDto(
    Guid DriverId,
    decimal? Latitude,
    decimal? Longitude,
    double? Heading,
    double? SpeedKmh,
    string StatusBadge,
    string TimeAgoText,
    string? StreetAddress,
    string? AreaCity,
    decimal? DestinationLatitude,
    decimal? DestinationLongitude,
    string? RoutePolyline = null);
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriversListResponseDto_Matches_Expected_Spec_Contract"
```
Expected: PASS.

- [ ] **Step 5: Commit DTO updates**

```bash
git add src/MealMate.Application/Features/DriverDelivery/Dispatcher/DispatcherDriverRosterDtos.cs src/MealMate.Application/Features/DriverDelivery/Dispatcher/DispatcherDriverDetailsDtos.cs tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs
git commit -m "feat(dispatcher): add clean DTO contracts for drivers list and details screens"
```

---

### Task 2: Implement Driver Availability Toggle Command & Audit

**Files:**
- Create: `src/MealMate.Application/Features/DriverDelivery/Dispatcher/UpdateDispatcherDriverAvailabilityCommand.cs`
- Modify: `tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs`

**Interfaces:**
- Produces: `UpdateDispatcherDriverAvailabilityCommand`, `UpdateDispatcherDriverAvailabilityResponseDto`
- Consumes: `IAppDbContext`, `ICurrentUser`, `IAuditLogger`, `ILocalizedMessageProvider`

- [ ] **Step 1: Write the failing tests for availability command**

In `tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs`:
```csharp
[Fact]
public async Task UpdateAvailability_TogglingToUnavailable_WithoutActiveTrip_SetsUnavailable()
{
    await using var db = CreateDb();
    var restaurantId = Guid.NewGuid();
    var driver = new DriverProfile
    {
        RestaurantId = restaurantId,
        FullNameAr = "محمد عبد الله",
        FullNameEn = "Mohammed Abdullah",
        IsAvailable = true
    };
    db.DriverProfiles.Add(driver);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.RestaurantId.Returns(restaurantId);
    currentUser.HasPermission(PermissionCodes.DeliveryDriversManage).Returns(true);

    var auditLogger = Substitute.For<IAuditLogger>();
    var messages = NullLocalizedMessageProvider.Instance;
    var handler = new UpdateDispatcherDriverAvailabilityCommandHandler(db, currentUser, auditLogger, messages);

    var result = await handler.Handle(new UpdateDispatcherDriverAvailabilityCommand(driver.Id, false, "Shift ended"), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.DriverId.Should().Be(driver.Id);
    result.Value.IsAvailable.Should().BeFalse();
    result.Value.OperationalStatus.Should().Be("Unavailable");
    result.Value.HasActiveAssignments.Should().BeFalse();

    var updated = await db.DriverProfiles.FindAsync(driver.Id);
    updated!.IsAvailable.Should().BeFalse();

    await auditLogger.Received(1).LogAsync(Arg.Is<AuditEntry>(e =>
        e.EntityType == nameof(DriverProfile) &&
        e.EntityId == driver.Id &&
        e.SourceChannel == "DispatcherApp" &&
        e.Reason == "Shift ended"), Arg.Any<CancellationToken>());
}

[Fact]
public async Task UpdateAvailability_TogglingToUnavailable_WithActiveTrip_SetsInDelivery()
{
    await using var db = CreateDb();
    var restaurantId = Guid.NewGuid();
    var driver = new DriverProfile
    {
        RestaurantId = restaurantId,
        FullNameAr = "خالد حسن",
        FullNameEn = "Khaled Hassan",
        IsAvailable = true
    };
    db.DriverProfiles.Add(driver);

    var trip = new Trip
    {
        RestaurantId = restaurantId,
        Code = "TRP-99",
        Status = TripStatus.InProgress,
        PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
    };
    trip.DriverAssignments.Add(new DriverAssignment
    {
        DriverProfileId = driver.Id,
        Status = "Active",
        AssignedAtUtc = DateTime.UtcNow
    });
    trip.Stops.Add(new TripStop
    {
        OrderCode = "BX-01",
        CustomerMaskedId = "C1",
        ZoneNameAr = "السالمية",
        ZoneNameEn = "Salmiya",
        MealSummaryAr = "غداء",
        MealSummaryEn = "Lunch",
        SlotLabel = "1",
        Status = TripStopStatus.PickedUp
    });
    db.Trips.Add(trip);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.RestaurantId.Returns(restaurantId);
    currentUser.HasPermission(PermissionCodes.DeliveryDriversManage).Returns(true);

    var auditLogger = Substitute.For<IAuditLogger>();
    var messages = NullLocalizedMessageProvider.Instance;
    var handler = new UpdateDispatcherDriverAvailabilityCommandHandler(db, currentUser, auditLogger, messages);

    var result = await handler.Handle(new UpdateDispatcherDriverAvailabilityCommand(driver.Id, false, null), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.IsAvailable.Should().BeFalse();
    result.Value.OperationalStatus.Should().Be("InDelivery");
    result.Value.HasActiveAssignments.Should().BeTrue();

    // Verify trip was NOT modified
    var unmodifiedTrip = await db.Trips.FindAsync(trip.Id);
    unmodifiedTrip!.Status.Should().Be(TripStatus.InProgress);
}

[Fact]
public async Task UpdateAvailability_CrossRestaurant_Returns_NotFound()
{
    await using var db = CreateDb();
    var driver = new DriverProfile
    {
        RestaurantId = Guid.NewGuid(),
        FullNameAr = "سائق آخر",
        FullNameEn = "Other Driver",
        IsAvailable = true
    };
    db.DriverProfiles.Add(driver);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.RestaurantId.Returns(Guid.NewGuid()); // Different restaurant
    currentUser.HasPermission(PermissionCodes.DeliveryDriversManage).Returns(true);

    var auditLogger = Substitute.For<IAuditLogger>();
    var messages = NullLocalizedMessageProvider.Instance;
    var handler = new UpdateDispatcherDriverAvailabilityCommandHandler(db, currentUser, auditLogger, messages);

    var result = await handler.Handle(new UpdateDispatcherDriverAvailabilityCommand(driver.Id, false, null), CancellationToken.None);

    result.IsFailure.Should().BeTrue();
    result.Error.Code.Should().Be("DRIVER_NOT_FOUND");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~UpdateAvailability"
```
Expected: FAIL with compilation error: `UpdateDispatcherDriverAvailabilityCommand` does not exist.

- [ ] **Step 3: Implement `UpdateDispatcherDriverAvailabilityCommand`**

Create `src/MealMate.Application/Features/DriverDelivery/Dispatcher/UpdateDispatcherDriverAvailabilityCommand.cs`:
```csharp
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record UpdateDispatcherDriverAvailabilityCommand(
    Guid DriverId,
    bool IsAvailable,
    string? Reason = null) : ICommand<UpdateDispatcherDriverAvailabilityResponseDto>;

public sealed record UpdateDispatcherDriverAvailabilityResponseDto(
    Guid DriverId,
    bool IsAvailable,
    string OperationalStatus,
    bool HasActiveAssignments,
    DateTime UpdatedAtUtc);

public sealed class UpdateDispatcherDriverAvailabilityCommandHandler
    : ICommandHandler<UpdateDispatcherDriverAvailabilityCommand, UpdateDispatcherDriverAvailabilityResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly ILocalizedMessageProvider _messages;

    public UpdateDispatcherDriverAvailabilityCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _messages = messages;
    }

    public async Task<Result<UpdateDispatcherDriverAvailabilityResponseDto>> Handle(
        UpdateDispatcherDriverAvailabilityCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var isDeliveryManager = _currentUser.IsInRole(AppRoles.DeliveryManager) ||
                                _currentUser.IsInRole(AppRoles.RestaurantOwner) ||
                                _currentUser.IsInRole(AppRoles.SuperAdmin) ||
                                _currentUser.IsInRole(AppRoles.OperationsAdmin);

        if (!isDeliveryManager && !_currentUser.HasPermission(PermissionCodes.DeliveryDriversManage))
        {
            return Error.Forbidden(
                "DRIVER_ACCESS_DENIED",
                _messages.GetMessage("DRIVER_ACCESS_DENIED", "You do not have permission to manage driver availability."));
        }

        var restaurantId = _currentUser.RestaurantId;

        // Query driver within the authorized restaurant boundary
        var query = _db.DriverProfiles.Where(d => d.Id == request.DriverId);
        if (restaurantId.HasValue && restaurantId.Value != Guid.Empty)
        {
            query = query.Where(d => d.RestaurantId == restaurantId.Value);
        }

        var driver = await query.FirstOrDefaultAsync(cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(
                "DRIVER_NOT_FOUND",
                _messages.GetMessage("DRIVER_NOT_FOUND", "Driver was not found in the authorized restaurant."));
        }

        // Check for active assignments & active trip stops
        var hasActiveAssignments = await _db.DriverAssignments
            .AsNoTracking()
            .Where(da => da.DriverProfileId == driver.Id &&
                         (da.Trip.Status == TripStatus.Assigned || da.Trip.Status == TripStatus.InProgress) &&
                         da.Trip.Stops.Any(s => s.Status == TripStopStatus.Pending || s.Status == TripStopStatus.PickedUp))
            .AnyAsync(cancellationToken);

        var previousAvailable = driver.IsAvailable;
        var nowUtc = DateTime.UtcNow;

        // If idempotent (no change), return current state without creating duplicate audit entry
        if (previousAvailable == request.IsAvailable)
        {
            var opStatusCurrent = hasActiveAssignments
                ? "InDelivery"
                : (driver.IsAvailable ? "Available" : "Unavailable");

            return Result.Success(new UpdateDispatcherDriverAvailabilityResponseDto(
                DriverId: driver.Id,
                IsAvailable: driver.IsAvailable,
                OperationalStatus: opStatusCurrent,
                HasActiveAssignments: hasActiveAssignments,
                UpdatedAtUtc: driver.ModifiedAtUtc ?? nowUtc));
        }

        // Apply state change
        driver.IsAvailable = request.IsAvailable;
        driver.ModifiedAtUtc = nowUtc;

        // Write append-only system audit log
        await _audit.LogAsync(new AuditEntry(
            Action: "UpdateAvailability",
            EntityType: nameof(DriverProfile),
            EntityId: driver.Id,
            Before: new { isAvailable = previousAvailable },
            After: new { isAvailable = request.IsAvailable },
            Reason: request.Reason,
            SourceChannel: "DispatcherApp"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var operationalStatus = hasActiveAssignments
            ? "InDelivery"
            : (driver.IsAvailable ? "Available" : "Unavailable");

        return Result.Success(new UpdateDispatcherDriverAvailabilityResponseDto(
            DriverId: driver.Id,
            IsAvailable: driver.IsAvailable,
            OperationalStatus: operationalStatus,
            HasActiveAssignments: hasActiveAssignments,
            UpdatedAtUtc: nowUtc));
    }
}
```

- [x] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~UpdateAvailability"
```
Expected: PASS with 3 passing tests.

- [x] **Step 5: Commit availability command**

```bash
git add src/MealMate.Application/Features/DriverDelivery/Dispatcher/UpdateDispatcherDriverAvailabilityCommand.cs tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs
git commit -m "feat(dispatcher): implement driver availability toggle command with audit logging"
```

---

### Task 3: Refactor Drivers List Query (`GetDispatcherDriverRosterQuery`) with Zero Mock Data

**Files:**
- Modify: `src/MealMate.Application/Features/DriverDelivery/Dispatcher/GetDispatcherDriverRosterQuery.cs`
- Modify: `tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs`

**Interfaces:**
- Produces: `DispatcherDriversResponseDto` (and backward-compatible `DispatcherDriverRosterDto` if queried as roster)
- Consumes: `IAppDbContext`, `ICurrentUser`, `ILocalizedMessageProvider`

- [ ] **Step 1: Write the failing tests for drivers list calculation and filtering**

In `tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs`:
```csharp
[Fact]
public async Task GetDrivers_Calculates_Counts_By_Operational_Priority()
{
    await using var db = CreateDb();
    var restaurantId = Guid.NewGuid();

    // Driver 1: Available, no trips
    var driver1 = new DriverProfile
    {
        RestaurantId = restaurantId,
        FullNameAr = "علي الأحمد",
        FullNameEn = "Ali Al-Ahmad",
        IsAvailable = true
    };

    // Driver 2: Unavailable (IsAvailable = false), no trips
    var driver2 = new DriverProfile
    {
        RestaurantId = restaurantId,
        FullNameAr = "بدر المنصور",
        FullNameEn = "Bader Al-Mansoor",
        IsAvailable = false
    };

    // Driver 3: InDelivery (IsAvailable = false, but active trip in progress)
    var driver3 = new DriverProfile
    {
        RestaurantId = restaurantId,
        FullNameAr = "عمر الشمري",
        FullNameEn = "Omar Al-Shammari",
        IsAvailable = false
    };

    db.DriverProfiles.AddRange(driver1, driver2, driver3);

    var trip = new Trip
    {
        RestaurantId = restaurantId,
        Code = "TRP-101",
        Status = TripStatus.InProgress,
        PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
    };
    trip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = driver3.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow });
    trip.Stops.Add(new TripStop { OrderCode = "B-1", CustomerMaskedId = "C1", ZoneNameAr = "حولي", ZoneNameEn = "Hawally", MealSummaryAr = "م", MealSummaryEn = "M", SlotLabel = "1", Status = TripStopStatus.PickedUp });
    db.Trips.Add(trip);

    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.RestaurantId.Returns(restaurantId);
    currentUser.HasPermission(PermissionCodes.DeliveryDispatchManage).Returns(true);

    var messages = NullLocalizedMessageProvider.Instance;
    var handler = new GetDispatcherDriversQueryHandler(db, currentUser, messages);

    var result = await handler.Handle(new GetDispatcherDriversQuery(restaurantId), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.Counts.Total.Should().Be(3);
    result.Value.Counts.Available.Should().Be(1);
    result.Value.Counts.InDelivery.Should().Be(1);
    result.Value.Counts.Unavailable.Should().Be(1);

    var d3Item = result.Value.Items.First(i => i.DriverId == driver3.Id);
    d3Item.OperationalStatus.Should().Be("InDelivery");
    d3Item.IsAvailable.Should().BeFalse();
}

[Fact]
public async Task GetDrivers_WhenNoDrivers_Returns_ZeroCounts_And_EmptyList_Without_MockData()
{
    await using var db = CreateDb();
    var restaurantId = Guid.NewGuid();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.RestaurantId.Returns(restaurantId);
    currentUser.HasPermission(PermissionCodes.DeliveryDispatchManage).Returns(true);

    var messages = NullLocalizedMessageProvider.Instance;
    var handler = new GetDispatcherDriversQueryHandler(db, currentUser, messages);

    var result = await handler.Handle(new GetDispatcherDriversQuery(restaurantId), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.Counts.Total.Should().Be(0);
    result.Value.Counts.Available.Should().Be(0);
    result.Value.Counts.InDelivery.Should().Be(0);
    result.Value.Counts.Unavailable.Should().Be(0);
    result.Value.Items.Should().BeEmpty();
    result.Value.Pagination.TotalItems.Should().Be(0);
}
```

- [x] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~GetDrivers_"
```
Expected: FAIL because `GetDispatcherDriversQuery` / `GetDispatcherDriversQueryHandler` is not defined yet.

- [x] **Step 3: Implement `GetDispatcherDriversQuery` and update `GetDispatcherDriverRosterQuery`**

In `src/MealMate.Application/Features/DriverDelivery/Dispatcher/GetDispatcherDriverRosterQuery.cs`:
- Create `GetDispatcherDriversQuery` returning `DispatcherDriversResponseDto`.
- Remove all fake phone numbers (`+965 50 123 4567`), fake CDN links (`https://cdn.mealmate.app/avatars/...`), fake ratings (`4.9m`), fake formulas (`distKm = 4.5 + ...`), and dummy fallback areas (`"السالمية"`).
- Query `DriverProfiles` strictly for the authorized restaurant.
- Read real `DriverRegistration.Phone`, real `DriverRatings` average and count.
- Count active trips via `DriverAssignments` joining `Trip` and active `TripStops`.
- Enforce the 3-tier priority:
  1. `InDelivery` (active trip with pending/picked-up stops)
  2. `Available` (`IsAvailable == true`)
  3. `Unavailable` (`IsAvailable == false`)
- Pre-filter counts calculated on the restaurant set before search and status filtering.
- Multi-field search matching Arabic name, English name, phone number, driver license, vehicle plate.
- Status filter: `All`, `Available`, `InDelivery`, `Unavailable`.
- Sorting: `Name`, `RatingDesc`, `Newest`, `Status`.
- Server-side pagination with metadata (`pageNumber`, `pageSize`, `totalItems`, `totalPages`, `hasPreviousPage`, `hasNextPage`).
- Keep `GetDispatcherDriverRosterQuery` and its handler intact or delegating to the clean query to preserve any existing legacy callers.

- [x] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~GetDrivers_"
```
Expected: PASS.

- [x] **Step 5: Commit drivers list query**

```bash
git add src/MealMate.Application/Features/DriverDelivery/Dispatcher/GetDispatcherDriverRosterQuery.cs tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs
git commit -m "feat(dispatcher): implement clean zero-mock drivers list query with counts and pagination"
```

---

### Task 4: Refactor Driver Details Query (`GetDispatcherDriverDetailsQuery`) with Zero Mock Data

**Files:**
- Modify: `src/MealMate.Application/Features/DriverDelivery/Dispatcher/GetDispatcherDriverDetailsQuery.cs`
- Modify: `tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs`

**Interfaces:**
- Produces: `DispatcherDriverDetailsResponseDto`
- Consumes: `IAppDbContext`, `ICurrentUser`, `ILocalizedMessageProvider`

- [ ] **Step 1: Write the failing tests for driver details query**

In `tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs`:
```csharp
[Fact]
public async Task GetDriverDetails_ZeroMock_Returns_Null_When_Location_And_Ratings_Missing()
{
    await using var db = CreateDb();
    var restaurantId = Guid.NewGuid();
    var driver = new DriverProfile
    {
        RestaurantId = restaurantId,
        FullNameAr = "سامي يوسف",
        FullNameEn = "Sami Yousef",
        IsAvailable = true,
        LicenseExpiryUtc = DateTime.UtcNow.AddDays(45),
        VehicleRegistrationExpiryUtc = DateTime.UtcNow.AddDays(-5),
        VerificationStatus = VerificationStatus.Approved
    };
    db.DriverProfiles.Add(driver);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.RestaurantId.Returns(restaurantId);
    currentUser.HasPermission(PermissionCodes.DeliveryDispatchManage).Returns(true);

    var messages = NullLocalizedMessageProvider.Instance;
    var handler = new GetDispatcherDriverDetailsQueryHandler(db, currentUser, messages);

    var result = await handler.Handle(new GetDispatcherDriverDetailsQuery(driver.Id), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    var details = result.Value;

    // Contact & Avatar: no fake fallbacks
    details.Driver.PhoneNumber.Should().BeNull();
    details.Driver.AvatarStorageKey.Should().BeNull();
    details.Driver.OperationalStatus.Should().Be("Available");
    details.Driver.LastLocationAtUtc.Should().BeNull();

    // Location: null coordinates when no tracking points
    details.CurrentLocation.Latitude.Should().BeNull();
    details.CurrentLocation.Longitude.Should().BeNull();
    details.CurrentLocation.RecordedAtUtc.Should().BeNull();

    // Performance: null rating, zero counts
    details.Performance.AverageRating.Should().BeNull();
    details.Performance.RatingsCount.Should().Be(0);
    details.Performance.TotalDelivered.Should().Be(0);

    // Distance: 0.0 when no points (no order-count multiplier)
    details.Today.DistanceKm.Should().Be(0.0);

    // Documents: ExpiringSoon (45 days is Valid, -5 days is Expired)
    details.Documents.Should().HaveCount(2);
    var license = details.Documents.First(d => d.Type == "DriverLicense");
    license.Status.Should().Be("Valid");
    var reg = details.Documents.First(d => d.Type == "VehicleRegistration");
    reg.Status.Should().Be("Expired");
}

[Fact]
public async Task GetDriverDetails_Aggregates_Real_Ratings_And_TrackingPoints()
{
    await using var db = CreateDb();
    var restaurantId = Guid.NewGuid();
    var driver = new DriverProfile
    {
        RestaurantId = restaurantId,
        FullNameAr = "ناصر الفهد",
        FullNameEn = "Nasser Al-Fahad",
        IsAvailable = true
    };
    db.DriverProfiles.Add(driver);

    // Add 2 real ratings: 5 and 4 -> avg 4.5
    db.DriverRatings.AddRange(
        new DriverRating { DriverProfileId = driver.Id, CustomerUserId = Guid.NewGuid(), Score = 5 },
        new DriverRating { DriverProfileId = driver.Id, CustomerUserId = Guid.NewGuid(), Score = 4 });

    // Add 2 tracking points for today in Kuwait
    var now = DateTime.UtcNow;
    db.DeliveryTrackingPoints.AddRange(
        new DeliveryTrackingPoint { DriverProfileId = driver.Id, Latitude = 29.3375m, Longitude = 48.0760m, RecordedAtUtc = now.AddMinutes(-10), Heading = 90.0, SpeedKmh = 30.0 },
        new DeliveryTrackingPoint { DriverProfileId = driver.Id, Latitude = 29.3400m, Longitude = 48.0800m, RecordedAtUtc = now, Heading = 95.0, SpeedKmh = 25.0 });

    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.RestaurantId.Returns(restaurantId);
    currentUser.HasPermission(PermissionCodes.DeliveryDispatchManage).Returns(true);

    var messages = NullLocalizedMessageProvider.Instance;
    var handler = new GetDispatcherDriverDetailsQueryHandler(db, currentUser, messages);

    var result = await handler.Handle(new GetDispatcherDriverDetailsQuery(driver.Id), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    var details = result.Value;
    details.Performance.AverageRating.Should().Be(4.5m);
    details.Performance.RatingsCount.Should().Be(2);

    details.CurrentLocation.Latitude.Should().Be(29.3400m);
    details.CurrentLocation.Longitude.Should().Be(48.0800m);
    details.Today.DistanceKm.Should().BeGreaterThan(0.0);
}
```

- [x] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~GetDriverDetails_ZeroMock"
```
Expected: FAIL because handler returns legacy DTO with fake phone numbers and fake ratings.

- [x] **Step 3: Implement clean zero-mock `GetDispatcherDriverDetailsQueryHandler`**

In `src/MealMate.Application/Features/DriverDelivery/Dispatcher/GetDispatcherDriverDetailsQuery.cs`:
- Resolve Kuwait timezone using platform-aware search (`Arab Standard Time` or `Asia/Kuwait`).
- Convert current UTC time to Kuwait timezone, compute Kuwait start-of-day and end-of-day in UTC.
- Enforce restaurant isolation (return `DRIVER_NOT_FOUND` if driver profile does not exist in authorized restaurant).
- Load driver profile, vehicle details (`Type`, `Model`, `Year`, `Color`, `Plate`).
- Retrieve real phone from `DriverRegistrations` where `ResultingDriverProfileId == driver.Id || SubmittedByUserId == driver.UserId`. Return `null` if not found (zero mock).
- Retrieve real avatar from `ProfileImageStorageKey` (zero CDN fabrication).
- Determine operational status:
  1. `InDelivery` if driver has active assignment with active stops.
  2. `Available` if `IsAvailable == true`.
  3. `Unavailable` if `IsAvailable == false`.
- Calculate today's deliveries:
  - `completedDeliveries`: count of delivered `TripStops` today (using Kuwait day boundaries in UTC).
  - `activeDeliveries`: count of stops `Pending` or `PickedUp` on active trips.
  - `failedDeliveries`: count of failed stops today.
  - `distanceKm`: sum of Haversine between today's tracking points, or `DriverWorkSession.TotalDistanceKm`. If none, 0.0.
- Calculate performance:
  - `totalDelivered`: lifetime delivered stops for this driver.
  - `averageRating` and `ratingsCount`: from `DriverRatings`. If count == 0, `averageRating = null`.
  - `onTimeRatePercent`: from latest `DriverPerformanceStat.OnTimeRate` if valid, otherwise `null`.
  - `violationsCount`: count of `Complaints` for `driver.Id` where `Status != ComplaintStatus.Rejected`.
- Calculate documents:
  - `DriverLicense` and `VehicleRegistration` evaluated against current time:
    - If expiry is null -> `Missing`
    - If expiry < now -> `Expired`
    - If expiry <= now + 30 days -> `ExpiringSoon`
    - If expiry > now + 30 days -> `Valid`
  - `verificationStatus` from `DriverProfile.VerificationStatus`.

- [x] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~GetDriverDetails_"
```
Expected: PASS.

- [x] **Step 5: Commit driver details query**

```bash
git add src/MealMate.Application/Features/DriverDelivery/Dispatcher/GetDispatcherDriverDetailsQuery.cs tests/MealMate.UnitTests/DriverDelivery/DispatcherDashboardTests.cs
git commit -m "feat(dispatcher): clean driver details query removing all fake data fallbacks"
```

---

### Task 5: SignalR Hub & Controller Endpoints with Security & Authorization

**Files:**
- Modify: `src/MealMate.Api/Realtime/DispatcherHub.cs`
- Modify: `src/MealMate.Api/Controllers/V1/DispatcherDashboardController.cs`
- Modify: `tests/MealMate.ApiTests/Controllers/DispatcherDashboardControllerTests.cs`

**Interfaces:**
- Produces: `DispatcherHub.EventDriverAvailabilityUpdated = "driver-availability-updated"`
- Endpoints:
  - `GET /api/v1/dispatcher/drivers`
  - `GET /api/v1/dispatcher/drivers/roster` (compatible)
  - `GET /api/v1/dispatcher/drivers/{driverId:guid}`
  - `GET /api/v1/dispatcher/drivers/{driverId:guid}/details` (compatible)
  - `PATCH /api/v1/dispatcher/drivers/{driverId:guid}/availability`

- [ ] **Step 1: Write route and attribute tests in `DispatcherDashboardControllerTests.cs`**

In `tests/MealMate.ApiTests/Controllers/DispatcherDashboardControllerTests.cs`:
```csharp
[Fact]
public void DriverAvailabilityMethod_HasPatchAttribute_And_Authorize()
{
    var method = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.UpdateDriverAvailability));
    method.Should().NotBeNull();
    var patch = method!.GetCustomAttribute<HttpPatchAttribute>();
    patch.Should().NotBeNull();
    patch!.Template.Should().Be("drivers/{driverId:guid}/availability");

    var auth = method.GetCustomAttribute<AuthorizeAttribute>();
    auth.Should().NotBeNull();
}

[Fact]
public void DriversListMethod_Supports_Both_Drivers_And_Roster_Routes()
{
    var method = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.GetDrivers));
    method.Should().NotBeNull();
    var gets = method!.GetCustomAttributes<HttpGetAttribute>().ToList();
    gets.Select(g => g.Template).Should().Contain("drivers");
    gets.Select(g => g.Template).Should().Contain("drivers/roster");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter "FullyQualifiedName~DriverAvailabilityMethod"
```
Expected: FAIL because `UpdateDriverAvailability` action does not exist on controller.

- [ ] **Step 3: Implement SignalR event and Controller actions**

In `src/MealMate.Api/Realtime/DispatcherHub.cs`:
```csharp
public const string EventDriverAvailabilityUpdated = "driver-availability-updated";
```

In `src/MealMate.Api/Controllers/V1/DispatcherDashboardController.cs`:
Add/update actions:
```csharp
[HttpGet("drivers")]
[HttpGet("drivers/roster")]
[Authorize]
public async Task<IActionResult> GetDrivers(
    [FromQuery] Guid? restaurantId = null,
    [FromQuery] string? search = null,
    [FromQuery] string? status = "All",
    [FromQuery] string? sortBy = "Name",
    [FromQuery] int pageNumber = 1,
    [FromQuery] int pageSize = 20,
    CancellationToken cancellationToken = default)
{
    var result = await Sender.Send(new GetDispatcherDriversQuery(
        restaurantId, search, status, sortBy, pageNumber, pageSize), cancellationToken);
    return HandleResult(result);
}

[HttpGet("drivers/{driverId:guid}")]
[HttpGet("drivers/{driverId:guid}/details")]
[Authorize]
public async Task<IActionResult> GetDriverDetails(
    [FromRoute] Guid driverId,
    CancellationToken cancellationToken = default)
{
    var result = await Sender.Send(new GetDispatcherDriverDetailsQuery(driverId), cancellationToken);
    return HandleResult(result);
}

[HttpPatch("drivers/{driverId:guid}/availability")]
[Authorize]
public async Task<IActionResult> UpdateDriverAvailability(
    [FromRoute] Guid driverId,
    [FromBody] UpdateDriverAvailabilityRequest request,
    CancellationToken cancellationToken = default)
{
    var result = await Sender.Send(new UpdateDispatcherDriverAvailabilityCommand(
        driverId, request.IsAvailable, request.Reason), cancellationToken);

    if (!result.IsSuccess)
    {
        return HandleResult(result);
    }

    try
    {
        var payload = new
        {
            driverId = result.Value.DriverId,
            isAvailable = result.Value.IsAvailable,
            operationalStatus = result.Value.OperationalStatus,
            hasActiveAssignments = result.Value.HasActiveAssignments,
            updatedAtUtc = result.Value.UpdatedAtUtc
        };

        var restaurantId = CurrentUser.RestaurantId;
        if (restaurantId.HasValue && restaurantId.Value != Guid.Empty)
        {
            var group = DispatcherHub.GroupForRestaurant(restaurantId.Value);
            await _dispatcherHub.Clients.Group(group)
                .SendAsync(DispatcherHub.EventDriverAvailabilityUpdated, payload, cancellationToken);
        }

        await _dispatcherHub.Clients.Group(DispatcherHub.UniversalDispatcherGroup)
            .SendAsync(DispatcherHub.EventDriverAvailabilityUpdated, payload, cancellationToken);
    }
    catch
    {
        // Non-fatal SignalR broadcast
    }

    return HandleResult(result);
}

public sealed record UpdateDriverAvailabilityRequest(
    bool IsAvailable,
    string? Reason = null);
```

- [x] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter "FullyQualifiedName~DispatcherDashboardControllerTests"
```
Expected: PASS.

- [x] **Step 5: Commit controller and hub changes**

```bash
git add src/MealMate.Api/Realtime/DispatcherHub.cs src/MealMate.Api/Controllers/V1/DispatcherDashboardController.cs tests/MealMate.ApiTests/Controllers/DispatcherDashboardControllerTests.cs
git commit -m "feat(dispatcher): add secured controller endpoints and SignalR availability event"
```

---

### Task 6: Integration Tests for Authorization, Tenant Isolation, and Contract Validation

**Files:**
- Create: `tests/MealMate.IntegrationTests/DispatcherDriversControllerTests.cs`

**Interfaces:**
- Tests: `GET /api/v1/dispatcher/drivers`, `GET /api/v1/dispatcher/drivers/{id}`, `PATCH /api/v1/dispatcher/drivers/{id}/availability`

- [x] **Step 1: Write integration tests**

Create `tests/MealMate.IntegrationTests/DispatcherDriversControllerTests.cs`

- [x] **Step 2: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.IntegrationTests/MealMate.IntegrationTests.csproj --filter "FullyQualifiedName~DispatcherDriversControllerTests"
```
Expected: PASS.

- [x] **Step 3: Commit integration tests**

```bash
git add tests/MealMate.IntegrationTests/DispatcherDriversControllerTests.cs
git commit -m "test(dispatcher): add integration tests for auth, tenant isolation, and availability updates"
```

---

### Task 7: Full Verification and Zero Mock Data Audit

- [x] **Step 1: Run the full test suite**

Run:
```powershell
dotnet test
```
Expected: All tests across `MealMate.UnitTests`, `MealMate.ApiTests`, and `MealMate.IntegrationTests` pass with 0 failures.

- [x] **Step 2: Audit code for forbidden mock data patterns**

Search changed files for forbidden strings:
- `+965 50 123 4567`
- `cdn.mealmate.app`
- `4.8m` or `4.9m` fallback ratings
- Fake distance multipliers (`* 4.2` or `* 4`)
- Hardcoded delay (`12`)
- `mock`, `dummy`, `sample`, `testdata` in production application code.

- [x] **Step 3: Output standard confirmation block**

```text
Mock Data Check:
- No mock business data introduced.
- No fake fallback responses introduced.
- No hardcoded production business values introduced.
- All runtime data comes from real application sources.
```

- [x] **Step 4: Commit any final polish**

```bash
git add .
git commit -m "chore(dispatcher): complete zero mock data audit and verification"
```
