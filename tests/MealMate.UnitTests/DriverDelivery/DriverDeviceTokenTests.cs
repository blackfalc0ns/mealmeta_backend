using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class DriverDeviceTokenTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task RegisterDeviceToken_WithRegistrationId_ResolvesSubmittedByUserId()
    {
        // Arrange
        await using var db = CreateDb();
        var submittedUserId = Guid.NewGuid();
        var registration = new DriverRegistration
        {
            SubmittedByUserId = submittedUserId,
            FullNameAr = "محمد أحمد",
            FullNameEn = "Mohamed Ahmed",
            Phone = "+96550000001",
            NationalId = "123456789012",
            VehiclePlate = "12345",
            Status = VerificationStatus.Submitted
        };
        db.DriverRegistrations.Add(registration);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(submittedUserId);
        var messages = NullLocalizedMessageProvider.Instance;

        var handler = new DriverNotificationCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new RegisterDriverDeviceTokenCommand(
            DriverId: null,
            Token: "fcm-test-token-123",
            Platform: "Android",
            DeviceId: "device-xyz",
            RegistrationId: registration.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var savedToken = await db.DriverDeviceTokens.FirstOrDefaultAsync(t => t.Token == "fcm-test-token-123");
        savedToken.Should().NotBeNull();
        savedToken!.UserId.Should().Be(submittedUserId);
        savedToken.Platform.Should().Be(MobilePlatform.Android);
        savedToken.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterDeviceToken_Unauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        await using var db = CreateDb();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);
        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new DriverNotificationCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new RegisterDriverDeviceTokenCommand(
            DriverId: null,
            Token: "fcm-test-token-123",
            Platform: "Android"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("errors.common.unauthorized");
    }

    [Fact]
    public async Task RegisterDeviceToken_OtherUserRegistration_ReturnsForbidden()
    {
        // Arrange
        await using var db = CreateDb();
        var regOwnerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var registration = new DriverRegistration
        {
            SubmittedByUserId = regOwnerId,
            FullNameAr = "محمد أحمد",
            FullNameEn = "Mohamed Ahmed",
            Phone = "+96550000001",
            NationalId = "123456789012",
            VehiclePlate = "12345",
            Status = VerificationStatus.Submitted
        };
        db.DriverRegistrations.Add(registration);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(callerId);
        currentUser.Roles.Returns(Array.Empty<string>());
        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new DriverNotificationCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new RegisterDriverDeviceTokenCommand(
            DriverId: null,
            Token: "fcm-test-token-123",
            Platform: "Android",
            RegistrationId: registration.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("auth.forbidden");
    }

    [Fact]
    public async Task RegisterDeviceToken_Upsert_ReassignsTokenWithoutConflict()
    {
        // Arrange
        await using var db = CreateDb();
        var oldUserId = Guid.NewGuid();
        var newUserId = Guid.NewGuid();

        var existingToken = new DriverDeviceToken
        {
            UserId = oldUserId,
            Token = "shared-device-token",
            Platform = MobilePlatform.Android,
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        db.DriverDeviceTokens.Add(existingToken);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(newUserId);
        var messages = NullLocalizedMessageProvider.Instance;

        var handler = new DriverNotificationCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new RegisterDriverDeviceTokenCommand(
            DriverId: null,
            Token: "shared-device-token",
            Platform: "iOS",
            DeviceId: "iphone-15"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var tokens = await db.DriverDeviceTokens.Where(t => t.Token == "shared-device-token").ToListAsync();
        tokens.Should().HaveCount(1);
        tokens[0].UserId.Should().Be(newUserId);
        tokens[0].Platform.Should().Be(MobilePlatform.Ios);
        tokens[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UnregisterDeviceToken_SetsIsActiveToFalse()
    {
        // Arrange
        await using var db = CreateDb();
        var ownerId = Guid.NewGuid();
        var token = new DriverDeviceToken
        {
            UserId = ownerId,
            Token = "token-to-deactivate",
            Platform = MobilePlatform.Android,
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow
        };
        db.DriverDeviceTokens.Add(token);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(ownerId);
        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new DriverNotificationCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new UnregisterDriverDeviceTokenCommand("token-to-deactivate"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var updated = await db.DriverDeviceTokens.FirstOrDefaultAsync(t => t.Token == "token-to-deactivate");
        updated.Should().NotBeNull();
        updated!.IsActive.Should().BeFalse();
    }
}
