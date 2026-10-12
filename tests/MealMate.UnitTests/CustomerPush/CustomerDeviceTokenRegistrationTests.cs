using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerPush;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerPush;

/// <summary>
/// Customer device-token registration must return the persistent registry id
/// (fcmDeviceTokenId) that VoiceCalls v2 device sessions bind push routes to.
/// </summary>
public class CustomerDeviceTokenRegistrationTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static RegisterCustomerDeviceTokenCommandHandler CreateHandler(AppDbContext db, Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return new RegisterCustomerDeviceTokenCommandHandler(db, currentUser, clock);
    }

    [Fact]
    public async Task RegisterDeviceToken_NewToken_ReturnsRegistryId()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var handler = CreateHandler(db, userId);

        var result = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("fcm-customer-token-1", MobilePlatform.Android, "device-1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FcmDeviceTokenId.Should().NotBeEmpty();

        var saved = await db.CustomerDeviceTokens.SingleAsync(t => t.Token == "fcm-customer-token-1");
        result.Value.FcmDeviceTokenId.Should().Be(saved.Id);
        saved.UserId.Should().Be(userId);
        saved.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterDeviceToken_SameTokenRepeated_ReturnsSameRegistryId()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var handler = CreateHandler(db, userId);

        var first = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("fcm-customer-token-2", MobilePlatform.Android, "device-2"),
            CancellationToken.None);
        var second = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("fcm-customer-token-2", MobilePlatform.Android, "device-2"),
            CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.FcmDeviceTokenId.Should().Be(first.Value.FcmDeviceTokenId);
        db.CustomerDeviceTokens.Count(t => t.Token == "fcm-customer-token-2").Should().Be(1);
    }

    [Fact]
    public async Task RegisterDeviceToken_ReactivatedToken_KeepsOriginalRegistryId()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var handler = CreateHandler(db, userId);

        var first = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("fcm-customer-token-3", MobilePlatform.Ios, "device-3"),
            CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        var row = await db.CustomerDeviceTokens.SingleAsync(t => t.Token == "fcm-customer-token-3");
        row.IsActive = false;
        await db.SaveChangesAsync();

        var second = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("fcm-customer-token-3", MobilePlatform.Ios, "device-3"),
            CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        second.Value.FcmDeviceTokenId.Should().Be(first.Value.FcmDeviceTokenId);
        (await db.CustomerDeviceTokens.SingleAsync(t => t.Token == "fcm-customer-token-3")).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterDeviceToken_Unauthenticated_ReturnsUnauthorized()
    {
        await using var db = CreateDb();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var handler = new RegisterCustomerDeviceTokenCommandHandler(db, currentUser, clock);

        var result = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("fcm-customer-token-4", MobilePlatform.Android, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("auth.invalid_credentials");
    }

    [Fact]
    public async Task Refresh_rebinds_only_the_matching_live_installation()
    {
        await using var db = CreateDb();
        var user = Guid.NewGuid();
        var handler = CreateHandler(db, user);
        var first = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand(
                "old-token", MobilePlatform.Android, "installation-a"), default);
        var session = new MealMate.Domain.VoiceCalls.VoiceCallDeviceSession
        {
            UserId = user, InstallationId = "installation-a", Platform = "android",
            FcmDeviceTokenId = first.Value.FcmDeviceTokenId,
            ControlProofHash = new string('a', 64),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        };
        db.VoiceCallDeviceSessions.Add(session);
        await db.SaveChangesAsync();
        var originalId = session.Id;
        var originalProof = session.ControlProofHash;

        var second = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand(
                "new-token", MobilePlatform.Android, "installation-a"), default);

        second.IsSuccess.Should().BeTrue();
        session.FcmDeviceTokenId.Should().Be(second.Value.FcmDeviceTokenId);
        session.Id.Should().Be(originalId);
        session.ControlProofHash.Should().Be(originalProof);
    }

    [Fact]
    public async Task Repeated_same_token_registration_keeps_the_binding_and_registry_id()
    {
        await using var db = CreateDb();
        var user = Guid.NewGuid();
        var handler = CreateHandler(db, user);
        var first = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("refresh-token", MobilePlatform.Android, "installation-r"), default);
        var session = new MealMate.Domain.VoiceCalls.VoiceCallDeviceSession
        {
            UserId = user, InstallationId = "installation-r", Platform = "android",
            FcmDeviceTokenId = first.Value.FcmDeviceTokenId,
            ControlProofHash = new string('b', 64),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        };
        db.VoiceCallDeviceSessions.Add(session);
        await db.SaveChangesAsync();

        var second = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("refresh-token", MobilePlatform.Android, "installation-r"), default);

        second.Value.FcmDeviceTokenId.Should().Be(first.Value.FcmDeviceTokenId);
        session.FcmDeviceTokenId.Should().Be(first.Value.FcmDeviceTokenId);
    }

    [Fact]
    public async Task Refresh_does_not_rebind_sessions_of_another_installation_platform_or_account()
    {
        await using var db = CreateDb();
        var user = Guid.NewGuid();
        var handler = CreateHandler(db, user);
        var first = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("base-token", MobilePlatform.Android, "installation-a"), default);
        var matching = NewLiveSession(user, "installation-a", "android", first.Value.FcmDeviceTokenId);
        var otherInstallation = NewLiveSession(user, "installation-b", "android", first.Value.FcmDeviceTokenId);
        var otherPlatform = NewLiveSession(user, "installation-a", "ios", first.Value.FcmDeviceTokenId);
        var otherAccount = NewLiveSession(Guid.NewGuid(), "installation-a", "android", first.Value.FcmDeviceTokenId);
        var revoked = NewLiveSession(user, "installation-a", "android", first.Value.FcmDeviceTokenId);
        revoked.RevokedAtUtc = DateTime.UtcNow;
        var expired = NewLiveSession(user, "installation-a", "android", first.Value.FcmDeviceTokenId);
        expired.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        db.VoiceCallDeviceSessions.AddRange(matching, otherInstallation, otherPlatform, otherAccount, revoked, expired);
        await db.SaveChangesAsync();
        var staleRoute = matching.FcmDeviceTokenId;

        var second = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("fresh-token", MobilePlatform.Android, "installation-a"), default);

        second.IsSuccess.Should().BeTrue();
        matching.FcmDeviceTokenId.Should().Be(second.Value.FcmDeviceTokenId);
        otherInstallation.FcmDeviceTokenId.Should().Be(staleRoute, "a different installation is never inferred");
        otherPlatform.FcmDeviceTokenId.Should().Be(staleRoute, "platform must match explicitly");
        otherAccount.FcmDeviceTokenId.Should().Be(staleRoute, "another account's session is never touched");
        revoked.FcmDeviceTokenId.Should().Be(staleRoute, "revoked sessions are dead and stay untouched");
        expired.FcmDeviceTokenId.Should().Be(staleRoute, "expired sessions are dead and stay untouched");
    }

    [Fact]
    public async Task Registration_without_device_id_never_rebinds_any_session()
    {
        await using var db = CreateDb();
        var user = Guid.NewGuid();
        var handler = CreateHandler(db, user);
        var first = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("seed-token", MobilePlatform.Android, "installation-a"), default);
        var session = NewLiveSession(user, "installation-a", "android", first.Value.FcmDeviceTokenId);
        db.VoiceCallDeviceSessions.Add(session);
        await db.SaveChangesAsync();

        var second = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("no-install-token", MobilePlatform.Android, null), default);

        second.IsSuccess.Should().BeTrue();
        session.FcmDeviceTokenId.Should().NotBe(second.Value.FcmDeviceTokenId,
            "without an explicit installation match the binding must not be inferred");
    }

    [Fact]
    public async Task Token_reassigned_to_a_new_account_detaches_the_previous_owner_sessions()
    {
        await using var db = CreateDb();
        var previousOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var handler = CreateHandler(db, previousOwner);
        var first = await handler.Handle(
            new RegisterCustomerDeviceTokenCommand("moving-token", MobilePlatform.Android, "installation-a"), default);
        var previousOwnerSession = NewLiveSession(previousOwner, "installation-a", "android", first.Value.FcmDeviceTokenId);
        db.VoiceCallDeviceSessions.Add(previousOwnerSession);
        await db.SaveChangesAsync();

        var claimant = CreateHandler(db, newOwner);
        var second = await claimant.Handle(
            new RegisterCustomerDeviceTokenCommand("moving-token", MobilePlatform.Android, "installation-a"), default);

        second.IsSuccess.Should().BeTrue();
        second.Value.FcmDeviceTokenId.Should().Be(first.Value.FcmDeviceTokenId,
            "re-registering the same token reactivates the same registry row");
        previousOwnerSession.FcmDeviceTokenId.Should().BeNull(
            "the previous account's session must not keep targeting a token it no longer owns");
    }

    private static MealMate.Domain.VoiceCalls.VoiceCallDeviceSession NewLiveSession(
        Guid userId, string installationId, string platform, Guid? fcmDeviceTokenId) =>
        new()
        {
            UserId = userId,
            InstallationId = installationId,
            Platform = platform,
            FcmDeviceTokenId = fcmDeviceTokenId,
            ControlProofHash = new string('c', 64),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        };
}
