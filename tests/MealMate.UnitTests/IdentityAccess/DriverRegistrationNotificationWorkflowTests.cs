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
    private readonly RecordingFcmMessagingClient _fcm;
    private readonly DriverRegistrationNotificationService _service;
    private readonly IDateTimeProvider _clock;

    public DriverRegistrationNotificationWorkflowTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        _clock = new TestDateTimeProvider();
        _fcm = new RecordingFcmMessagingClient { IsConfigured = true };
        _service = new DriverRegistrationNotificationService(
            _db,
            _fcm,
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

    [Fact]
    public async Task NotifyDriverApplicationSubmitted_SendsPushToDriverDevice()
    {
        // Arrange
        var driverUserId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        _db.CustomerDeviceTokens.Add(new CustomerDeviceToken
        {
            UserId = driverUserId,
            Token = "driver-fcm-token-1",
            Platform = MobilePlatform.Android,
            IsActive = true,
            LastSeenAtUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act
        await _service.NotifyDriverApplicationSubmittedAsync(
            registrationId,
            driverUserId,
            "+96551234001",
            "أحمد السائق",
            "برجر هاوس");

        // Assert
        _fcm.SentMessages.Should().HaveCount(1);
        var msg = _fcm.SentMessages[0];
        msg.Token.Should().Be("driver-fcm-token-1");
        msg.Title.Should().Contain("استلام طلب تسجيلك");
        msg.Body.Should().Contain("برجر هاوس");
        msg.Data.Should().NotBeNull();
        msg.Data!["eventKey"].Should().Be("driver.registration.submitted");
        msg.Data["status"].Should().Be("Submitted");
    }

    [Fact]
    public async Task NotifyDriverRestaurantApproved_SendsPushWithUnderReviewStatus()
    {
        // Arrange
        var driverUserId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        _db.CustomerDeviceTokens.Add(new CustomerDeviceToken
        {
            UserId = driverUserId,
            Token = "driver-fcm-token-2",
            Platform = MobilePlatform.Ios,
            IsActive = true,
            LastSeenAtUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act
        await _service.NotifyDriverRestaurantApprovedAsync(
            registrationId,
            driverUserId,
            "+96551234002",
            "خالد السائق",
            "شاورما كينج");

        // Assert
        _fcm.SentMessages.Should().HaveCount(1);
        var msg = _fcm.SentMessages[0];
        msg.Token.Should().Be("driver-fcm-token-2");
        msg.Title.Should().Contain("موافقة المطعم");
        msg.Body.Should().Contain("شاورما كينج");
        msg.Data!["eventKey"].Should().Be("driver.registration.restaurant_approved");
        msg.Data["status"].Should().Be("UnderReview");
        msg.Data["stage"].Should().Be("1");
    }

    [Fact]
    public async Task NotifyDriverRestaurantChangesRequested_SendsPushWithNotes()
    {
        // Arrange
        var driverUserId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        _db.CustomerDeviceTokens.Add(new CustomerDeviceToken
        {
            UserId = driverUserId,
            Token = "driver-fcm-token-3",
            Platform = MobilePlatform.Android,
            IsActive = true,
            LastSeenAtUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act
        await _service.NotifyDriverRestaurantChangesRequestedAsync(
            registrationId,
            driverUserId,
            "+96551234003",
            "سالم السائق",
            "فلافل تايم",
            "يرجى إعادة رفع صورة رخصة القيادة لأنها غير واضحة");

        // Assert
        _fcm.SentMessages.Should().HaveCount(1);
        var msg = _fcm.SentMessages[0];
        msg.Token.Should().Be("driver-fcm-token-3");
        msg.Title.Should().Contain("استكمال نواقص");
        msg.Body.Should().Contain("يرجى إعادة رفع صورة رخصة القيادة");
        msg.Data!["eventKey"].Should().Be("driver.registration.restaurant_changes_requested");
        msg.Data["status"].Should().Be("NeedsChanges");
        msg.Data["notes"].Should().Be("يرجى إعادة رفع صورة رخصة القيادة لأنها غير واضحة");
    }

    [Fact]
    public async Task NotifyDriverRestaurantRejected_SendsPushWithReason()
    {
        // Arrange
        var driverUserId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        _db.CustomerDeviceTokens.Add(new CustomerDeviceToken
        {
            UserId = driverUserId,
            Token = "driver-fcm-token-4",
            Platform = MobilePlatform.Android,
            IsActive = true,
            LastSeenAtUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act
        await _service.NotifyDriverRestaurantRejectedAsync(
            registrationId,
            driverUserId,
            "+96551234004",
            "فهد السائق",
            "مطعم الرياض",
            "اكتفاء المطعم من السائقين حالياً");

        // Assert
        _fcm.SentMessages.Should().HaveCount(1);
        var msg = _fcm.SentMessages[0];
        msg.Token.Should().Be("driver-fcm-token-4");
        msg.Body.Should().Contain("اكتفاء المطعم من السائقين");
        msg.Data!["eventKey"].Should().Be("driver.registration.restaurant_rejected");
        msg.Data["status"].Should().Be("Rejected");
        msg.Data["reason"].Should().Be("اكتفاء المطعم من السائقين حالياً");
    }

    [Fact]
    public async Task NotifyDriverAdminApproved_SendsPushWithApprovedStatus()
    {
        // Arrange
        var driverUserId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        _db.CustomerDeviceTokens.Add(new CustomerDeviceToken
        {
            UserId = driverUserId,
            Token = "driver-fcm-token-5",
            Platform = MobilePlatform.Android,
            IsActive = true,
            LastSeenAtUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act
        await _service.NotifyDriverAdminApprovedAsync(
            registrationId,
            driverUserId,
            "+96551234005",
            "ماجد السائق",
            "برجر هاوس");

        // Assert
        _fcm.SentMessages.Should().HaveCount(1);
        var msg = _fcm.SentMessages[0];
        msg.Token.Should().Be("driver-fcm-token-5");
        msg.Title.Should().Contain("اعتماد حسابك");
        msg.Body.Should().Contain("تفعيل حسابك");
        msg.Data!["eventKey"].Should().Be("driver.registration.admin_approved");
        msg.Data["status"].Should().Be("Approved");
    }

    [Fact]
    public async Task NotifyDriverAdminChangesRequested_SendsPushWithAdminNotes()
    {
        // Arrange
        var driverUserId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        _db.CustomerDeviceTokens.Add(new CustomerDeviceToken
        {
            UserId = driverUserId,
            Token = "driver-fcm-token-6",
            Platform = MobilePlatform.Ios,
            IsActive = true,
            LastSeenAtUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act
        await _service.NotifyDriverAdminChangesRequestedAsync(
            registrationId,
            driverUserId,
            "+96551234006",
            "طارق السائق",
            "صورة البطاقة المدنية منتهية الصلاحية");

        // Assert
        _fcm.SentMessages.Should().HaveCount(1);
        var msg = _fcm.SentMessages[0];
        msg.Token.Should().Be("driver-fcm-token-6");
        msg.Title.Should().Contain("إدارة المنصة");
        msg.Body.Should().Contain("صورة البطاقة المدنية منتهية الصلاحية");
        msg.Data!["eventKey"].Should().Be("driver.registration.admin_changes_requested");
        msg.Data["status"].Should().Be("NeedsChanges");
    }

    [Fact]
    public async Task NotifyDriverAdminRejected_SendsPushWithReason()
    {
        // Arrange
        var driverUserId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        _db.CustomerDeviceTokens.Add(new CustomerDeviceToken
        {
            UserId = driverUserId,
            Token = "driver-fcm-token-7",
            Platform = MobilePlatform.Android,
            IsActive = true,
            LastSeenAtUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act
        await _service.NotifyDriverAdminRejectedAsync(
            registrationId,
            driverUserId,
            "+96551234007",
            "ناصر السائق",
            "عدم استيفاء الشروط القانونية للنقل");

        // Assert
        _fcm.SentMessages.Should().HaveCount(1);
        var msg = _fcm.SentMessages[0];
        msg.Token.Should().Be("driver-fcm-token-7");
        msg.Title.Should().Contain("رفض طلب التسجيل");
        msg.Body.Should().Contain("عدم استيفاء الشروط القانونية");
        msg.Data!["eventKey"].Should().Be("driver.registration.admin_rejected");
        msg.Data["status"].Should().Be("Rejected");
    }

    private sealed class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);
        public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);
    }

    private sealed class RecordingFcmMessagingClient : IFcmMessagingClient
    {
        public bool IsConfigured { get; set; } = true;
        public System.Collections.Generic.List<(string Token, string Title, string Body, System.Collections.Generic.IReadOnlyDictionary<string, string>? Data)> SentMessages { get; } = new();

        public Task<FcmSendResult> SendAsync(
            string token,
            string title,
            string body,
            System.Collections.Generic.IReadOnlyDictionary<string, string>? data = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            SentMessages.Add((token, title, body, data));
            return Task.FromResult(FcmSendResult.Success());
        }
    }
}
