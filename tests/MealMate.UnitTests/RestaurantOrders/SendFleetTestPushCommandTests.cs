using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.RestaurantPush;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOrders;

public sealed class SendFleetTestPushCommandTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task SendDispatcherTestPush_Returns_NotFound_When_No_Restaurant_Exists()
    {
        await using var db = CreateInMemoryDbContext();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var pushService = Substitute.For<IRestaurantPushTestService>();
        var handler = new SendTestNotificationToFleetCommandHandlers(db, currentUser, pushService);

        var result = await handler.Handle(new SendDispatcherTestPushCommand(RestaurantId: Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Orders.NotFound);
        await pushService.DidNotReceive().SendTestToDispatcherAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendDispatcherTestPush_Sends_Successfully_When_Restaurant_Found()
    {
        await using var db = CreateInMemoryDbContext();
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            TradeNameAr = "مطعم الاختبار",
            TradeNameEn = "Test Restaurant",
            LegalNameAr = "شركة الاختبار",
            LegalNameEn = "Test Company"
        };
        db.RestaurantProfiles.Add(restaurant);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(restaurant.UserId);

        var pushService = Substitute.For<IRestaurantPushTestService>();
        var expectedResult = new TestPushDeliveryResultDto(true, "Dispatcher", 1, "تم إرسال الإشعار بنجاح", null);
        pushService.SendTestToDispatcherAsync(restaurant.Id, Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var handler = new SendTestNotificationToFleetCommandHandlers(db, currentUser, pushService);

        var result = await handler.Handle(new SendDispatcherTestPushCommand(restaurant.Id, null, "رسالة تجريبية"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResult);
        await pushService.Received(1).SendTestToDispatcherAsync(restaurant.Id, null, "رسالة تجريبية", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendDriverTestPush_Sends_Successfully_When_Restaurant_Found()
    {
        await using var db = CreateInMemoryDbContext();
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            TradeNameAr = "مطعم الاختبار",
            TradeNameEn = "Test Restaurant",
            LegalNameAr = "شركة الاختبار",
            LegalNameEn = "Test Company"
        };
        db.RestaurantProfiles.Add(restaurant);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(restaurant.UserId);

        var driverId = Guid.NewGuid();
        var pushService = Substitute.For<IRestaurantPushTestService>();
        var expectedResult = new TestPushDeliveryResultDto(true, "Driver", 2, "تم إرسال الإشعار إلى سائقين", null);
        pushService.SendTestToDriverAsync(restaurant.Id, driverId, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var handler = new SendTestNotificationToFleetCommandHandlers(db, currentUser, pushService);

        var result = await handler.Handle(new SendDriverTestPushCommand(restaurant.Id, driverId, "طلب جديد جاهز"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResult);
        await pushService.Received(1).SendTestToDriverAsync(restaurant.Id, driverId, "طلب جديد جاهز", Arg.Any<CancellationToken>());
    }
}
