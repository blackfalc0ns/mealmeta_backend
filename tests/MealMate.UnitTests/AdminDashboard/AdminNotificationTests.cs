using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminNotifications;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MealMate.UnitTests.AdminDashboard;

public sealed class AdminNotificationTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task GetAdminNotifications_Returns_Saved_Notifications_And_Unread_Count()
    {
        await using var db = CreateInMemoryDbContext();

        db.AdminNotifications.AddRange(
            new AdminNotification
            {
                TitleAr = "حساب مطعم جديد 👤",
                TitleEn = "New Restaurant Account",
                BodyAr = "قام فلان بإنشاء حساب",
                BodyEn = "Someone created an account",
                IsRead = false,
                SentAtUtc = DateTime.UtcNow.AddMinutes(-5),
                PayloadJson = "{\"deepLink\":\"/admin/accounts/pending\",\"eventKey\":\"restaurant.account_created\"}",
            },
            new AdminNotification
            {
                TitleAr = "طلب تسجيل مطعم جديد 🍽️",
                TitleEn = "New Restaurant Registration Submitted",
                BodyAr = "تم تقديم طلب تسجيل جديد",
                BodyEn = "Registration submitted",
                IsRead = true,
                SentAtUtc = DateTime.UtcNow.AddMinutes(-10),
                PayloadJson = "{\"deepLink\":\"/admin/accounts/pending/123\",\"eventKey\":\"restaurant.registration_submitted\"}",
            });
        await db.SaveChangesAsync();

        var handler = new GetAdminNotificationsQueryHandler(db);
        var result = await handler.Handle(new GetAdminNotificationsQuery(10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var response = result.Value;
        response.Items.Should().HaveCount(2);
        response.UnreadCount.Should().Be(1);
        response.Items[0].EventKey.Should().Be("restaurant.account_created");
        response.Items[0].DeepLink.Should().Be("/admin/accounts/pending");
    }

    [Fact]
    public async Task MarkAdminNotificationRead_Marks_Single_Notification_As_Read()
    {
        await using var db = CreateInMemoryDbContext();
        var notifId = Guid.NewGuid();

        var notif = new AdminNotification
        {
            TitleAr = "حساب مطعم جديد 👤",
            TitleEn = "New Restaurant Account",
            IsRead = false,
            SentAtUtc = DateTime.UtcNow,
        };
        db.AdminNotifications.Add(notif);
        await db.SaveChangesAsync();

        var handler = new MarkAdminNotificationReadCommandHandler(db);
        var result = await handler.Handle(new MarkAdminNotificationReadCommand(notif.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = await db.AdminNotifications.FindAsync(notif.Id);
        item!.IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task MarkAllAdminNotificationsRead_Marks_All_Unread_As_Read()
    {
        await using var db = CreateInMemoryDbContext();

        db.AdminNotifications.AddRange(
            new AdminNotification { TitleAr = "N1", TitleEn = "N1", IsRead = false, SentAtUtc = DateTime.UtcNow },
            new AdminNotification { TitleAr = "N2", TitleEn = "N2", IsRead = false, SentAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var handler = new MarkAllAdminNotificationsReadCommandHandler(db);
        var result = await handler.Handle(new MarkAllAdminNotificationsReadCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var unread = await db.AdminNotifications.CountAsync(x => !x.IsRead);
        unread.Should().Be(0);
    }

    [Fact]
    public async Task RegistrationPushNotificationService_Persists_InApp_Notification_On_Account_Created()
    {
        await using var db = CreateInMemoryDbContext();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(false); // Even when FCM push is not configured, DB row must be stored!

        var service = new RegistrationPushNotificationService(db, fcm, NullLogger<RegistrationPushNotificationService>.Instance);
        var userId = Guid.NewGuid();

        await service.NotifyAdminsNewRestaurantAccountCreatedAsync(userId, "مطعم البركة", "owner@baraka.kw", "96599112233");

        var stored = await db.AdminNotifications.ToListAsync();
        stored.Should().HaveCount(1);
        stored[0].TitleAr.Should().Contain("حساب مطعم جديد");
        stored[0].BodyAr.Should().Contain("مطعم البركة");
        stored[0].PayloadJson.Should().Contain("restaurant.account_created");
    }

    [Fact]
    public async Task RegistrationPushNotificationService_Persists_RestaurantNotification_On_Approved()
    {
        await using var db = CreateInMemoryDbContext();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(false); // Even when FCM push is unconfigured, restaurant DB notification must be saved!

        var service = new RegistrationPushNotificationService(db, fcm, NullLogger<RegistrationPushNotificationService>.Instance);
        var ownerUserId = Guid.NewGuid();

        await service.NotifyRestaurantRegistrationApprovedAsync(ownerUserId);

        var stored = await db.RestaurantNotifications.ToListAsync();
        stored.Should().HaveCount(1);
        stored[0].UserId.Should().Be(ownerUserId);
        stored[0].Type.Should().Be("registration.approved");
        stored[0].TitleAr.Should().Contain("تهانينا");
        stored[0].DeepLink.Should().Be("/restaurant/orders/pending-confirmation");
        stored[0].IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task RegistrationPushNotificationService_Persists_RestaurantNotification_On_Rejected()
    {
        await using var db = CreateInMemoryDbContext();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(false);

        var service = new RegistrationPushNotificationService(db, fcm, NullLogger<RegistrationPushNotificationService>.Instance);
        var ownerUserId = Guid.NewGuid();

        await service.NotifyRestaurantRegistrationRejectedAsync(ownerUserId, "المستندات غير واضحة");

        var stored = await db.RestaurantNotifications.ToListAsync();
        stored.Should().HaveCount(1);
        stored[0].UserId.Should().Be(ownerUserId);
        stored[0].Type.Should().Be("registration.rejected");
        stored[0].TitleAr.Should().Contain("تم رفض");
        stored[0].BodyAr.Should().Contain("المستندات غير واضحة");
        stored[0].DeepLink.Should().Be("/restaurant/under-review");
        stored[0].IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task RegistrationPushNotificationService_Persists_RestaurantNotification_On_Field_Rejected_With_DeepLink()
    {
        await using var db = CreateInMemoryDbContext();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(false);

        var service = new RegistrationPushNotificationService(db, fcm, NullLogger<RegistrationPushNotificationService>.Instance);
        var ownerUserId = Guid.NewGuid();

        await service.NotifyRestaurantFieldRejectedAsync(ownerUserId, "commercialRegistrationNumber", "رقم السجل غير صحيح");

        var stored = await db.RestaurantNotifications.ToListAsync();
        stored.Should().HaveCount(1);
        stored[0].UserId.Should().Be(ownerUserId);
        stored[0].Type.Should().Be("field.rejected");
        stored[0].TitleAr.Should().Contain("ملاحظات");
        stored[0].BodyAr.Should().Contain("رقم السجل غير صحيح");
        stored[0].DeepLink.Should().Be("/restaurant/onboarding?step=business&field=commercialRegistrationNumber&correction=true");
        stored[0].IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task MarkAllRestaurantReviewNotificationsRead_Marks_All_As_Read_For_User()
    {
        await using var db = CreateInMemoryDbContext();
        var ownerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        db.RestaurantNotifications.AddRange(
            new MealMate.Domain.IdentityAccess.RestaurantNotification
            {
                UserId = ownerUserId,
                Type = "registration.approved",
                TitleAr = "T1",
                TitleEn = "T1",
                BodyAr = "B1",
                BodyEn = "B1",
                IsRead = false,
                SentAtUtc = DateTime.UtcNow,
            },
            new MealMate.Domain.IdentityAccess.RestaurantNotification
            {
                UserId = ownerUserId,
                Type = "field.rejected",
                TitleAr = "T2",
                TitleEn = "T2",
                BodyAr = "B2",
                BodyEn = "B2",
                IsRead = false,
                SentAtUtc = DateTime.UtcNow,
            },
            new MealMate.Domain.IdentityAccess.RestaurantNotification
            {
                UserId = otherUserId,
                Type = "registration.approved",
                TitleAr = "T3",
                TitleEn = "T3",
                BodyAr = "B3",
                BodyEn = "B3",
                IsRead = false,
                SentAtUtc = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(ownerUserId);

        var handler = new MealMate.Application.Features.IdentityAccess.RestaurantRegistration.RestaurantReviewCenterHandlers(db, currentUser);
        var result = await handler.Handle(new MealMate.Application.Features.IdentityAccess.RestaurantRegistration.MarkAllRestaurantReviewNotificationsReadCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var ownerUnread = await db.RestaurantNotifications.CountAsync(x => x.UserId == ownerUserId && !x.IsRead);
        ownerUnread.Should().Be(0);

        var otherUnread = await db.RestaurantNotifications.CountAsync(x => x.UserId == otherUserId && !x.IsRead);
        otherUnread.Should().Be(1);
    }
}

