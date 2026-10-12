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
        result.Value.Should().NotBeNull();
        result.Value!.FcmDeviceTokenId.Should().NotBeEmpty();

        var savedToken = await db.DriverDeviceTokens.FirstOrDefaultAsync(t => t.Token == "fcm-test-token-123");
        savedToken.Should().NotBeNull();
        result.Value.FcmDeviceTokenId.Should().Be(savedToken!.Id);
        savedToken.UserId.Should().Be(submittedUserId);
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
        result.Value.Should().NotBeNull();

        var tokens = await db.DriverDeviceTokens.Where(t => t.Token == "shared-device-token").ToListAsync();
        tokens.Should().HaveCount(1);
        tokens[0].UserId.Should().Be(newUserId);
        tokens[0].Platform.Should().Be(MobilePlatform.Ios);
        tokens[0].IsActive.Should().BeTrue();
        result.Value!.FcmDeviceTokenId.Should().Be(tokens[0].Id);
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

    private static MealMate.Domain.VoiceCalls.VoiceCallDeviceSession NewLiveSession(
        Guid userId, string installationId, Guid? routeId) =>
        new()
        {
            UserId = userId,
            InstallationId = installationId,
            Platform = "android",
            FcmDeviceTokenId = routeId,
            ControlProofHash = new string('a', 64),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        };

    private static async Task<(MealMate.Domain.IdentityAccess.DriverDeviceToken Route, MealMate.Domain.VoiceCalls.VoiceCallDeviceSession Session)> SeedBoundRouteAsync(
        AppDbContext db, Guid userId, string tokenText, string installation)
    {
        var route = new DriverDeviceToken
        {
            UserId = userId,
            Token = tokenText,
            Platform = MobilePlatform.Android,
            DeviceId = installation,
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow
        };
        db.DriverDeviceTokens.Add(route);
        var session = NewLiveSession(userId, installation, route.Id);
        db.VoiceCallDeviceSessions.Add(session);
        await db.SaveChangesAsync();
        return (route, session);
    }

    private static DriverNotificationCommandHandlers CreateHandler(AppDbContext db, Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId);
        return new(db, currentUser, NullLocalizedMessageProvider.Instance);
    }

    [Fact]
    public async Task Driver_refresh_without_explicit_deviceId_never_rebinds_voice_sessions()
    {
        await using var db = CreateDb();
        var user = Guid.NewGuid();
        var (route, session) = await SeedBoundRouteAsync(db, user, "driver-old-token", "driver-install-1");

        // The registry row remembers an installation, but this request carries none —
        // the remembered value must never be enough to move a live binding.
        var result = await CreateHandler(db, user).Handle(
            new RegisterDriverDeviceTokenCommand(DriverId: null, Token: "driver-old-token", Platform: "Android", DeviceId: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.FcmDeviceTokenId.Should().Be(route.Id, "no explicit deviceId in the request means no rebind");
    }

    [Fact]
    public async Task Driver_refresh_with_blank_deviceId_never_rebinds_voice_sessions()
    {
        await using var db = CreateDb();
        var user = Guid.NewGuid();
        var (route, session) = await SeedBoundRouteAsync(db, user, "driver-blank-token", "driver-install-2");

        var result = await CreateHandler(db, user).Handle(
            new RegisterDriverDeviceTokenCommand(DriverId: null, Token: "driver-blank-token", Platform: "Android", DeviceId: "   "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.FcmDeviceTokenId.Should().Be(route.Id);
    }

    [Fact]
    public async Task Driver_refresh_with_explicit_deviceId_rebinds_the_matching_live_session()
    {
        await using var db = CreateDb();
        var user = Guid.NewGuid();
        var (route, session) = await SeedBoundRouteAsync(db, user, "driver-explicit-token", "driver-install-3");
        var originalSessionId = session.Id;

        var result = await CreateHandler(db, user).Handle(
            new RegisterDriverDeviceTokenCommand(DriverId: null, Token: "driver-explicit-token", Platform: "Android", DeviceId: "driver-install-3"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FcmDeviceTokenId.Should().Be(route.Id);
        session.FcmDeviceTokenId.Should().Be(route.Id,
            "re-registering the same token keeps the binding on the same registry row");
        session.Id.Should().Be(originalSessionId, "session identity is never rotated by a route refresh");
    }

    [Fact]
    public async Task Driver_new_token_with_explicit_deviceId_moves_the_live_binding_to_the_new_route()
    {
        await using var db = CreateDb();
        var user = Guid.NewGuid();
        var (_, session) = await SeedBoundRouteAsync(db, user, "driver-superseded", "driver-install-4");

        var result = await CreateHandler(db, user).Handle(
            new RegisterDriverDeviceTokenCommand(DriverId: null, Token: "driver-fresh", Platform: "Android", DeviceId: "driver-install-4"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.FcmDeviceTokenId.Should().Be(result.Value.FcmDeviceTokenId);
        var superseded = await db.DriverDeviceTokens.SingleAsync(t => t.Token == "driver-superseded");
        superseded.IsActive.Should().BeFalse("the same installation's previous route is deactivated");
    }

    [Fact]
    public async Task Driver_refresh_never_rebinds_another_accounts_sessions()
    {
        await using var db = CreateDb();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var (route, otherSession) = await SeedBoundRouteAsync(db, owner, "driver-claimed-token", "driver-install-5");
        var ownerSession = NewLiveSession(owner, "driver-install-5", route.Id);
        db.VoiceCallDeviceSessions.Add(ownerSession);
        await db.SaveChangesAsync();

        var result = await CreateHandler(db, other).Handle(
            new RegisterDriverDeviceTokenCommand(DriverId: null, Token: "driver-claimed-token", Platform: "Android", DeviceId: "driver-install-5"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        otherSession.FcmDeviceTokenId.Should().BeNull(
            "the previous account's session stops targeting the token it no longer owns");
        ownerSession.FcmDeviceTokenId.Should().BeNull(
            "every session of the previous owner stops targeting the claimed token");
    }
}
