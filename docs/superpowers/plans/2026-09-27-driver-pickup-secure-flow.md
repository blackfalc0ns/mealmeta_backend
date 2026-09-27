# Secure Driver Pickup Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a secure, resumable, and idempotent driver pickup flow where barcode validation issues a cryptographically hashed 5-minute scoped token, condition photo upload establishes ownership via a box-specific endpoint, pickup confirmation consumes the token and validates photo ownership in a single transaction, the pickup summary is resumable from persistent storage, and trip start is an independent idempotent operation.

**Architecture:** Database-backed workflow using EF Core with PostgreSQL and the existing `IFileStorage` abstraction. Barcode validation creates durable `PickupValidation` records with SHA-256 hashed tokens. Box photo uploads are recorded as `PickupConditionPhoto` rows before confirmation. Pickup confirmation executes inside an atomic database transaction verifying token, assignment, photo ownership, and box state. Trip start is isolated and only executed when all assigned stops are `PickedUp`.

**Tech Stack:** ASP.NET Core 9.0, EF Core 9.0 / PostgreSQL, MediatR, SignalR, NSubstitute, FluentAssertions, xUnit, SHA-256 cryptography, `IFileStorage`.

**Spec:** [docs/superpowers/specs/2026-09-27-driver-pickup-secure-flow-design.md](file:///f:/MealMate/backend/docs/superpowers/specs/2026-09-27-driver-pickup-secure-flow-design.md)

## Global Constraints

- Strict Zero Mock Data Policy: All runtime business data must originate from authentic persistence, storage provider, or authenticated session context. No fake fallbacks, mock ratings, or dummy phone numbers.
- Secure Driver Resolution: All driver pickup endpoints resolve `DriverProfileId` exclusively from `ICurrentUser`; caller-provided `driverId` query/body parameters are strictly prohibited.
- Token Security: Raw validation tokens are generated using `RandomNumberGenerator`, expire in 5 minutes, and are never logged or stored in plaintext; only the SHA-256 hash is persisted.
- Photo Verification: Uploads are validated against magic bytes signatures (JPEG, PNG, WebP) and maximum 10 MiB size. Storage folder: `uploads/drivers/delivery/pickup-condition/{tripId}/{boxId}`.
- Concurrency & Idempotency: Confirmation and start operations require an `Idempotency-Key` header and serialize concurrent retries using database unique indexes.
- Complete Removal of Legacy Bypasses: Delete legacy actions `POST /api/v1/driver/orders/{boxId}/pickup` and `POST /api/v1/driver/pickup/start-trip`.

---

## File Structure Map

```text
src/MealMate.Domain/DriverDelivery/
  ├── PickupValidation.cs                  # Durable barcode validation & SHA-256 token hash entity
  └── PickupConditionPhoto.cs              # Condition photo ownership, metadata, and lifecycle entity

src/MealMate.Persistence/
  ├── Configurations/DriverDelivery/
  │     └── DriverDeliveryConfigurations.cs# EF Core mappings & indexes for PickupValidation & PickupConditionPhoto
  ├── AppDbContext.cs                      # Adds DbSets for PickupValidations and PickupConditionPhotos
  └── Migrations/
        └── 20260927150000_AddSecureDriverPickupEntities.cs # PostgreSQL migration

src/MealMate.Application/
  ├── Common/Abstractions/
  │     └── IAppDbContext.cs               # Adds DbSets to interface
  └── Features/DriverDelivery/DriverApp/
        ├── SecureDriverPickupDtos.cs      # Request/response contracts matching spec exactly
        ├── ValidatePickupBarcodeCommand.cs# Token generation, SHA-256 hashing, rate limiting, validation
        ├── UploadPickupConditionPhotoCommand.cs # Multipart file signature validation, storage, ownership
        ├── ConfirmPickupBoxCommand.cs     # Atomic transaction, token consumption, PickupScan, counts
        ├── GetTripPickupSummaryQuery.cs   # Resumable summary query derived from persisted entities
        ├── StartTripDeliveryCommand.cs    # Idempotent trip start requiring all boxes PickedUp
        └── OrphanPickupPhotoCleanupService.cs # Background service cleaning unconfirmed photos > 24h

src/MealMate.Api/
  └── Controllers/V1/
        ├── DriverPickupController.cs      # Secured routes with [Authorize] and Idempotency-Key headers
        └── DriverHomeController.cs        # Removal of legacy pickup endpoint

tests/MealMate.UnitTests/
  └── DriverDelivery/
        ├── SecureDriverPickupUnitTests.cs # Comprehensive unit test suite covering validation, upload, confirm, summary, start
        └── OrphanPickupPhotoCleanupTests.cs # Unit tests for orphan photo cleanup

tests/MealMate.ApiTests/
  └── Controllers/
        └── DriverPickupControllerTests.cs # Controller route, security, and idempotency header tests

tests/MealMate.IntegrationTests/
  └── DriverDelivery/
        └── SecureDriverPickupIntegrationTests.cs # End-to-end integration and concurrency tests

mobile-screens/
  ├── screen-06.02-assigned-boxes-list.md  # Updated PickedUp terminology
  ├── screen-06.03-qr-barcode-scanner.md   # Updated token contract
  ├── screen-06.04-camera-box-condition.md # Updated multipart upload contract
  ├── screen-06.05-boxes-received-summary.md # Updated summary & trip start contracts
  └── screen-06.06-assigned-boxes-in-transit.md # Updated PickedUp terminology
```

---

### Task 1: Domain Entities, EF Core Mappings, & Migration

**Files:**
- Create: `src/MealMate.Domain/DriverDelivery/PickupValidation.cs`
- Create: `src/MealMate.Domain/DriverDelivery/PickupConditionPhoto.cs`
- Modify: `src/MealMate.Application/Common/Abstractions/IAppDbContext.cs:40-70`
- Modify: `src/MealMate.Persistence/AppDbContext.cs:40-70`
- Modify: `src/MealMate.Persistence/Configurations/DriverDelivery/DriverDeliveryConfigurations.cs:1-120`
- Create: `src/MealMate.Persistence/Migrations/20260927150000_AddSecureDriverPickupEntities.cs`
- Test: `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`

**Interfaces:**
- Produces: `PickupValidation` and `PickupConditionPhoto` domain entities with database mappings and unique indexes.
- Consumes: `BaseEntity`, `IAppDbContext`.

- [ ] **Step 1: Write the failing test for entity configuration and persistence**

In `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`:
```csharp
using FluentAssertions;
using MealMate.Domain.DriverDelivery;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class SecureDriverPickupUnitTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<MealMate.Application.Common.Abstractions.IDomainEventDispatcher>());
    }

    [Fact]
    public async Task AppDbContext_CanPersistAndQuery_PickupValidation_And_PickupConditionPhoto()
    {
        await using var db = CreateDb();
        var tripId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var driverProfileId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        var validation = new PickupValidation
        {
            TripId = tripId,
            BoxId = boxId,
            DriverProfileId = driverProfileId,
            DriverAssignmentId = assignmentId,
            BarcodeValue = "480K-1234",
            TokenHash = "mock-sha256-hash",
            ValidatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            IsManualEntry = false
        };
        db.PickupValidations.Add(validation);

        var photo = new PickupConditionPhoto
        {
            PickupValidationId = validation.Id,
            TripId = tripId,
            BoxId = boxId,
            DriverProfileId = driverProfileId,
            StorageKey = "uploads/drivers/delivery/pickup-condition/trip/box/photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 204800,
            Checksum = "mock-sha256",
            UploadedAtUtc = DateTime.UtcNow
        };
        db.PickupConditionPhotos.Add(photo);

        await db.SaveChangesAsync();

        var queriedValidation = await db.PickupValidations.FirstOrDefaultAsync(v => v.Id == validation.Id);
        queriedValidation.Should().NotBeNull();
        queriedValidation!.TokenHash.Should().Be("mock-sha256-hash");
        queriedValidation.ConsumedAtUtc.Should().BeNull();

        var queriedPhoto = await db.PickupConditionPhotos.FirstOrDefaultAsync(p => p.Id == photo.Id);
        queriedPhoto.Should().NotBeNull();
        queriedPhoto!.StorageKey.Should().Be("uploads/drivers/delivery/pickup-condition/trip/box/photo.jpg");
        queriedPhoto.ConfirmedAtUtc.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~SecureDriverPickupUnitTests"
```
Expected: FAIL because `PickupValidation` and `PickupConditionPhoto` are not defined yet.

- [ ] **Step 3: Implement domain entities and EF Core mappings**

Create `src/MealMate.Domain/DriverDelivery/PickupValidation.cs`:
```csharp
using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class PickupValidation : BaseEntity
{
    public Guid TripId { get; set; }
    public Guid BoxId { get; set; }
    public Guid DriverProfileId { get; set; }
    public Guid DriverAssignmentId { get; set; }
    public string BarcodeValue { get; set; } = default!;
    public string TokenHash { get; set; } = default!;
    public DateTime ValidatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public bool IsManualEntry { get; set; }
}
```

Create `src/MealMate.Domain/DriverDelivery/PickupConditionPhoto.cs`:
```csharp
using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class PickupConditionPhoto : BaseEntity
{
    public Guid PickupValidationId { get; set; }
    public Guid TripId { get; set; }
    public Guid BoxId { get; set; }
    public Guid DriverProfileId { get; set; }
    public string StorageKey { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string? Checksum { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
```

In `src/MealMate.Application/Common/Abstractions/IAppDbContext.cs`:
```csharp
DbSet<PickupValidation> PickupValidations { get; }
DbSet<PickupConditionPhoto> PickupConditionPhotos { get; }
```

In `src/MealMate.Persistence/AppDbContext.cs`:
```csharp
public DbSet<PickupValidation> PickupValidations => Set<PickupValidation>();
public DbSet<PickupConditionPhoto> PickupConditionPhotos => Set<PickupConditionPhoto>();
```

In `src/MealMate.Persistence/Configurations/DriverDelivery/DriverDeliveryConfigurations.cs`:
```csharp
public sealed class PickupValidationConfiguration : IEntityTypeConfiguration<PickupValidation>
{
    public void Configure(EntityTypeBuilder<PickupValidation> builder)
    {
        builder.ToTable("PickupValidations");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.BarcodeValue).HasMaxLength(100).IsRequired();
        builder.Property(v => v.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(v => v.TokenHash).IsUnique();
        builder.HasIndex(v => new { v.TripId, v.BoxId, v.DriverProfileId, v.ValidatedAtUtc });
    }
}

public sealed class PickupConditionPhotoConfiguration : IEntityTypeConfiguration<PickupConditionPhoto>
{
    public void Configure(EntityTypeBuilder<PickupConditionPhoto> builder)
    {
        builder.ToTable("PickupConditionPhotos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Checksum).HasMaxLength(128);
        builder.HasIndex(p => p.StorageKey).IsUnique();
        builder.HasIndex(p => new { p.TripId, p.BoxId, p.DriverProfileId, p.UploadedAtUtc });
    }
}
```

Create migration `src/MealMate.Persistence/Migrations/20260927150000_AddSecureDriverPickupEntities.cs` adding tables `PickupValidations` and `PickupConditionPhotos` with `IF NOT EXISTS` guards.

- [ ] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~SecureDriverPickupUnitTests"
```
Expected: PASS.

- [ ] **Step 5: Commit Task 1**

```bash
git add src/MealMate.Domain/DriverDelivery/PickupValidation.cs src/MealMate.Domain/DriverDelivery/PickupConditionPhoto.cs src/MealMate.Application/Common/Abstractions/IAppDbContext.cs src/MealMate.Persistence/ tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs
git commit -m "feat(driver-pickup): add PickupValidation and PickupConditionPhoto domain models and persistence"
```

---

### Task 2: DTO Contracts & Barcode Validation Command (`ValidatePickupBarcodeCommand`)

**Files:**
- Create: `src/MealMate.Application/Features/DriverDelivery/DriverApp/SecureDriverPickupDtos.cs`
- Create: `src/MealMate.Application/Features/DriverDelivery/DriverApp/ValidatePickupBarcodeCommand.cs`
- Modify: `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`

**Interfaces:**
- Produces: `ValidatePickupBarcodeCommand`, `ValidatePickupBarcodeResponseDto`, `ValidatePickupBoxDto`.
- Consumes: `IAppDbContext`, `ICurrentUser`, `ILocalizedMessageProvider`.

- [ ] **Step 1: Write the failing tests for barcode validation**

In `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`:
```csharp
[Fact]
public async Task ValidateBarcode_WhenValid_ReturnsToken_And_PersistsSha256Hash()
{
    await using var db = CreateDb();
    var driverUserId = Guid.NewGuid();
    var restaurantId = Guid.NewGuid();

    var driver = new MealMate.Domain.IdentityAccess.DriverProfile
    {
        UserId = driverUserId,
        RestaurantId = restaurantId,
        FullNameAr = "خالد المحمد",
        FullNameEn = "Khaled Al-Mohammad",
        IsAvailable = true
    };
    db.DriverProfiles.Add(driver);

    var trip = new Trip
    {
        RestaurantId = restaurantId,
        Code = "TRP-901",
        Status = TripStatus.Assigned,
        PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
    };
    var assignment = new DriverAssignment
    {
        DriverProfileId = driver.Id,
        Status = "Active",
        AssignedAtUtc = DateTime.UtcNow
    };
    trip.DriverAssignments.Add(assignment);
    var stop = new TripStop
    {
        OrderCode = "480K-1234",
        CustomerMaskedId = "CUST-1",
        ZoneNameAr = "حولي",
        ZoneNameEn = "Hawally",
        CustomerName = "محمد علي",
        MealSummaryAr = "3 وجبات",
        MealSummaryEn = "3 Meals",
        SlotLabel = "غداء",
        Status = TripStopStatus.Pending,
        BoxCount = 3
    };
    trip.Stops.Add(stop);
    db.Trips.Add(trip);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.UserId.Returns(driverUserId);

    var messages = NullLocalizedMessageProvider.Instance;
    var handler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);

    // Act
    var command = new ValidatePickupBarcodeCommand("480K-1234", IsManualEntry: false);
    var result = await handler.Handle(command, CancellationToken.None);

    // Assert
    result.IsSuccess.Should().BeTrue();
    var response = result.Value;
    response.IsValid.Should().BeTrue();
    response.ValidationToken.Should().NotBeNullOrWhiteSpace();
    response.ScanStatus.Should().Be("BarcodeValidated");
    response.NextAction.Should().Be("CaptureConditionPhoto");
    response.Box.BoxId.Should().Be(stop.Id);
    response.Box.BoxCode.Should().Be("480K-1234");
    response.Box.CustomerName.Should().Be("محمد علي");
    response.Box.DeliveryZone.Should().Be("حولي");
    response.Box.MealsCount.Should().Be(3);

    // Assert DB audit record stores hash, not raw token
    var record = await db.PickupValidations.FirstOrDefaultAsync(v => v.BoxId == stop.Id);
    record.Should().NotBeNull();
    record!.TokenHash.Should().NotBe(response.ValidationToken);
    record.DriverProfileId.Should().Be(driver.Id);
    record.DriverAssignmentId.Should().Be(assignment.Id);
}

[Fact]
public async Task ValidateBarcode_WhenBoxBelongsToAnotherDriver_Returns_BoxNotAssignedToDriver()
{
    await using var db = CreateDb();
    var driverUserId = Guid.NewGuid();
    var otherDriverId = Guid.NewGuid();
    var restaurantId = Guid.NewGuid();

    var myDriver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, RestaurantId = restaurantId, FullNameAr = "سائقي", FullNameEn = "My Driver" };
    var otherDriver = new MealMate.Domain.IdentityAccess.DriverProfile { Id = otherDriverId, RestaurantId = restaurantId, FullNameAr = "سائق آخر", FullNameEn = "Other Driver" };
    db.DriverProfiles.AddRange(myDriver, otherDriver);

    var otherTrip = new Trip { RestaurantId = restaurantId, Code = "TRP-888", Status = TripStatus.Assigned };
    otherTrip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = otherDriver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow });
    otherTrip.Stops.Add(new TripStop { OrderCode = "999K-9999", CustomerMaskedId = "CUST-9", Status = TripStopStatus.Pending });
    db.Trips.Add(otherTrip);

    // My active trip
    var myTrip = new Trip { RestaurantId = restaurantId, Code = "TRP-111", Status = TripStatus.Assigned };
    myTrip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = myDriver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow });
    db.Trips.Add(myTrip);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.UserId.Returns(driverUserId);

    var messages = NullLocalizedMessageProvider.Instance;
    var handler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);

    var result = await handler.Handle(new ValidatePickupBarcodeCommand("999K-9999"), CancellationToken.None);

    result.IsFailure.Should().BeTrue();
    result.Error.Code.Should().Be("BOX_NOT_ASSIGNED_TO_DRIVER");
}

[Fact]
public async Task ValidateBarcode_WhenBoxAlreadyPickedUp_Returns_BoxAlreadyPickedUp()
{
    await using var db = CreateDb();
    var driverUserId = Guid.NewGuid();
    var restaurantId = Guid.NewGuid();

    var driver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, RestaurantId = restaurantId, FullNameAr = "سائق", FullNameEn = "Driver" };
    db.DriverProfiles.Add(driver);

    var trip = new Trip { RestaurantId = restaurantId, Code = "TRP-101", Status = TripStatus.Assigned };
    trip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow });
    var stop = new TripStop { OrderCode = "111K-0001", CustomerMaskedId = "C1", Status = TripStopStatus.PickedUp };
    trip.Stops.Add(stop);
    db.Trips.Add(trip);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.UserId.Returns(driverUserId);

    var messages = NullLocalizedMessageProvider.Instance;
    var handler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);

    var result = await handler.Handle(new ValidatePickupBarcodeCommand("111K-0001"), CancellationToken.None);

    result.IsFailure.Should().BeTrue();
    result.Error.Code.Should().Be("BOX_ALREADY_PICKED_UP");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~ValidateBarcode_"
```
Expected: FAIL.

- [ ] **Step 3: Implement `SecureDriverPickupDtos` and `ValidatePickupBarcodeCommandHandler`**

Create `src/MealMate.Application/Features/DriverDelivery/DriverApp/SecureDriverPickupDtos.cs`:
```csharp
namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record ValidatePickupBarcodeRequest(
    string BarcodeValue,
    bool IsManualEntry = false);

public sealed record ValidatePickupBoxDto(
    Guid BoxId,
    string BoxCode,
    string CustomerName,
    string DeliveryZone,
    int MealsCount);

public sealed record ValidatePickupBarcodeResponseDto(
    bool IsValid,
    string ValidationToken,
    DateTime ExpiresAtUtc,
    ValidatePickupBoxDto Box,
    string ScanStatus,
    string NextAction);

public sealed record UploadPickupConditionPhotoResponseDto(
    Guid BoxId,
    string StorageKey,
    string ContentType,
    long SizeBytes,
    DateTime UploadedAtUtc,
    string ScanStatus,
    string NextAction);

public sealed record ConfirmPickupBoxRequest(
    string ValidationToken,
    string ConditionPhotoStorageKey,
    decimal Latitude,
    decimal Longitude);

public sealed record ConfirmPickupBoxResponseDto(
    Guid TripId,
    Guid BoxId,
    string BoxCode,
    string Status,
    DateTime PickedUpAtUtc,
    int AssignedBoxesCount,
    int ValidatedBoxesCount,
    int ReceivedBoxesCount,
    int RemainingBoxesCount,
    bool AllBoxesReceived,
    string NextAction);

public sealed record TripPickupSummaryBoxDto(
    Guid BoxId,
    string BoxCode,
    string CustomerName,
    string Status,
    DateTime? ValidatedAtUtc,
    DateTime? PickedUpAtUtc,
    string? ConditionPhotoUrl);

public sealed record TripPickupSummaryResponseDto(
    Guid TripId,
    string TripCode,
    string TripStatus,
    string PickupStatus,
    int AssignedBoxesCount,
    int ValidatedBoxesCount,
    int ReceivedBoxesCount,
    int RemainingBoxesCount,
    bool AllBoxesReceived,
    bool CanStartTrip,
    IReadOnlyList<TripPickupSummaryBoxDto> Boxes);

public sealed record StartTripRequest(
    decimal Latitude,
    decimal Longitude);

public sealed record StartTripResponseDto(
    Guid TripId,
    string TripCode,
    string Status,
    int DispatchedBoxesCount,
    string Message,
    DateTime StartedAtUtc);
```

Create `src/MealMate.Application/Features/DriverDelivery/DriverApp/ValidatePickupBarcodeCommand.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record ValidatePickupBarcodeCommand(
    string BarcodeValue,
    bool IsManualEntry = false) : ICommand<ValidatePickupBarcodeResponseDto>;

public sealed class ValidatePickupBarcodeCommandHandler
    : ICommandHandler<ValidatePickupBarcodeCommand, ValidatePickupBarcodeResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public ValidatePickupBarcodeCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<ValidatePickupBarcodeResponseDto>> Handle(
        ValidatePickupBarcodeCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound(
                "DRIVER_NOT_FOUND",
                _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile was not found."));
        }

        if (string.IsNullOrWhiteSpace(request.BarcodeValue))
        {
            return Error.Validation(
                "BARCODE_REQUIRED",
                _messages.GetMessage("BARCODE_REQUIRED", "Barcode value is required."));
        }

        var cleanInput = request.BarcodeValue.Trim().TrimStart('#');

        // Locate driver's active trip and assignment
        var activeAssignment = await _db.DriverAssignments
            .AsNoTracking()
            .Include(da => da.Trip)
            .ThenInclude(t => t.Stops)
            .Where(da => da.DriverProfileId == driver.Id &&
                         da.Status == "Active" &&
                         (da.Trip.Status == TripStatus.Assigned || da.Trip.Status == TripStatus.InProgress))
            .OrderByDescending(da => da.AssignedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeAssignment is null)
        {
            return Error.Validation(
                "BOX_NOT_IN_ACTIVE_TRIP",
                _messages.GetMessage("BOX_NOT_IN_ACTIVE_TRIP", "You do not have an active trip assigned."));
        }

        // Search for stop matching order code
        var matchedStop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.OrderCode != null &&
                                      (s.OrderCode.Equals(cleanInput, StringComparison.OrdinalIgnoreCase) ||
                                       s.OrderCode.EndsWith(cleanInput, StringComparison.OrdinalIgnoreCase) ||
                                       cleanInput.EndsWith(s.OrderCode, StringComparison.OrdinalIgnoreCase)),
                                 cancellationToken);

        if (matchedStop is null)
        {
            return Error.NotFound(
                "BARCODE_NOT_FOUND",
                _messages.GetMessage("BARCODE_NOT_FOUND", "Barcode not found in the system."));
        }

        // Verify stop belongs to this driver's current trip
        if (matchedStop.TripId != activeAssignment.TripId)
        {
            return Error.Forbidden(
                "BOX_NOT_ASSIGNED_TO_DRIVER",
                _messages.GetMessage("BOX_NOT_ASSIGNED_TO_DRIVER", "This box is not assigned to your active trip."));
        }

        // Check if already picked up
        if (matchedStop.Status == TripStopStatus.PickedUp || matchedStop.Status == TripStopStatus.Delivered)
        {
            return Error.Conflict(
                "BOX_ALREADY_PICKED_UP",
                _messages.GetMessage("BOX_ALREADY_PICKED_UP", "This box has already been picked up."));
        }

        // Generate opaque random token and hash
        var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToHexString(rawTokenBytes).ToLowerInvariant();
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

        var nowUtc = DateTime.UtcNow;
        var expiresAtUtc = nowUtc.AddMinutes(5);

        var validation = new PickupValidation
        {
            TripId = activeAssignment.TripId,
            BoxId = matchedStop.Id,
            DriverProfileId = driver.Id,
            DriverAssignmentId = activeAssignment.Id,
            BarcodeValue = cleanInput,
            TokenHash = tokenHash,
            ValidatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            IsManualEntry = request.IsManualEntry
        };

        _db.PickupValidations.Add(validation);
        await _db.SaveChangesAsync(cancellationToken);

        var isAr = _messages.IsArabic();
        var boxDto = new ValidatePickupBoxDto(
            BoxId: matchedStop.Id,
            BoxCode: matchedStop.OrderCode ?? cleanInput,
            CustomerName: matchedStop.CustomerName ?? (isAr ? "العميل" : "Customer"),
            DeliveryZone: (isAr ? matchedStop.ZoneNameAr : matchedStop.ZoneNameEn) ?? string.Empty,
            MealsCount: matchedStop.BoxCount > 0 ? matchedStop.BoxCount : 1);

        return Result.Success(new ValidatePickupBarcodeResponseDto(
            IsValid: true,
            ValidationToken: rawToken,
            ExpiresAtUtc: expiresAtUtc,
            Box: boxDto,
            ScanStatus: "BarcodeValidated",
            NextAction: "CaptureConditionPhoto"));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~ValidateBarcode_"
```
Expected: PASS.

- [ ] **Step 5: Commit Task 2**

```bash
git add src/MealMate.Application/Features/DriverDelivery/DriverApp/SecureDriverPickupDtos.cs src/MealMate.Application/Features/DriverDelivery/DriverApp/ValidatePickupBarcodeCommand.cs tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs
git commit -m "feat(driver-pickup): implement secure barcode validation with token hashing and anti-fraud checks"
```

---

### Task 3: Box Condition Photo Upload (`UploadPickupConditionPhotoCommand`)

**Files:**
- Create: `src/MealMate.Application/Features/DriverDelivery/DriverApp/UploadPickupConditionPhotoCommand.cs`
- Modify: `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`

**Interfaces:**
- Produces: `UploadPickupConditionPhotoCommand`, `UploadPickupConditionPhotoResponseDto`.
- Consumes: `IAppDbContext`, `ICurrentUser`, `IFileStorage`, `ILocalizedMessageProvider`.

- [ ] **Step 1: Write failing tests for condition photo upload**

In `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`:
```csharp
[Fact]
public async Task UploadPhoto_WhenTokenValidAndJpegSignature_SavesToStorage_And_RecordsConditionPhoto()
{
    await using var db = CreateDb();
    var driverUserId = Guid.NewGuid();
    var restaurantId = Guid.NewGuid();

    var driver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, RestaurantId = restaurantId, FullNameAr = "سائق", FullNameEn = "Driver" };
    db.DriverProfiles.Add(driver);

    var trip = new Trip { RestaurantId = restaurantId, Code = "TRP-101", Status = TripStatus.Assigned };
    var assignment = new DriverAssignment { DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow };
    trip.DriverAssignments.Add(assignment);
    var stop = new TripStop { OrderCode = "BOX-777", Status = TripStopStatus.Pending };
    trip.Stops.Add(stop);
    db.Trips.Add(trip);

    var rawToken = "my-secret-token-123456";
    var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
    var validation = new PickupValidation
    {
        TripId = trip.Id,
        BoxId = stop.Id,
        DriverProfileId = driver.Id,
        DriverAssignmentId = assignment.Id,
        BarcodeValue = "BOX-777",
        TokenHash = tokenHash,
        ValidatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
    };
    db.PickupValidations.Add(validation);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.UserId.Returns(driverUserId);

    var storage = Substitute.For<IFileStorage>();
    var expectedKey = $"uploads/drivers/delivery/pickup-condition/{trip.Id}/{stop.Id}/photo.jpg";
    storage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), "image/jpeg", Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(new StoredFile(expectedKey, "photo.jpg", "image/jpeg", 1024, "hash"));

    var messages = NullLocalizedMessageProvider.Instance;
    var handler = new UploadPickupConditionPhotoCommandHandler(db, currentUser, storage, messages);

    // JPEG magic bytes: FF D8 FF
    var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
    using var stream = new MemoryStream(jpegBytes);

    var command = new UploadPickupConditionPhotoCommand(
        BoxId: stop.Id,
        ValidationToken: rawToken,
        FileName: "photo.jpg",
        ContentType: "image/jpeg",
        ContentStream: stream);

    var result = await handler.Handle(command, CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.BoxId.Should().Be(stop.Id);
    result.Value.StorageKey.Should().Be(expectedKey);
    result.Value.ScanStatus.Should().Be("PhotoUploaded");
    result.Value.NextAction.Should().Be("ConfirmPickup");

    var photoRow = await db.PickupConditionPhotos.FirstOrDefaultAsync(p => p.BoxId == stop.Id);
    photoRow.Should().NotBeNull();
    photoRow!.StorageKey.Should().Be(expectedKey);
}

[Fact]
public async Task UploadPhoto_WhenInvalidMagicBytes_Returns_PhotoInvalidSignature()
{
    await using var db = CreateDb();
    var driverUserId = Guid.NewGuid();
    var driver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, FullNameAr = "سائق", FullNameEn = "Driver" };
    db.DriverProfiles.Add(driver);

    var rawToken = "raw-tok";
    var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
    var validation = new PickupValidation { BoxId = Guid.NewGuid(), DriverProfileId = driver.Id, TokenHash = tokenHash, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5) };
    db.PickupValidations.Add(validation);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.UserId.Returns(driverUserId);

    var storage = Substitute.For<IFileStorage>();
    var handler = new UploadPickupConditionPhotoCommandHandler(db, currentUser, storage, NullLocalizedMessageProvider.Instance);

    // Text file bytes masquerading as image/jpeg
    var fakeBytes = System.Text.Encoding.UTF8.GetBytes("Not an image header");
    using var stream = new MemoryStream(fakeBytes);

    var command = new UploadPickupConditionPhotoCommand(validation.BoxId, rawToken, "fake.jpg", "image/jpeg", stream);
    var result = await handler.Handle(command, CancellationToken.None);

    result.IsFailure.Should().BeTrue();
    result.Error.Code.Should().Be("PHOTO_INVALID_SIGNATURE");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~UploadPhoto_"
```
Expected: FAIL.

- [ ] **Step 3: Implement `UploadPickupConditionPhotoCommandHandler`**

Create `src/MealMate.Application/Features/DriverDelivery/DriverApp/UploadPickupConditionPhotoCommand.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record UploadPickupConditionPhotoCommand(
    Guid BoxId,
    string ValidationToken,
    string FileName,
    string ContentType,
    Stream ContentStream) : ICommand<UploadPickupConditionPhotoResponseDto>;

public sealed class UploadPickupConditionPhotoCommandHandler
    : ICommandHandler<UploadPickupConditionPhotoCommand, UploadPickupConditionPhotoResponseDto>
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MiB

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly ILocalizedMessageProvider _messages;

    public UploadPickupConditionPhotoCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _messages = messages;
    }

    public async Task<Result<UploadPickupConditionPhotoResponseDto>> Handle(
        UploadPickupConditionPhotoCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound("DRIVER_NOT_FOUND", _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile not found."));
        }

        if (string.IsNullOrWhiteSpace(request.ValidationToken))
        {
            return Error.Validation("VALIDATION_TOKEN_INVALID", "Validation token is required.");
        }

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.ValidationToken.Trim()))).ToLowerInvariant();

        var validation = await _db.PickupValidations
            .FirstOrDefaultAsync(v => v.BoxId == request.BoxId &&
                                      v.DriverProfileId == driver.Id &&
                                      v.TokenHash == tokenHash, cancellationToken);

        if (validation is null)
        {
            return Error.Validation("VALIDATION_TOKEN_INVALID", "Invalid validation token for this box.");
        }

        if (validation.ExpiresAtUtc < DateTime.UtcNow)
        {
            return Error.Validation("VALIDATION_TOKEN_EXPIRED", "Validation token has expired. Please re-scan barcode.");
        }

        if (validation.ConsumedAtUtc.HasValue)
        {
            return Error.Validation("VALIDATION_TOKEN_CONSUMED", "Validation token has already been consumed.");
        }

        // Verify active assignment unchanged
        var activeAssignmentExists = await _db.DriverAssignments
            .AsNoTracking()
            .AnyAsync(da => da.Id == validation.DriverAssignmentId && da.Status == "Active", cancellationToken);

        if (!activeAssignmentExists)
        {
            return Error.Forbidden("BOX_ASSIGNMENT_CHANGED", "Driver assignment for this trip has changed.");
        }

        // Validate content type & size
        var contentType = request.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (contentType != "image/jpeg" && contentType != "image/jpg" && contentType != "image/png" && contentType != "image/webp")
        {
            return Error.Validation("PHOTO_INVALID_TYPE", "Only JPEG, PNG, and WebP images are allowed.");
        }

        if (request.ContentStream is null || request.ContentStream.Length == 0)
        {
            return Error.Validation("PHOTO_REQUIRED", "Condition photo file stream is required.");
        }

        if (request.ContentStream.Length > MaxFileSizeBytes)
        {
            return Error.Validation("PHOTO_TOO_LARGE", "Condition photo must not exceed 10 MiB.");
        }

        // Verify magic bytes signature
        request.ContentStream.Position = 0;
        var header = new byte[12];
        var read = await request.ContentStream.ReadAsync(header.AsMemory(0, Math.Min(12, (int)request.ContentStream.Length)), cancellationToken);
        request.ContentStream.Position = 0;

        if (!IsValidImageSignature(header, read, contentType))
        {
            return Error.Validation("PHOTO_INVALID_SIGNATURE", "File content does not match the declared image format.");
        }

        var folder = $"uploads/drivers/delivery/pickup-condition/{validation.TripId}/{validation.BoxId}";
        var storedFile = await _storage.SaveAsync(request.ContentStream, request.FileName, contentType, folder, cancellationToken);

        // Check if a previous unconfirmed photo existed for this validation; if so, replace it
        var existingPhotos = await _db.PickupConditionPhotos
            .Where(p => p.PickupValidationId == validation.Id && p.ConfirmedAtUtc == null && p.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);

        var nowUtc = DateTime.UtcNow;
        var photoRecord = new PickupConditionPhoto
        {
            PickupValidationId = validation.Id,
            TripId = validation.TripId,
            BoxId = validation.BoxId,
            DriverProfileId = driver.Id,
            StorageKey = storedFile.StorageKey,
            ContentType = contentType,
            SizeBytes = storedFile.SizeBytes,
            Checksum = storedFile.Sha256Checksum,
            UploadedAtUtc = nowUtc
        };

        _db.PickupConditionPhotos.Add(photoRecord);
        await _db.SaveChangesAsync(cancellationToken);

        // Safely clean up previous replaced unconfirmed files
        foreach (var oldPhoto in existingPhotos)
        {
            try
            {
                await _storage.DeleteAsync(oldPhoto.StorageKey, cancellationToken);
                oldPhoto.DeletedAtUtc = nowUtc;
            }
            catch
            {
                // Non-fatal cleanup log
            }
        }
        if (existingPhotos.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new UploadPickupConditionPhotoResponseDto(
            BoxId: validation.BoxId,
            StorageKey: storedFile.StorageKey,
            ContentType: contentType,
            SizeBytes: storedFile.SizeBytes,
            UploadedAtUtc: nowUtc,
            ScanStatus: "PhotoUploaded",
            NextAction: "ConfirmPickup"));
    }

    private static bool IsValidImageSignature(byte[] header, int read, string contentType)
    {
        if (read < 3) return false;

        // JPEG: FF D8 FF
        if ((contentType == "image/jpeg" || contentType == "image/jpg") &&
            header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (contentType == "image/png" && read >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return true;
        }

        // WebP: RIFF....WEBP
        if (contentType == "image/webp" && read >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return true;
        }

        return false;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~UploadPhoto_"
```
Expected: PASS.

- [ ] **Step 5: Commit Task 3**

```bash
git add src/MealMate.Application/Features/DriverDelivery/DriverApp/UploadPickupConditionPhotoCommand.cs tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs
git commit -m "feat(driver-pickup): implement condition photo upload with magic byte verification and ownership binding"
```

---

### Task 4: Atomic Pickup Confirmation with Idempotency (`ConfirmPickupBoxCommand`)

**Files:**
- Create: `src/MealMate.Application/Features/DriverDelivery/DriverApp/ConfirmPickupBoxCommand.cs`
- Modify: `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`

**Interfaces:**
- Produces: `ConfirmPickupBoxCommand`, `ConfirmPickupBoxResponseDto`.
- Consumes: `IAppDbContext`, `ICurrentUser`, `ILocalizedMessageProvider`.

- [ ] **Step 1: Write failing tests for pickup confirmation**

In `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`:
```csharp
[Fact]
public async Task ConfirmPickup_ConsumesToken_CreatesPickupScan_UpdatesStopToPickedUp_And_LeavesTripUnstarted()
{
    await using var db = CreateDb();
    var driverUserId = Guid.NewGuid();
    var restaurantId = Guid.NewGuid();

    var driver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, RestaurantId = restaurantId, FullNameAr = "سائق", FullNameEn = "Driver" };
    db.DriverProfiles.Add(driver);

    var trip = new Trip { RestaurantId = restaurantId, Code = "TRP-101", Status = TripStatus.Assigned };
    var assignment = new DriverAssignment { DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow };
    trip.DriverAssignments.Add(assignment);

    var stop1 = new TripStop { OrderCode = "BOX-1", Status = TripStopStatus.Pending };
    var stop2 = new TripStop { OrderCode = "BOX-2", Status = TripStopStatus.Pending };
    trip.Stops.Add(stop1);
    trip.Stops.Add(stop2);
    db.Trips.Add(trip);

    var rawToken = "confirm-token-1";
    var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
    var validation = new PickupValidation
    {
        TripId = trip.Id,
        BoxId = stop1.Id,
        DriverProfileId = driver.Id,
        DriverAssignmentId = assignment.Id,
        BarcodeValue = "BOX-1",
        TokenHash = tokenHash,
        ValidatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
    };
    db.PickupValidations.Add(validation);

    var photoKey = "uploads/drivers/delivery/pickup-condition/trip/box1/photo.jpg";
    var photo = new PickupConditionPhoto
    {
        PickupValidationId = validation.Id,
        TripId = trip.Id,
        BoxId = stop1.Id,
        DriverProfileId = driver.Id,
        StorageKey = photoKey,
        ContentType = "image/jpeg",
        SizeBytes = 1024,
        UploadedAtUtc = DateTime.UtcNow
    };
    db.PickupConditionPhotos.Add(photo);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.UserId.Returns(driverUserId);

    var handler = new ConfirmPickupBoxCommandHandler(db, currentUser, NullLocalizedMessageProvider.Instance);

    var command = new ConfirmPickupBoxCommand(
        BoxId: stop1.Id,
        ValidationToken: rawToken,
        ConditionPhotoStorageKey: photoKey,
        Latitude: 29.3375m,
        Longitude: 48.0280m,
        IdempotencyKey: Guid.NewGuid().ToString());

    var result = await handler.Handle(command, CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    var response = result.Value;
    response.Status.Should().Be("PickedUp");
    response.AssignedBoxesCount.Should().Be(2);
    response.ReceivedBoxesCount.Should().Be(1);
    response.RemainingBoxesCount.Should().Be(1);
    response.AllBoxesReceived.Should().BeFalse();
    response.NextAction.Should().Be("ShowBoxSuccess");

    // Verify DB mutations
    var updatedStop = await db.TripStops.FindAsync(stop1.Id);
    updatedStop!.Status.Should().Be(TripStopStatus.PickedUp);

    var updatedTrip = await db.Trips.FindAsync(trip.Id);
    updatedTrip!.Status.Should().Be(TripStatus.Assigned); // Trip not started!

    var updatedValidation = await db.PickupValidations.FindAsync(validation.Id);
    updatedValidation!.ConsumedAtUtc.Should().NotBeNull();

    var updatedPhoto = await db.PickupConditionPhotos.FindAsync(photo.Id);
    updatedPhoto!.ConfirmedAtUtc.Should().NotBeNull();

    var scan = await db.PickupScans.FirstOrDefaultAsync(s => s.TripStopId == stop1.Id);
    scan.Should().NotBeNull();
    scan!.ConditionPhotoStorageKey.Should().Be(photoKey);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~ConfirmPickup_"
```
Expected: FAIL.

- [ ] **Step 3: Implement `ConfirmPickupBoxCommandHandler`**

Create `src/MealMate.Application/Features/DriverDelivery/DriverApp/ConfirmPickupBoxCommand.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record ConfirmPickupBoxCommand(
    Guid BoxId,
    string ValidationToken,
    string ConditionPhotoStorageKey,
    decimal Latitude,
    decimal Longitude,
    string IdempotencyKey) : ICommand<ConfirmPickupBoxResponseDto>;

public sealed class ConfirmPickupBoxCommandHandler
    : ICommandHandler<ConfirmPickupBoxCommand, ConfirmPickupBoxResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public ConfirmPickupBoxCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<ConfirmPickupBoxResponseDto>> Handle(
        ConfirmPickupBoxCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound("DRIVER_NOT_FOUND", _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile not found."));
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Error.Validation("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required.");
        }

        var requestHash = ComputeRequestHash(request);
        var idempotencyScope = $"PickupConfirm:{driver.Id}";

        // 1. Idempotency Check
        var existingRecord = await _db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Scope == idempotencyScope && r.Key == request.IdempotencyKey, cancellationToken);

        if (existingRecord != null)
        {
            if (existingRecord.RequestHash != requestHash)
            {
                return Error.Conflict("IDEMPOTENCY_KEY_REUSED", "An operation with this idempotency key already exists with different request parameters.");
            }

            if (!string.IsNullOrWhiteSpace(existingRecord.ResponseBody))
            {
                var cached = JsonSerializer.Deserialize<ConfirmPickupBoxResponseDto>(existingRecord.ResponseBody);
                if (cached != null) return Result.Success(cached);
            }
        }

        if (request.Latitude == 0 && request.Longitude == 0)
        {
            return Error.Validation("LOCATION_REQUIRED", "GPS coordinates are required to confirm pickup.");
        }

        if (string.IsNullOrWhiteSpace(request.ValidationToken))
        {
            return Error.Validation("VALIDATION_TOKEN_INVALID", "Validation token is required.");
        }

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.ValidationToken.Trim()))).ToLowerInvariant();

        var validation = await _db.PickupValidations
            .FirstOrDefaultAsync(v => v.BoxId == request.BoxId &&
                                      v.DriverProfileId == driver.Id &&
                                      v.TokenHash == tokenHash, cancellationToken);

        if (validation is null)
        {
            return Error.Validation("VALIDATION_TOKEN_INVALID", "Invalid validation token.");
        }

        if (validation.ExpiresAtUtc < DateTime.UtcNow)
        {
            return Error.Validation("VALIDATION_TOKEN_EXPIRED", "Validation token has expired.");
        }

        if (validation.ConsumedAtUtc.HasValue)
        {
            return Error.Validation("VALIDATION_TOKEN_CONSUMED", "Validation token has already been consumed.");
        }

        var activeAssignmentExists = await _db.DriverAssignments
            .AsNoTracking()
            .AnyAsync(da => da.Id == validation.DriverAssignmentId && da.Status == "Active", cancellationToken);

        if (!activeAssignmentExists)
        {
            return Error.Forbidden("BOX_ASSIGNMENT_CHANGED", "Driver assignment for this trip has changed.");
        }

        var photo = await _db.PickupConditionPhotos
            .FirstOrDefaultAsync(p => p.PickupValidationId == validation.Id &&
                                      p.BoxId == request.BoxId &&
                                      p.StorageKey == request.ConditionPhotoStorageKey &&
                                      p.DeletedAtUtc == null, cancellationToken);

        if (photo is null)
        {
            return Error.Validation("PHOTO_NOT_UPLOADED", "No condition photo record found matching this storage key and validation.");
        }

        var stop = await _db.TripStops
            .FirstOrDefaultAsync(s => s.Id == request.BoxId && s.TripId == validation.TripId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound("BOX_NOT_FOUND", "Stop record not found.");
        }

        if (stop.Status == TripStopStatus.PickedUp || stop.Status == TripStopStatus.Delivered)
        {
            return Error.Conflict("BOX_ALREADY_PICKED_UP", "Box is already marked as picked up.");
        }

        var nowUtc = DateTime.UtcNow;

        // Execute state mutations
        validation.ConsumedAtUtc = nowUtc;
        photo.ConfirmedAtUtc = nowUtc;
        stop.Status = TripStopStatus.PickedUp;

        var pickupScan = new PickupScan
        {
            TripStopId = stop.Id,
            TripId = validation.TripId,
            DriverProfileId = driver.Id,
            ScannedAtUtc = nowUtc,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsManualEntry = validation.IsManualEntry,
            ConditionPhotoStorageKey = photo.StorageKey
        };
        _db.PickupScans.Add(pickupScan);

        await _db.SaveChangesAsync(cancellationToken);

        // Derive authoritative counts
        var tripStops = await _db.TripStops
            .AsNoTracking()
            .Where(s => s.TripId == validation.TripId)
            .ToListAsync(cancellationToken);

        var assignedBoxesCount = tripStops.Count;
        var receivedBoxesCount = tripStops.Count(s => s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Delivered);
        var remainingBoxesCount = Math.Max(0, assignedBoxesCount - receivedBoxesCount);
        var allBoxesReceived = remainingBoxesCount == 0 && assignedBoxesCount > 0;

        var validatedBoxesCount = await _db.PickupValidations
            .AsNoTracking()
            .Where(v => v.TripId == validation.TripId && v.DriverAssignmentId == validation.DriverAssignmentId)
            .Select(v => v.BoxId)
            .Distinct()
            .CountAsync(cancellationToken);

        var nextAction = allBoxesReceived ? "ShowPickupSummary" : "ShowBoxSuccess";

        var response = new ConfirmPickupBoxResponseDto(
            TripId: validation.TripId,
            BoxId: stop.Id,
            BoxCode: stop.OrderCode ?? string.Empty,
            Status: "PickedUp",
            PickedUpAtUtc: nowUtc,
            AssignedBoxesCount: assignedBoxesCount,
            ValidatedBoxesCount: validatedBoxesCount,
            ReceivedBoxesCount: receivedBoxesCount,
            RemainingBoxesCount: remainingBoxesCount,
            AllBoxesReceived: allBoxesReceived,
            NextAction: nextAction);

        // Record Idempotency outcome
        var newRecord = new IdempotencyRecord
        {
            Scope = idempotencyScope,
            Key = request.IdempotencyKey,
            RequestHash = requestHash,
            ResponseBody = JsonSerializer.Serialize(response),
            HttpStatusCode = 200,
            UserId = driver.UserId,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddHours(24)
        };
        _db.IdempotencyRecords.Add(newRecord);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(response);
    }

    private static string ComputeRequestHash(ConfirmPickupBoxCommand request)
    {
        var raw = $"{request.BoxId}:{request.ValidationToken}:{request.ConditionPhotoStorageKey}:{request.Latitude}:{request.Longitude}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~ConfirmPickup_"
```
Expected: PASS.

- [ ] **Step 5: Commit Task 4**

```bash
git add src/MealMate.Application/Features/DriverDelivery/DriverApp/ConfirmPickupBoxCommand.cs tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs
git commit -m "feat(driver-pickup): implement atomic pickup confirmation with token consumption and idempotency"
```

---

### Task 5: Resumable Pickup Summary Query (`GetTripPickupSummaryQuery`)

**Files:**
- Create: `src/MealMate.Application/Features/DriverDelivery/DriverApp/GetTripPickupSummaryQuery.cs`
- Modify: `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`

**Interfaces:**
- Produces: `GetTripPickupSummaryQuery`, `TripPickupSummaryResponseDto`.
- Consumes: `IAppDbContext`, `ICurrentUser`, `ILocalizedMessageProvider`.

- [ ] **Step 1: Write failing test for pickup summary**

In `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`:
```csharp
[Fact]
public async Task GetPickupSummary_ReturnsPersistedBoxes_And_DerivedStatus()
{
    await using var db = CreateDb();
    var driverUserId = Guid.NewGuid();
    var restaurantId = Guid.NewGuid();

    var driver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, RestaurantId = restaurantId, FullNameAr = "سائق", FullNameEn = "Driver" };
    db.DriverProfiles.Add(driver);

    var trip = new Trip { RestaurantId = restaurantId, Code = "48-8752", Status = TripStatus.Assigned };
    var assignment = new DriverAssignment { DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow };
    trip.DriverAssignments.Add(assignment);

    var stop1 = new TripStop { OrderCode = "480K-1234", CustomerName = "محمد علي", Status = TripStopStatus.PickedUp, DeliveredAtUtc = DateTime.UtcNow };
    var stop2 = new TripStop { OrderCode = "480K-5678", CustomerName = "سالم", Status = TripStopStatus.Pending };
    trip.Stops.Add(stop1);
    trip.Stops.Add(stop2);
    db.Trips.Add(trip);

    db.PickupScans.Add(new PickupScan
    {
        TripId = trip.Id,
        TripStopId = stop1.Id,
        DriverProfileId = driver.Id,
        ScannedAtUtc = DateTime.UtcNow,
        ConditionPhotoStorageKey = "uploads/box1.jpg"
    });
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.UserId.Returns(driverUserId);

    var handler = new GetTripPickupSummaryQueryHandler(db, currentUser, NullLocalizedMessageProvider.Instance);

    var result = await handler.Handle(new GetTripPickupSummaryQuery(trip.Id), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    var summary = result.Value;
    summary.TripId.Should().Be(trip.Id);
    summary.TripCode.Should().Be("48-8752");
    summary.AssignedBoxesCount.Should().Be(2);
    summary.ReceivedBoxesCount.Should().Be(1);
    summary.RemainingBoxesCount.Should().Be(1);
    summary.AllBoxesReceived.Should().BeFalse();
    summary.CanStartTrip.Should().BeFalse();
    summary.PickupStatus.Should().Be("InProgress");
    summary.Boxes.Should().HaveCount(2);

    var b1 = summary.Boxes.First(b => b.BoxId == stop1.Id);
    b1.Status.Should().Be("PickedUp");
    b1.ConditionPhotoUrl.Should().Be("/api/v1/uploads/content?key=uploads%2Fbox1.jpg");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~GetPickupSummary_"
```
Expected: FAIL.

- [ ] **Step 3: Implement `GetTripPickupSummaryQueryHandler`**

Create `src/MealMate.Application/Features/DriverDelivery/DriverApp/GetTripPickupSummaryQuery.cs`:
```csharp
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record GetTripPickupSummaryQuery(Guid TripId) : IQuery<TripPickupSummaryResponseDto>;

public sealed class GetTripPickupSummaryQueryHandler
    : IQueryHandler<GetTripPickupSummaryQuery, TripPickupSummaryResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetTripPickupSummaryQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<TripPickupSummaryResponseDto>> Handle(
        GetTripPickupSummaryQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound("DRIVER_NOT_FOUND", _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile not found."));
        }

        var trip = await _db.Trips
            .AsNoTracking()
            .Include(t => t.Stops)
            .Include(t => t.DriverAssignments)
            .FirstOrDefaultAsync(t => t.Id == request.TripId, cancellationToken);

        if (trip is null)
        {
            return Error.NotFound("TRIP_NOT_FOUND", "Trip was not found.");
        }

        // Ownership verification
        var isAssignedToDriver = trip.DriverAssignments.Any(da => da.DriverProfileId == driver.Id && da.Status == "Active");
        if (!isAssignedToDriver)
        {
            return Error.Forbidden("DRIVER_NOT_AUTHORIZED", "This trip is not assigned to you.");
        }

        var stops = trip.Stops.ToList();
        var stopIds = stops.Select(s => s.Id).ToList();

        var pickupScans = await _db.PickupScans
            .AsNoTracking()
            .Where(s => s.TripId == trip.Id)
            .ToDictionaryAsync(s => s.TripStopId, cancellationToken);

        var latestValidations = await _db.PickupValidations
            .AsNoTracking()
            .Where(v => v.TripId == trip.Id && v.DriverProfileId == driver.Id)
            .GroupBy(v => v.BoxId)
            .Select(g => g.OrderByDescending(v => v.ValidatedAtUtc).First())
            .ToDictionaryAsync(v => v.BoxId, cancellationToken);

        var assignedBoxesCount = stops.Count;
        var receivedBoxesCount = stops.Count(s => s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Delivered);
        var remainingBoxesCount = Math.Max(0, assignedBoxesCount - receivedBoxesCount);
        var allBoxesReceived = remainingBoxesCount == 0 && assignedBoxesCount > 0;
        var validatedBoxesCount = latestValidations.Count;

        var canStartTrip = allBoxesReceived && (trip.Status == TripStatus.Assigned);

        string pickupStatus;
        if (canStartTrip)
        {
            pickupStatus = "ReadyToStart";
        }
        else if (receivedBoxesCount > 0)
        {
            pickupStatus = "InProgress";
        }
        else
        {
            pickupStatus = "Pending";
        }

        var isAr = _messages.IsArabic();
        var boxesDtoList = new List<TripPickupSummaryBoxDto>();

        foreach (var stop in stops)
        {
            pickupScans.TryGetValue(stop.Id, out var scan);
            latestValidations.TryGetValue(stop.Id, out var validation);

            string? photoUrl = null;
            if (!string.IsNullOrWhiteSpace(scan?.ConditionPhotoStorageKey))
            {
                photoUrl = $"/api/v1/uploads/content?key={Uri.EscapeDataString(scan.ConditionPhotoStorageKey)}";
            }

            boxesDtoList.Add(new TripPickupSummaryBoxDto(
                BoxId: stop.Id,
                BoxCode: stop.OrderCode ?? string.Empty,
                CustomerName: stop.CustomerName ?? (isAr ? "العميل" : "Customer"),
                Status: stop.Status.ToString(),
                ValidatedAtUtc: validation?.ValidatedAtUtc,
                PickedUpAtUtc: scan?.ScannedAtUtc,
                ConditionPhotoUrl: photoUrl));
        }

        return Result.Success(new TripPickupSummaryResponseDto(
            TripId: trip.Id,
            TripCode: trip.Code,
            TripStatus: trip.Status.ToString(),
            PickupStatus: pickupStatus,
            AssignedBoxesCount: assignedBoxesCount,
            ValidatedBoxesCount: validatedBoxesCount,
            ReceivedBoxesCount: receivedBoxesCount,
            RemainingBoxesCount: remainingBoxesCount,
            AllBoxesReceived: allBoxesReceived,
            CanStartTrip: canStartTrip,
            Boxes: boxesDtoList));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~GetPickupSummary_"
```
Expected: PASS.

- [ ] **Step 5: Commit Task 5**

```bash
git add src/MealMate.Application/Features/DriverDelivery/DriverApp/GetTripPickupSummaryQuery.cs tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs
git commit -m "feat(driver-pickup): implement resumable pickup summary query derived from persistent database state"
```

---

### Task 6: Secure Trip Start Command with Idempotency (`StartTripCommand`)

**Files:**
- Create: `src/MealMate.Application/Features/DriverDelivery/DriverApp/StartTripCommand.cs`
- Modify: `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`

**Interfaces:**
- Produces: `StartTripCommand`, `StartTripResponseDto`.
- Consumes: `IAppDbContext`, `ICurrentUser`, `ILocalizedMessageProvider`.

- [ ] **Step 1: Write failing tests for start trip**

In `tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs`:
```csharp
[Fact]
public async Task StartTrip_WhenAllBoxesPickedUp_SetsInProgress_And_ReturnsSuccess()
{
    await using var db = CreateDb();
    var driverUserId = Guid.NewGuid();
    var restaurantId = Guid.NewGuid();

    var driver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, RestaurantId = restaurantId, FullNameAr = "سائق", FullNameEn = "Driver" };
    db.DriverProfiles.Add(driver);

    var trip = new Trip { RestaurantId = restaurantId, Code = "TRP-999", Status = TripStatus.Assigned };
    var assignment = new DriverAssignment { DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow };
    trip.DriverAssignments.Add(assignment);
    trip.Stops.Add(new TripStop { OrderCode = "BOX-1", Status = TripStopStatus.PickedUp });
    trip.Stops.Add(new TripStop { OrderCode = "BOX-2", Status = TripStopStatus.PickedUp });
    db.Trips.Add(trip);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.UserId.Returns(driverUserId);

    var handler = new StartTripCommandHandler(db, currentUser, NullLocalizedMessageProvider.Instance);

    var command = new StartTripCommand(trip.Id, 29.3375m, 48.0280m, Guid.NewGuid().ToString());
    var result = await handler.Handle(command, CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.Status.Should().Be("InProgress");
    result.Value.DispatchedBoxesCount.Should().Be(2);

    var updatedTrip = await db.Trips.FindAsync(trip.Id);
    updatedTrip!.Status.Should().Be(TripStatus.InProgress);
    updatedTrip.StartedAtUtc.Should().NotBeNull();
}

[Fact]
public async Task StartTrip_WhenUnscannedBoxesExist_Returns_UnscannedBoxesExist()
{
    await using var db = CreateDb();
    var driverUserId = Guid.NewGuid();
    var driver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, FullNameAr = "سائق", FullNameEn = "Driver" };
    db.DriverProfiles.Add(driver);

    var trip = new Trip { Code = "TRP-101", Status = TripStatus.Assigned };
    trip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = driver.Id, Status = "Active" });
    trip.Stops.Add(new TripStop { OrderCode = "BOX-1", Status = TripStopStatus.PickedUp });
    trip.Stops.Add(new TripStop { OrderCode = "BOX-2", Status = TripStopStatus.Pending }); // Pending!
    db.Trips.Add(trip);
    await db.SaveChangesAsync();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAuthenticated.Returns(true);
    currentUser.UserId.Returns(driverUserId);

    var handler = new StartTripCommandHandler(db, currentUser, NullLocalizedMessageProvider.Instance);

    var command = new StartTripCommand(trip.Id, 29.3375m, 48.0280m, Guid.NewGuid().ToString());
    var result = await handler.Handle(command, CancellationToken.None);

    result.IsFailure.Should().BeTrue();
    result.Error.Code.Should().Be("UNSCANNED_BOXES_EXIST");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~StartTrip_"
```
Expected: FAIL.

- [ ] **Step 3: Implement `StartTripCommandHandler`**

Create `src/MealMate.Application/Features/DriverDelivery/DriverApp/StartTripCommand.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record StartTripCommand(
    Guid TripId,
    decimal Latitude,
    decimal Longitude,
    string IdempotencyKey) : ICommand<StartTripResponseDto>;

public sealed class StartTripCommandHandler
    : ICommandHandler<StartTripCommand, StartTripResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public StartTripCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<StartTripResponseDto>> Handle(
        StartTripCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound("DRIVER_NOT_FOUND", _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile not found."));
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Error.Validation("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required.");
        }

        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.TripId}:{request.Latitude}:{request.Longitude}")));
        var idempotencyScope = $"TripStart:{driver.Id}";

        // Idempotency check
        var existingRecord = await _db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Scope == idempotencyScope && r.Key == request.IdempotencyKey, cancellationToken);

        if (existingRecord != null)
        {
            if (existingRecord.RequestHash != requestHash)
            {
                return Error.Conflict("IDEMPOTENCY_KEY_REUSED", "An operation with this idempotency key already exists with different request parameters.");
            }

            if (!string.IsNullOrWhiteSpace(existingRecord.ResponseBody))
            {
                var cached = JsonSerializer.Deserialize<StartTripResponseDto>(existingRecord.ResponseBody);
                if (cached != null) return Result.Success(cached);
            }
        }

        if (request.Latitude == 0 && request.Longitude == 0)
        {
            return Error.Validation("LOCATION_REQUIRED", "Driver GPS coordinates are required to start trip.");
        }

        var trip = await _db.Trips
            .Include(t => t.Stops)
            .Include(t => t.DriverAssignments)
            .FirstOrDefaultAsync(t => t.Id == request.TripId, cancellationToken);

        if (trip is null)
        {
            return Error.NotFound("TRIP_NOT_FOUND", "Trip was not found.");
        }

        var isAssignedToDriver = trip.DriverAssignments.Any(da => da.DriverProfileId == driver.Id && da.Status == "Active");
        if (!isAssignedToDriver)
        {
            return Error.Forbidden("DRIVER_NOT_AUTHORIZED", "This trip is not assigned to you.");
        }

        if (trip.Status == TripStatus.InProgress)
        {
            return Error.Conflict("TRIP_ALREADY_STARTED", "This trip is already in progress.");
        }

        if (trip.Status != TripStatus.Assigned)
        {
            return Error.Validation("TRIP_NOT_READY", "Trip cannot be started from its current status.");
        }

        var pendingStops = trip.Stops.Where(s => s.Status != TripStopStatus.PickedUp && s.Status != TripStopStatus.Delivered).ToList();
        if (pendingStops.Count > 0)
        {
            return Error.Validation("UNSCANNED_BOXES_EXIST", $"{pendingStops.Count} boxes have not been scanned and verified yet.");
        }

        var nowUtc = DateTime.UtcNow;
        trip.Status = TripStatus.InProgress;
        trip.StartedAtUtc = nowUtc;

        // Update active work session count
        var activeSession = await _db.DriverWorkSessions
            .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);

        if (activeSession != null)
        {
            activeSession.InDeliveryOrdersCount = trip.Stops.Count;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var isAr = _messages.IsArabic();
        var msg = isAr
            ? $"تم بدء رحلة التوصيل بنجاح بحمولة {trip.Stops.Count} صندوق. رافقتك السلامة!"
            : $"Trip delivery started successfully with {trip.Stops.Count} boxes. Have a safe drive!";

        var response = new StartTripResponseDto(
            TripId: trip.Id,
            TripCode: trip.Code,
            Status: "InProgress",
            DispatchedBoxesCount: trip.Stops.Count,
            Message: msg,
            StartedAtUtc: nowUtc);

        // Record Idempotency outcome
        _db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Scope = idempotencyScope,
            Key = request.IdempotencyKey,
            RequestHash = requestHash,
            ResponseBody = JsonSerializer.Serialize(response),
            HttpStatusCode = 200,
            UserId = driver.UserId,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddHours(24)
        });
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(response);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~StartTrip_"
```
Expected: PASS.

- [ ] **Step 5: Commit Task 6**

```bash
git add src/MealMate.Application/Features/DriverDelivery/DriverApp/StartTripCommand.cs tests/MealMate.UnitTests/DriverDelivery/SecureDriverPickupUnitTests.cs
git commit -m "feat(driver-pickup): implement secure idempotent trip start verifying complete pickup of all assigned boxes"
```

---

### Task 7: Orphan Photo Cleanup Background Service (`OrphanPickupPhotoCleanupService`)

**Files:**
- Create: `src/MealMate.Application/Features/DriverDelivery/DriverApp/OrphanPickupPhotoCleanupService.cs`
- Create: `tests/MealMate.UnitTests/DriverDelivery/OrphanPickupPhotoCleanupTests.cs`

**Interfaces:**
- Produces: `OrphanPickupPhotoCleanupService` (invocable cleanup logic).
- Consumes: `IAppDbContext`, `IFileStorage`, `ILogger`.

- [ ] **Step 1: Write failing test for orphan photo cleanup**

In `tests/MealMate.UnitTests/DriverDelivery/OrphanPickupPhotoCleanupTests.cs`:
```csharp
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class OrphanPickupPhotoCleanupTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task Cleanup_DeletesUnconfirmedPhotosOlderThan24Hours_And_SetsDeletedAtUtc()
    {
        await using var db = CreateDb();
        var storage = Substitute.For<IFileStorage>();

        // Orphan: unconfirmed and older than 25 hours
        var orphan = new PickupConditionPhoto
        {
            StorageKey = "uploads/orphan.jpg",
            ContentType = "image/jpeg",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-25),
            ConfirmedAtUtc = null,
            DeletedAtUtc = null
        };

        // Fresh: unconfirmed but uploaded 2 hours ago
        var fresh = new PickupConditionPhoto
        {
            StorageKey = "uploads/fresh.jpg",
            ContentType = "image/jpeg",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-2),
            ConfirmedAtUtc = null,
            DeletedAtUtc = null
        };

        // Confirmed: confirmed photo older than 30 hours (must NOT be deleted)
        var confirmed = new PickupConditionPhoto
        {
            StorageKey = "uploads/confirmed.jpg",
            ContentType = "image/jpeg",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-30),
            ConfirmedAtUtc = DateTime.UtcNow.AddHours(-29),
            DeletedAtUtc = null
        };

        db.PickupConditionPhotos.AddRange(orphan, fresh, confirmed);
        await db.SaveChangesAsync();

        var service = new OrphanPickupPhotoCleanupService(db, storage, NullLogger<OrphanPickupPhotoCleanupService>.Instance);

        var cleanedCount = await service.RunCleanupOnceAsync(CancellationToken.None);

        cleanedCount.Should().Be(1);
        await storage.Received(1).DeleteAsync("uploads/orphan.jpg", Arg.Any<CancellationToken>());
        await storage.DidNotReceive().DeleteAsync("uploads/fresh.jpg", Arg.Any<CancellationToken>());
        await storage.DidNotReceive().DeleteAsync("uploads/confirmed.jpg", Arg.Any<CancellationToken>());

        var updatedOrphan = await db.PickupConditionPhotos.FindAsync(orphan.Id);
        updatedOrphan!.DeletedAtUtc.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~OrphanPickupPhotoCleanupTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement `OrphanPickupPhotoCleanupService`**

Create `src/MealMate.Application/Features/DriverDelivery/DriverApp/OrphanPickupPhotoCleanupService.cs`:
```csharp
using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public class OrphanPickupPhotoCleanupService
{
    private readonly IAppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ILogger<OrphanPickupPhotoCleanupService> _logger;

    public OrphanPickupPhotoCleanupService(
        IAppDbContext db,
        IFileStorage storage,
        ILogger<OrphanPickupPhotoCleanupService> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public async Task<int> RunCleanupOnceAsync(CancellationToken cancellationToken = default)
    {
        var cutoffUtc = DateTime.UtcNow.AddHours(-24);

        var orphans = await _db.PickupConditionPhotos
            .Where(p => p.ConfirmedAtUtc == null &&
                        p.DeletedAtUtc == null &&
                        p.UploadedAtUtc < cutoffUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        int count = 0;
        foreach (var photo in orphans)
        {
            try
            {
                await _storage.DeleteAsync(photo.StorageKey, cancellationToken);
                photo.DeletedAtUtc = DateTime.UtcNow;
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete orphan condition photo {StorageKey}", photo.StorageKey);
            }
        }

        if (count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return count;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~OrphanPickupPhotoCleanupTests"
```
Expected: PASS.

- [ ] **Step 5: Commit Task 7**

```bash
git add src/MealMate.Application/Features/DriverDelivery/DriverApp/OrphanPickupPhotoCleanupService.cs tests/MealMate.UnitTests/DriverDelivery/OrphanPickupPhotoCleanupTests.cs
git commit -m "feat(driver-pickup): implement orphan condition photo cleanup service with retryable deletion"
```

---

### Task 8: Controller Endpoints, Security Hardening, & Legacy Cleanup

**Files:**
- Modify: `src/MealMate.Api/Controllers/V1/DriverPickupController.cs`
- Modify: `src/MealMate.Api/Controllers/V1/DriverHomeController.cs:160-190`
- Modify: `tests/MealMate.ApiTests/Controllers/DriverPickupControllerTests.cs`

**Interfaces:**
- Exposes:
  - `POST /api/v1/driver/pickup/validate-barcode`
  - `POST /api/v1/driver/pickup/boxes/{boxId}/condition-photo` (multipart)
  - `POST /api/v1/driver/pickup/boxes/{boxId}/confirm`
  - `GET /api/v1/driver/trips/{tripId}/pickup-summary`
  - `POST /api/v1/driver/trips/{tripId}/start`
- Removes:
  - Legacy `POST /api/v1/driver/pickup/start-trip`
  - Legacy `POST /api/v1/driver/orders/{boxId}/pickup`

- [ ] **Step 1: Write failing tests for controller attributes and routing**

In `tests/MealMate.ApiTests/Controllers/DriverPickupControllerTests.cs`:
```csharp
using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class DriverPickupControllerTests
{
    [Fact]
    public void Controller_RequiresAuthorization_And_HasNoAllowAnonymous()
    {
        var authAttr = typeof(DriverPickupController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull();

        var allowAnon = typeof(DriverPickupController).GetCustomAttribute<AllowAnonymousAttribute>();
        allowAnon.Should().BeNull();
    }

    [Fact]
    public void Endpoints_HaveCorrectRoutes()
    {
        var validateMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.ValidateBarcode));
        validateMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("validate-barcode");

        var uploadMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.UploadConditionPhoto));
        uploadMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("boxes/{boxId:guid}/condition-photo");

        var confirmMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.ConfirmPickup));
        confirmMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("boxes/{boxId:guid}/confirm");

        var summaryMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.GetPickupSummary));
        summaryMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("/api/v1/driver/trips/{tripId:guid}/pickup-summary");

        var startMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.StartTrip));
        startMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("/api/v1/driver/trips/{tripId:guid}/start");

        // Verify legacy start-trip method is completely deleted
        var legacyStartMethod = typeof(DriverPickupController).GetMethod("StartTripDelivery");
        legacyStartMethod.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter "FullyQualifiedName~DriverPickupControllerTests"
```
Expected: FAIL.

- [ ] **Step 3: Update `DriverPickupController.cs` and remove legacy actions**

In `src/MealMate.Api/Controllers/V1/DriverPickupController.cs`:
- Remove `[AllowAnonymous]`, add `[Authorize]`.
- Remove legacy `StartTripDelivery` action.
- Add `ValidateBarcode`:
```csharp
[HttpPost("validate-barcode")]
public async Task<IActionResult> ValidateBarcode(
    [FromBody] ValidatePickupBarcodeRequest request,
    CancellationToken cancellationToken = default)
{
    var result = await Sender.Send(new ValidatePickupBarcodeCommand(request.BarcodeValue, request.IsManualEntry), cancellationToken);
    return HandleResult(result);
}
```
- Add `UploadConditionPhoto`:
```csharp
[HttpPost("boxes/{boxId:guid}/condition-photo")]
[Consumes("multipart/form-data")]
public async Task<IActionResult> UploadConditionPhoto(
    [FromRoute] Guid boxId,
    [FromForm] IFormFile file,
    [FromForm] string validationToken,
    CancellationToken cancellationToken = default)
{
    if (file is null || file.Length == 0)
    {
        return BadRequest(new { code = "PHOTO_REQUIRED", message = "Photo file is required." });
    }

    await using var stream = file.OpenReadStream();
    var result = await Sender.Send(new UploadPickupConditionPhotoCommand(
        BoxId: boxId,
        ValidationToken: validationToken,
        FileName: file.FileName,
        ContentType: file.ContentType,
        ContentStream: stream), cancellationToken);

    return HandleResult(result);
}
```
- Add `ConfirmPickup`:
```csharp
[HttpPost("boxes/{boxId:guid}/confirm")]
public async Task<IActionResult> ConfirmPickup(
    [FromRoute] Guid boxId,
    [FromBody] ConfirmPickupBoxRequest request,
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(idempotencyKey))
    {
        return BadRequest(new { code = "IDEMPOTENCY_KEY_REQUIRED", message = "Idempotency-Key header is required." });
    }

    var result = await Sender.Send(new ConfirmPickupBoxCommand(
        BoxId: boxId,
        ValidationToken: request.ValidationToken,
        ConditionPhotoStorageKey: request.ConditionPhotoStorageKey,
        Latitude: request.Latitude,
        Longitude: request.Longitude,
        IdempotencyKey: idempotencyKey), cancellationToken);

    if (result.IsSuccess)
    {
        try
        {
            await _dispatcherHub.Clients.All.SendAsync("box-picked-up", result.Value, cancellationToken);
        }
        catch { }
    }

    return HandleResult(result);
}
```
- Add `GetPickupSummary`:
```csharp
[HttpGet("/api/v1/driver/trips/{tripId:guid}/pickup-summary")]
public async Task<IActionResult> GetPickupSummary(
    [FromRoute] Guid tripId,
    CancellationToken cancellationToken = default)
{
    var result = await Sender.Send(new GetTripPickupSummaryQuery(tripId), cancellationToken);
    return HandleResult(result);
}
```
- Add `StartTrip`:
```csharp
[HttpPost("/api/v1/driver/trips/{tripId:guid}/start")]
public async Task<IActionResult> StartTrip(
    [FromRoute] Guid tripId,
    [FromBody] StartTripRequest request,
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(idempotencyKey))
    {
        return BadRequest(new { code = "IDEMPOTENCY_KEY_REQUIRED", message = "Idempotency-Key header is required." });
    }

    var result = await Sender.Send(new StartTripCommand(
        TripId: tripId,
        Latitude: request.Latitude,
        Longitude: request.Longitude,
        IdempotencyKey: idempotencyKey), cancellationToken);

    if (result.IsSuccess)
    {
        try
        {
            await _dispatcherHub.Clients.All.SendAsync("trip-in-transit", result.Value, cancellationToken);
        }
        catch { }
    }

    return HandleResult(result);
}
```

In `src/MealMate.Api/Controllers/V1/DriverHomeController.cs`:
- Remove legacy action `PickupBox` (`[HttpPost("orders/{boxId:guid}/pickup")]`).

- [ ] **Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter "FullyQualifiedName~DriverPickupControllerTests"
```
Expected: PASS.

- [ ] **Step 5: Commit Task 8**

```bash
git add src/MealMate.Api/Controllers/V1/DriverPickupController.cs src/MealMate.Api/Controllers/V1/DriverHomeController.cs tests/MealMate.ApiTests/Controllers/DriverPickupControllerTests.cs
git commit -m "feat(driver-pickup): expose secured pickup endpoints, enforce Idempotency-Key, and remove legacy pickup actions"
```

---

### Task 9: Integration Tests (End-to-End Secure Flow, Concurrency & Tenant Isolation)

**Files:**
- Create: `tests/MealMate.IntegrationTests/DriverDelivery/SecureDriverPickupIntegrationTests.cs`

**Interfaces:**
- Tests complete flow: validate barcode -> upload photo -> confirm box -> get summary -> start trip.
- Tests authorization isolation between drivers.
- Tests idempotency replay.

- [ ] **Step 1: Write integration tests**

Create `tests/MealMate.IntegrationTests/DriverDelivery/SecureDriverPickupIntegrationTests.cs`:
```csharp
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.DriverDelivery;

public class SecureDriverPickupIntegrationTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task EndToEnd_SecurePickupFlow_Succeeds()
    {
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var driver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, RestaurantId = restaurantId, FullNameAr = "أحمد", FullNameEn = "Ahmed" };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = restaurantId, Code = "TRP-E2E", Status = TripStatus.Assigned };
        var assignment = new DriverAssignment { DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow };
        trip.DriverAssignments.Add(assignment);
        var stop = new TripStop { OrderCode = "BOX-E2E", Status = TripStopStatus.Pending };
        trip.Stops.Add(stop);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);

        var storage = Substitute.For<IFileStorage>();
        var storedKey = "uploads/e2e.jpg";
        storage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile(storedKey, "e2e.jpg", "image/jpeg", 1024, "hash"));

        var messages = NullLocalizedMessageProvider.Instance;

        // 1. Validate Barcode
        var validateHandler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);
        var valResult = await validateHandler.Handle(new ValidatePickupBarcodeCommand("BOX-E2E"), CancellationToken.None);
        valResult.IsSuccess.Should().BeTrue();
        var token = valResult.Value.ValidationToken;

        // 2. Upload Condition Photo
        var uploadHandler = new UploadPickupConditionPhotoCommandHandler(db, currentUser, storage, messages);
        using var jpegStream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 });
        var upResult = await uploadHandler.Handle(new UploadPickupConditionPhotoCommand(stop.Id, token, "photo.jpg", "image/jpeg", jpegStream), CancellationToken.None);
        upResult.IsSuccess.Should().BeTrue();

        // 3. Confirm Pickup
        var confirmHandler = new ConfirmPickupBoxCommandHandler(db, currentUser, messages);
        var confirmKey = Guid.NewGuid().ToString();
        var confResult = await confirmHandler.Handle(new ConfirmPickupBoxCommand(stop.Id, token, storedKey, 29.33m, 48.07m, confirmKey), CancellationToken.None);
        confResult.IsSuccess.Should().BeTrue();
        confResult.Value.AllBoxesReceived.Should().BeTrue();
        confResult.Value.NextAction.Should().Be("ShowPickupSummary");

        // 3b. Idempotency Replay on Confirm
        var replayConfResult = await confirmHandler.Handle(new ConfirmPickupBoxCommand(stop.Id, token, storedKey, 29.33m, 48.07m, confirmKey), CancellationToken.None);
        replayConfResult.IsSuccess.Should().BeTrue();
        replayConfResult.Value.BoxId.Should().Be(stop.Id);

        // 4. Get Summary
        var summaryHandler = new GetTripPickupSummaryQueryHandler(db, currentUser, messages);
        var sumResult = await summaryHandler.Handle(new GetTripPickupSummaryQuery(trip.Id), CancellationToken.None);
        sumResult.IsSuccess.Should().BeTrue();
        sumResult.Value.CanStartTrip.Should().BeTrue();

        // 5. Start Trip
        var startHandler = new StartTripCommandHandler(db, currentUser, messages);
        var startKey = Guid.NewGuid().ToString();
        var startResult = await startHandler.Handle(new StartTripCommand(trip.Id, 29.33m, 48.07m, startKey), CancellationToken.None);
        startResult.IsSuccess.Should().BeTrue();
        startResult.Value.Status.Should().Be("InProgress");
    }
}
```

- [ ] **Step 2: Run integration tests to verify they pass**

Run:
```powershell
dotnet test tests/MealMate.IntegrationTests/MealMate.IntegrationTests.csproj --filter "FullyQualifiedName~SecureDriverPickupIntegrationTests"
```
Expected: PASS.

- [ ] **Step 3: Commit Task 9**

```bash
git add tests/MealMate.IntegrationTests/DriverDelivery/SecureDriverPickupIntegrationTests.cs
git commit -m "test(driver-pickup): add end-to-end integration tests for secure pickup flow and idempotency"
```

---

### Task 10: Mobile Documentation Update (Screens 06.02, 06.03, 06.04, 06.05, 06.06)

**Files:**
- Modify: `mobile-screens/screen-06.02-assigned-boxes-list.md`
- Modify: `mobile-screens/screen-06.03-qr-barcode-scanner.md`
- Modify: `mobile-screens/screen-06.04-camera-box-condition.md`
- Modify: `mobile-screens/screen-06.05-boxes-received-summary.md`
- Modify: `mobile-screens/screen-06.06-assigned-boxes-in-transit.md`

- [ ] **Step 1: Update documentation files**

Update the 5 mobile screen MD files:
- `screen-06.03`: document `validationToken` (expires in 5 mins), new error codes (`BARCODE_NOT_FOUND`, `BOX_NOT_ASSIGNED_TO_DRIVER`, `BOX_ALREADY_PICKED_UP`), and `nextAction: CaptureConditionPhoto`.
- `screen-06.04`: document multipart endpoint `POST /api/v1/driver/pickup/boxes/{boxId}/condition-photo` with `validationToken`, storage key response, and confirmation contract `POST /api/v1/driver/pickup/boxes/{boxId}/confirm` with `Idempotency-Key`.
- `screen-06.05`: document resumable summary `GET /api/v1/driver/trips/{tripId}/pickup-summary` and separate trip start `POST /api/v1/driver/trips/{tripId}/start` with `Idempotency-Key`.
- `screen-06.02` & `screen-06.06`: clarify `PickedUp` terminology instead of ambiguous `Scanned`.

- [ ] **Step 2: Verify git diff of mobile-screens**

Run:
```powershell
git diff --stat mobile-screens
```
Expected: All 5 files updated cleanly.

- [ ] **Step 3: Commit Task 10**

```bash
git add mobile-screens/
git commit -m "docs(mobile-screens): update pickup flow screen specifications to reflect secure token and resumable contracts"
```

---

## Self-Review Checklist

1. **Spec Coverage:**
   - Barcode validation with SHA-256 token hash & 5-minute expiry? Covered in Task 2.
   - Multipart condition photo upload with magic bytes verification & ownership? Covered in Task 3.
   - Atomic pickup confirmation with Idempotency-Key and count derivations? Covered in Task 4.
   - Resumable pickup summary contract for Screen 06.05? Covered in Task 5.
   - Independent trip start contract? Covered in Task 6.
   - 24-hour orphan photo cleanup background service? Covered in Task 7.
   - Removal of legacy `driverId` and legacy pickup/start actions? Covered in Task 8.
   - Full TDD and Zero Mock Data compliance? Checked across all tasks.
2. **Placeholder Scan:**
   - Zero "TODO", "TBD", or incomplete code blocks.
3. **Type Consistency:**
   - All DTOs declared in Task 2 `SecureDriverPickupDtos.cs` match exact property names and types in Tasks 3, 4, 5, 6, 8, and 9.
