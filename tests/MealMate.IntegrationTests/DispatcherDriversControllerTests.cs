using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests;

public class DispatcherDriversControllerTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task UpdateDriverAvailability_PersistsToDatabase_And_LogsAuditEntry()
    {
        // Arrange — an operating driver (active session): the availability toggle
        // remains the readiness switch for drivers who are already operational.
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            RestaurantId = restaurantId,
            FullNameAr = "محمد عبد الله",
            FullNameEn = "Mohammed Abdullah",
            IsAvailable = false
        };
        db.DriverProfiles.Add(driver);
        db.DriverWorkSessions.Add(new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            RestaurantId = restaurantId,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            Status = "Active"
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(restaurantId);
        currentUser.IsInRole(AppRoles.DeliveryManager).Returns(true);

        var audit = Substitute.For<IAuditLogger>();
        var messages = NullLocalizedMessageProvider.Instance;

        var handler = new UpdateDispatcherDriverAvailabilityCommandHandler(db, currentUser, audit, messages);

        // Act
        var command = new UpdateDispatcherDriverAvailabilityCommand(
            DriverId: driver.Id,
            IsAvailable: true,
            Reason: "Driver resumed duty after shift break");

        var result = await handler.Handle(command, CancellationToken.None);

        // Assert: Success
        result.IsSuccess.Should().BeTrue();
        result.Value.IsAvailable.Should().BeTrue();
        result.Value.OperationalStatus.Should().Be("Available");

        // Assert: Persistence in database
        var persisted = await db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driver.Id);
        persisted.Should().NotBeNull();
        persisted!.IsAvailable.Should().BeTrue();
        persisted.ModifiedAtUtc.Should().NotBeNull();

        // Assert: Audit log
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e =>
                e.Action == "UpdateAvailability" &&
                e.EntityType == nameof(DriverProfile) &&
                e.EntityId == driver.Id &&
                e.Reason == "Driver resumed duty after shift break" &&
                e.SourceChannel == "DispatcherApp"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateDriverAvailability_CrossTenant_Returns_NotFound_And_DoesNotMutate()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantA = Guid.NewGuid();
        var restaurantB = Guid.NewGuid();

        var driver = new DriverProfile
        {
            RestaurantId = restaurantA,
            FullNameAr = "أحمد خالد",
            FullNameEn = "Ahmed Khaled",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(restaurantB); // Different tenant!
        currentUser.IsInRole(AppRoles.DeliveryManager).Returns(true);

        var audit = Substitute.For<IAuditLogger>();
        var messages = NullLocalizedMessageProvider.Instance;

        var handler = new UpdateDispatcherDriverAvailabilityCommandHandler(db, currentUser, audit, messages);

        // Act
        var command = new UpdateDispatcherDriverAvailabilityCommand(
            DriverId: driver.Id,
            IsAvailable: false,
            Reason: "Cross tenant attempt");

        var result = await handler.Handle(command, CancellationToken.None);

        // Assert: 404 Not Found
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DRIVER_NOT_FOUND");

        // Database unchanged
        var persisted = await db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driver.Id);
        persisted!.IsAvailable.Should().BeTrue();

        // No audit entry
        await audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateDriverAvailability_Unauthenticated_Returns_Unauthorized()
    {
        // Arrange
        await using var db = CreateDb();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);

        var audit = Substitute.For<IAuditLogger>();
        var messages = NullLocalizedMessageProvider.Instance;

        var handler = new UpdateDispatcherDriverAvailabilityCommandHandler(db, currentUser, audit, messages);

        // Act
        var result = await handler.Handle(
            new UpdateDispatcherDriverAvailabilityCommand(Guid.NewGuid(), true),
            CancellationToken.None);

        // Assert: Unauthorized
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(MealMate.Application.Common.Models.ErrorType.Unauthorized);
    }

    [Fact]
    public async Task UpdateDriverAvailability_WithoutPermission_Returns_Forbidden()
    {
        // Arrange
        await using var db = CreateDb();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.IsInRole(Arg.Any<string>()).Returns(false);
        currentUser.HasPermission(PermissionCodes.DeliveryDriversManage).Returns(false);

        var audit = Substitute.For<IAuditLogger>();
        var messages = NullLocalizedMessageProvider.Instance;

        var handler = new UpdateDispatcherDriverAvailabilityCommandHandler(db, currentUser, audit, messages);

        // Act
        var result = await handler.Handle(
            new UpdateDispatcherDriverAvailabilityCommand(Guid.NewGuid(), true),
            CancellationToken.None);

        // Assert: Forbidden
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(MealMate.Application.Common.Models.ErrorType.Forbidden);
    }

    [Fact]
    public async Task GetDriverDetails_CrossTenant_Returns_NotFound()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantA = Guid.NewGuid();
        var restaurantB = Guid.NewGuid();

        var driver = new DriverProfile
        {
            RestaurantId = restaurantA,
            FullNameAr = "سائق مطعم أ",
            FullNameEn = "Restaurant A Driver",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(restaurantB); // Cross tenant!

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new GetDispatcherDriverDetailsQueryHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDispatcherDriverDetailsQuery(driver.Id), CancellationToken.None);

        // Assert: 404 Not Found
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Dispatcher.DriverNotFound);
    }

    #region Supervisor-managed operational status (shift-status)

    private static (ICurrentUser User, IAuditLogger Audit) SetupSupervisor(Guid restaurantId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(restaurantId);
        currentUser.IsInRole(AppRoles.DeliveryManager).Returns(true);
        return (currentUser, Substitute.For<IAuditLogger>());
    }

    private static DriverProfile CreateInactiveDriver(AppDbContext db, Guid restaurantId)
    {
        var driver = new DriverProfile
        {
            RestaurantId = restaurantId,
            FullNameAr = "سائق التشغيل",
            FullNameEn = "Operational Driver",
            IsAvailable = false,
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(driver);
        return driver;
    }

    [Fact]
    public async Task UpdateShiftStatus_Activate_OpensSingleSession_MakesAvailable_AndIsIdempotent()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = CreateInactiveDriver(db, restaurantId);
        await db.SaveChangesAsync();
        var (currentUser, audit) = SetupSupervisor(restaurantId);
        var handler = new UpdateDispatcherDriverShiftStatusCommandHandler(db, currentUser, audit, NullLocalizedMessageProvider.Instance);

        // Act — first activation
        var first = await handler.Handle(
            new UpdateDispatcherDriverShiftStatusCommand(driver.Id, "Active", "بدء الوردية بواسطة مسؤول التوصيل"),
            CancellationToken.None);

        // Assert
        first.IsSuccess.Should().BeTrue();
        first.Value.ShiftStatus.Should().Be("Active");
        first.Value.IsAvailable.Should().BeTrue();
        first.Value.CurrentStatusText.Should().Be("Online and available for new orders");

        var persisted = await db.DriverProfiles.AsNoTracking().FirstAsync(d => d.Id == driver.Id);
        persisted.IsAvailable.Should().BeTrue();

        var sessions = await db.DriverWorkSessions.AsNoTracking()
            .Where(s => s.DriverProfileId == driver.Id).ToListAsync();
        sessions.Should().HaveCount(1);
        var session = sessions.Single();
        session.Status.Should().Be("Active");
        session.RestaurantId.Should().Be(restaurantId);

        // Act — repeated activation must converge, not duplicate
        var startedAt = session.StartedAtUtc;
        var second = await handler.Handle(
            new UpdateDispatcherDriverShiftStatusCommand(driver.Id, "Active"),
            CancellationToken.None);

        // Assert
        second.IsSuccess.Should().BeTrue();
        second.Value.ShiftStatus.Should().Be("Active");
        var sessionsAfter = await db.DriverWorkSessions.AsNoTracking()
            .Where(s => s.DriverProfileId == driver.Id).ToListAsync();
        sessionsAfter.Should().HaveCount(1, "repeated activation must not open a second session");
        sessionsAfter.Single().StartedAtUtc.Should().Be(startedAt);

        await audit.Received().LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "UpdateShiftStatus" && e.EntityId == driver.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateShiftStatus_Activate_UnapprovedDriver_ReturnsForbidden()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = CreateInactiveDriver(db, restaurantId);
        driver.VerificationStatus = VerificationStatus.Draft; // not approved yet
        await db.SaveChangesAsync();
        var (currentUser, audit) = SetupSupervisor(restaurantId);
        var handler = new UpdateDispatcherDriverShiftStatusCommandHandler(db, currentUser, audit, NullLocalizedMessageProvider.Instance);

        // Act
        var result = await handler.Handle(
            new UpdateDispatcherDriverShiftStatusCommand(driver.Id, "Active"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.NotApproved");
        (await db.DriverWorkSessions.Where(s => s.DriverProfileId == driver.Id).CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateShiftStatus_Activate_CrossTenant_ReturnsNotFound()
    {
        // Arrange
        await using var db = CreateDb();
        var driver = CreateInactiveDriver(db, Guid.NewGuid());
        await db.SaveChangesAsync();
        var (currentUser, audit) = SetupSupervisor(Guid.NewGuid()); // another restaurant
        var handler = new UpdateDispatcherDriverShiftStatusCommandHandler(db, currentUser, audit, NullLocalizedMessageProvider.Instance);

        // Act
        var result = await handler.Handle(
            new UpdateDispatcherDriverShiftStatusCommand(driver.Id, "Active"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DRIVER_NOT_FOUND");
    }

    [Fact]
    public async Task UpdateShiftStatus_InvalidValue_ReturnsValidationError()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = CreateInactiveDriver(db, restaurantId);
        await db.SaveChangesAsync();
        var (currentUser, audit) = SetupSupervisor(restaurantId);
        var handler = new UpdateDispatcherDriverShiftStatusCommandHandler(db, currentUser, audit, NullLocalizedMessageProvider.Instance);

        // Act
        var result = await handler.Handle(
            new UpdateDispatcherDriverShiftStatusCommand(driver.Id, "Online"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DRIVER_SHIFT_STATUS_INVALID");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task UpdateShiftStatus_Deactivate_WithActiveOrders_ReturnsConflict_AndKeepsSession()
    {
        // Arrange — operating driver holding a picked-up box
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = CreateInactiveDriver(db, restaurantId);
        db.DriverWorkSessions.Add(new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            RestaurantId = restaurantId,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            Status = "Active"
        });
        driver.IsAvailable = true;

        var trip = new Trip { RestaurantId = restaurantId, Code = "TRP-771", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = TripStatus.InProgress };
        db.Trips.Add(trip);
        db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" });
        db.TripStops.Add(new TripStop
        {
            TripId = trip.Id, OrderCode = "#BX-7701", CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "09:30 ص",
            Status = TripStopStatus.PickedUp
        });
        await db.SaveChangesAsync();
        var (currentUser, audit) = SetupSupervisor(restaurantId);
        var handler = new UpdateDispatcherDriverShiftStatusCommandHandler(db, currentUser, audit, NullLocalizedMessageProvider.Instance);

        // Act
        var result = await handler.Handle(
            new UpdateDispatcherDriverShiftStatusCommand(driver.Id, "Inactive"),
            CancellationToken.None);

        // Assert — 409 and nothing silently dropped
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DRIVER_SHIFT_ACTIVE_ORDERS");
        result.Error.Type.Should().Be(ErrorType.Conflict);

        var session = await db.DriverWorkSessions.AsNoTracking().FirstAsync(s => s.DriverProfileId == driver.Id);
        session.Status.Should().Be("Active");
        session.EndedAtUtc.Should().BeNull();
        var persisted = await db.DriverProfiles.AsNoTracking().FirstAsync(d => d.Id == driver.Id);
        persisted.IsAvailable.Should().BeTrue();
        await audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateShiftStatus_Deactivate_AfterOrdersComplete_ClosesSession_AndClearsAvailability()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = CreateInactiveDriver(db, restaurantId);
        db.DriverWorkSessions.Add(new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            RestaurantId = restaurantId,
            StartedAtUtc = DateTime.UtcNow.AddHours(-3),
            Status = "Active"
        });
        driver.IsAvailable = true;
        await db.SaveChangesAsync();
        var (currentUser, audit) = SetupSupervisor(restaurantId);
        var handler = new UpdateDispatcherDriverShiftStatusCommandHandler(db, currentUser, audit, NullLocalizedMessageProvider.Instance);

        // Act
        var result = await handler.Handle(
            new UpdateDispatcherDriverShiftStatusCommand(driver.Id, "Inactive", "نهاية الوردية"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ShiftStatus.Should().Be("Inactive");
        result.Value.IsAvailable.Should().BeFalse();
        result.Value.CurrentStatusText.Should().Be("Offline");

        var session = await db.DriverWorkSessions.AsNoTracking().FirstAsync(s => s.DriverProfileId == driver.Id);
        session.Status.Should().Be("Ended");
        session.EndedAtUtc.Should().NotBeNull();

        var persisted = await db.DriverProfiles.AsNoTracking().FirstAsync(d => d.Id == driver.Id);
        persisted.IsAvailable.Should().BeFalse();

        await audit.Received().LogAsync(
            Arg.Is<AuditEntry>(e =>
                e.Action == "UpdateShiftStatus" &&
                e.Reason == "نهاية الوردية" &&
                e.SourceChannel == "DispatcherApp"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateShiftStatus_Deactivate_LegacyDriverWithoutSession_ClearsAvailabilityOnly()
    {
        // Arrange — legacy record: IsAvailable=true with no work session at all
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = CreateInactiveDriver(db, restaurantId);
        driver.IsAvailable = true;
        await db.SaveChangesAsync();
        var (currentUser, audit) = SetupSupervisor(restaurantId);
        var handler = new UpdateDispatcherDriverShiftStatusCommandHandler(db, currentUser, audit, NullLocalizedMessageProvider.Instance);

        // Act
        var result = await handler.Handle(
            new UpdateDispatcherDriverShiftStatusCommand(driver.Id, "Inactive"),
            CancellationToken.None);

        // Assert — converges to the consistent inactive state without inventing sessions
        result.IsSuccess.Should().BeTrue();
        result.Value.ShiftStatus.Should().Be("Inactive");
        (await db.DriverWorkSessions.Where(s => s.DriverProfileId == driver.Id).CountAsync()).Should().Be(0);
        var persisted = await db.DriverProfiles.AsNoTracking().FirstAsync(d => d.Id == driver.Id);
        persisted.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateShiftStatus_Unauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        await using var db = CreateDb();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);
        var handler = new UpdateDispatcherDriverShiftStatusCommandHandler(db, currentUser, Substitute.For<IAuditLogger>(), NullLocalizedMessageProvider.Instance);

        // Act
        var result = await handler.Handle(
            new UpdateDispatcherDriverShiftStatusCommand(Guid.NewGuid(), "Active"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task UpdateAvailability_Activate_OnInactiveDriver_ReturnsConflict()
    {
        // Arrange — the availability toggle must not be a side door into activation
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = CreateInactiveDriver(db, restaurantId);
        await db.SaveChangesAsync();
        var (currentUser, audit) = SetupSupervisor(restaurantId);

        var handler = new UpdateDispatcherDriverAvailabilityCommandHandler(
            db, currentUser, audit, NullLocalizedMessageProvider.Instance);

        // Act
        var result = await handler.Handle(
            new UpdateDispatcherDriverAvailabilityCommand(driver.Id, true, "try to re-activate via availability"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DRIVER_NOT_OPERATIONAL");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        var persisted = await db.DriverProfiles.AsNoTracking().FirstAsync(d => d.Id == driver.Id);
        persisted.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAvailability_Activate_OnActiveDriver_StillSucceeds()
    {
        // Arrange — operating driver (active session): availability toggle keeps working
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = CreateInactiveDriver(db, restaurantId);
        db.DriverWorkSessions.Add(new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            RestaurantId = restaurantId,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            Status = "Active"
        });
        driver.IsAvailable = false;
        await db.SaveChangesAsync();
        var (currentUser, audit) = SetupSupervisor(restaurantId);

        var handler = new UpdateDispatcherDriverAvailabilityCommandHandler(
            db, currentUser, audit, NullLocalizedMessageProvider.Instance);

        // Act
        var result = await handler.Handle(
            new UpdateDispatcherDriverAvailabilityCommand(driver.Id, true),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsAvailable.Should().BeTrue();
    }

    #endregion
}
