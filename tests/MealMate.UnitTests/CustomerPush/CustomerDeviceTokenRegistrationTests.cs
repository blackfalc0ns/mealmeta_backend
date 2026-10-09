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
}
