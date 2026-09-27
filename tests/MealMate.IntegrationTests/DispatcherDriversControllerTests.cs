using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
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
        // Arrange
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
}
