using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class DriverRegistrationNotificationWorkflowTests
{
    private readonly AppDbContext _db;
    private readonly DriverRegistrationNotificationService _service;
    private readonly IDateTimeProvider _clock;

    public DriverRegistrationNotificationWorkflowTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        _clock = new TestDateTimeProvider();
        _service = new DriverRegistrationNotificationService(
            _db,
            new NullFcmMessagingClient(),
            _clock,
            NullLogger<DriverRegistrationNotificationService>.Instance);
    }

    [Fact]
    public async Task NotifyRestaurantDriverApplicationSubmitted_PersistsRestaurantNotificationWithDeepLink()
    {
        // Arrange
        var ownerUserId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();

        var restaurant = new RestaurantProfile
        {
            UserId = ownerUserId,
            LegalNameAr = "مطعم التجربة",
            LegalNameEn = "Test Restaurant",
            TradeNameAr = "مطعم التجربة",
            TradeNameEn = "Test Restaurant",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
        };
        _db.RestaurantProfiles.Add(restaurant);
        await _db.SaveChangesAsync();

        var restaurantId = restaurant.Id;

        // Act
        await _service.NotifyRestaurantDriverApplicationSubmittedAsync(
            restaurantId,
            registrationId,
            "أحمد السائق",
            "+96551234001");

        // Assert
        var notification = await _db.RestaurantNotifications
            .FirstOrDefaultAsync(n => n.SubjectId == registrationId);

        notification.Should().NotBeNull();
        notification!.UserId.Should().Be(ownerUserId);
        notification.Type.Should().Be("driver.registration.submitted");
        notification.TitleAr.Should().Contain("سائق");
        notification.BodyAr.Should().Contain("أحمد السائق");
        notification.BodyAr.Should().Contain("+96551234001");
        notification.DeepLink.Should().Be($"/restaurant/drivers/registrations/{registrationId}");
        notification.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task NotifyAdminsDriverApplicationReadyForConfirmation_PersistsAdminNotificationWithPayload()
    {
        // Arrange
        var registrationId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        // Act
        await _service.NotifyAdminsDriverApplicationReadyForConfirmationAsync(
            registrationId,
            restaurantId,
            "برجر هاوس",
            "خالد الشمري",
            "+96561234002");

        // Assert
        var adminNotif = await _db.AdminNotifications
            .FirstOrDefaultAsync(n => n.PayloadJson != null && n.PayloadJson.Contains(registrationId.ToString()));

        adminNotif.Should().NotBeNull();
        adminNotif!.UserId.Should().BeNull(); // broadcast to all admins
        adminNotif.TitleAr.Should().Contain("سائق");
        adminNotif.BodyAr.Should().Contain("برجر هاوس");
        adminNotif.BodyAr.Should().Contain("خالد الشمري");
        adminNotif.Channel.Should().Be(NotificationChannel.InApp);
        adminNotif.PayloadJson.Should().Contain("driver.registration.restaurant_approved");
        adminNotif.PayloadJson.Should().Contain($"/admin/drivers/registrations/{registrationId}");
    }

    [Fact]
    public async Task NotifyRestaurantDriverApplicationResubmitted_PersistsResubmissionNotification()
    {
        // Arrange
        var ownerUserId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();

        var restaurant = new RestaurantProfile
        {
            UserId = ownerUserId,
            LegalNameAr = "بيتزا تايم",
            LegalNameEn = "Pizza Time",
            TradeNameAr = "بيتزا تايم",
            TradeNameEn = "Pizza Time",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
        };
        _db.RestaurantProfiles.Add(restaurant);
        await _db.SaveChangesAsync();

        var restaurantId = restaurant.Id;

        // Act
        await _service.NotifyRestaurantDriverApplicationResubmittedAsync(
            restaurantId,
            registrationId,
            "سالم العلي",
            "+96599887766");

        // Assert
        var notification = await _db.RestaurantNotifications
            .FirstOrDefaultAsync(n => n.SubjectId == registrationId && n.Type == "driver.registration.resubmitted");

        notification.Should().NotBeNull();
        notification!.UserId.Should().Be(ownerUserId);
        notification.TitleAr.Should().Contain("إعادة تقديم");
        notification.BodyAr.Should().Contain("سالم العلي");
        notification.DeepLink.Should().Be($"/restaurant/drivers/registrations/{registrationId}");
    }

    private sealed class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);
        public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);
    }

    private sealed class NullFcmMessagingClient : IFcmMessagingClient
    {
        public bool IsConfigured => false;

        public Task<FcmSendResult> SendAsync(
            string token,
            string title,
            string body,
            IReadOnlyDictionary<string, string>? data = null,
            System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult(FcmSendResult.Success());
    }
}
