using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class DriverConnectionStatusTests
{
    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    private static ICurrentUser Supervisor(Guid restaurantId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.RestaurantId.Returns(restaurantId);
        user.HasPermission(PermissionCodes.DeliveryDispatchManage).Returns(true);
        user.HasPermission(PermissionCodes.DeliveryDriversManage).Returns(true);
        return user;
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task HomeListAndDetails_ReportTheSameConnectionStatus(bool hasSession, bool available)
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { RestaurantId = Guid.NewGuid(), UserId = Guid.NewGuid(),
            FullNameAr = "Ø³Ø§Ø¦Ù‚ Ø§Ù„Ø§Ø®ØªØ¨Ø§Ø±", FullNameEn = "Test driver", IsAvailable = available };
        db.DriverProfiles.Add(driver);
        if (hasSession)
            db.DriverWorkSessions.Add(new DriverWorkSession { DriverProfileId = driver.Id, Status = "Active", StartedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var messages = NullLocalizedMessageProvider.Instance;
        var user = Supervisor(driver.RestaurantId!.Value);
        var home = await DriverHomeScreenBuilder.BuildAsync(db, messages, driver, default);
        var list = await new GetDispatcherDriversQueryHandler(db, user, messages)
            .Handle(new GetDispatcherDriversQuery(driver.RestaurantId), default);
        var details = await new GetDispatcherDriverDetailsQueryHandler(db, user, messages)
            .Handle(new GetDispatcherDriverDetailsQuery(driver.Id), default);
        list.IsSuccess.Should().BeTrue();
        details.IsSuccess.Should().BeTrue();
        var expected = hasSession ? "Active" : "Inactive";
        home.ShiftStatus.Should().Be(expected);
        home.IsAvailable.Should().Be(hasSession && available);
        foreach (var payload in new object[] { list.Value.Items.Single(), details.Value.Driver })
        {
            var json = JsonSerializer.SerializeToElement(payload, payload.GetType(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            json.TryGetProperty("shiftStatus", out var status).Should().BeTrue("dispatcher responses must expose operational connection status");
            status.GetString().Should().Be(expected);
            json.GetProperty("isAvailable").GetBoolean().Should().Be(home.IsAvailable);
            json.GetProperty("currentStatusText").GetString().Should().Be(home.CurrentStatusText);
        }
        (await db.DriverProfiles.AsNoTracking().SingleAsync()).IsAvailable.Should().Be(available);
    }

    [Fact]
    public async Task RepeatedAvailabilityTrue_WithoutSession_IsRejected()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { RestaurantId = Guid.NewGuid(), FullNameAr = "سائق الاختبار", FullNameEn = "Test driver", IsAvailable = true };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();
        var result = await new UpdateDispatcherDriverAvailabilityCommandHandler(db, Supervisor(driver.RestaurantId!.Value),
            Substitute.For<IAuditLogger>(), NullLocalizedMessageProvider.Instance)
            .Handle(new UpdateDispatcherDriverAvailabilityCommand(driver.Id, true), default);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DRIVER_NOT_OPERATIONAL");
        db.DriverWorkSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task SettingAvailabilityFalse_DoesNotDisconnectAnActiveDriver()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { RestaurantId = Guid.NewGuid(), FullNameAr = "سائق الاختبار", FullNameEn = "Test driver", IsAvailable = true };
        var session = new DriverWorkSession { DriverProfileId = driver.Id, Status = "Active", StartedAtUtc = DateTime.UtcNow };
        db.DriverProfiles.Add(driver);
        db.DriverWorkSessions.Add(session);
        await db.SaveChangesAsync();
        var result = await new UpdateDispatcherDriverAvailabilityCommandHandler(db, Supervisor(driver.RestaurantId!.Value),
            Substitute.For<IAuditLogger>(), NullLocalizedMessageProvider.Instance)
            .Handle(new UpdateDispatcherDriverAvailabilityCommand(driver.Id, false), default);
        result.IsSuccess.Should().BeTrue();
        var home = await DriverHomeScreenBuilder.BuildAsync(db, NullLocalizedMessageProvider.Instance, driver, default);
        home.ShiftStatus.Should().Be("Active");
        home.IsAvailable.Should().BeFalse();
        home.ActiveSession.Should().NotBeNull();
        (await db.DriverWorkSessions.AsNoTracking().SingleAsync()).Status.Should().Be("Active");
    }
}
