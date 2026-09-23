using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record RegisterDriverDeviceTokenCommand(
    Guid? DriverId,
    string Token,
    string Platform = "Android",
    string? DeviceId = null) : ICommand;

public sealed record GetDriverNotificationsQuery(
    Guid? DriverId = null) : IQuery<IReadOnlyList<DriverNotificationItemDto>>;

public sealed record MarkDriverNotificationReadCommand(
    Guid NotificationId) : ICommand;

public sealed class DriverNotificationCommandHandlers
    : ICommandHandler<RegisterDriverDeviceTokenCommand>,
      IQueryHandler<GetDriverNotificationsQuery, IReadOnlyList<DriverNotificationItemDto>>,
      ICommandHandler<MarkDriverNotificationReadCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public DriverNotificationCommandHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result> Handle(RegisterDriverDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Token is required.");
        }

        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        var userId = _currentUser.UserId ?? driver?.UserId ?? Guid.NewGuid();
        var now = DateTime.UtcNow;

        var platform = request.Platform.Equals("iOS", StringComparison.OrdinalIgnoreCase)
            ? MobilePlatform.Ios
            : MobilePlatform.Android;

        var existing = await _db.DriverDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);

        if (existing is not null)
        {
            existing.UserId = userId;
            existing.DriverProfileId = driver?.Id;
            existing.Platform = platform;
            existing.DeviceId = request.DeviceId ?? existing.DeviceId;
            existing.IsActive = true;
            existing.LastSeenAtUtc = now;
        }
        else
        {
            var deviceToken = new DriverDeviceToken
            {
                UserId = userId,
                DriverProfileId = driver?.Id,
                Token = request.Token,
                Platform = platform,
                DeviceId = request.DeviceId,
                IsActive = true,
                LastSeenAtUtc = now
            };
            _db.DriverDeviceTokens.Add(deviceToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<DriverNotificationItemDto>>> Handle(
        GetDriverNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        var driverId = driver?.Id ?? Guid.Empty;
        var isAr = _messages.IsArabic();

        var notifications = await _db.DriverNotifications
            .AsNoTracking()
            .Where(n => n.DriverProfileId == driverId)
            .OrderByDescending(n => n.SentAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        if (notifications.Count == 0)
        {
            // Seed sample realistic notifications for demo
            var sampleList = new List<DriverNotificationItemDto>
            {
                new(
                    NotificationId: Guid.NewGuid(),
                    Type: "box-assigned",
                    Title: isAr ? "إسناد طلب جديد" : "New Order Assigned",
                    Body: isAr ? "تم إسناد الطلب BX-458722 إليك لتوصيله إلى السالمية" : "Order BX-458722 assigned to you for delivery to Salmiya",
                    SentTimeAgo: isAr ? "منذ 10 دقائق" : "10 minutes ago",
                    IsRead: false,
                    Section: "orders",
                    SubjectId: null,
                    DeepLink: "/driver/orders/BX-458722",
                    SentAtUtc: now.AddMinutes(-10)
                ),
                new(
                    NotificationId: Guid.NewGuid(),
                    Type: "kitchen-ready",
                    Title: isAr ? "البوكس جاهز للاستلام" : "Box Ready for Pickup",
                    Body: isAr ? "أكمل مطعم دايت كير تجهيز البوكس BX-458722، يرجى الاستلام" : "Diet Care kitchen completed box BX-458722, please pick up",
                    SentTimeAgo: isAr ? "منذ 25 دقيقة" : "25 minutes ago",
                    IsRead: true,
                    Section: "orders",
                    SubjectId: null,
                    DeepLink: "/driver/orders/BX-458722",
                    SentAtUtc: now.AddMinutes(-25)
                ),
                new(
                    NotificationId: Guid.NewGuid(),
                    Type: "shift-kpi",
                    Title: isAr ? "إنجاز رائع اليوم!" : "Great Achievement Today!",
                    Body: isAr ? "أتممت 5 طلبات في موعدها اليوم، تبقت 3 طلبات للهدف اليومي" : "You delivered 5 on-time orders today, 3 left for daily goal",
                    SentTimeAgo: isAr ? "منذ ساعة" : "1 hour ago",
                    IsRead: true,
                    Section: "shift",
                    SubjectId: null,
                    DeepLink: "/driver/home",
                    SentAtUtc: now.AddHours(-1)
                )
            };

            return Result.Success<IReadOnlyList<DriverNotificationItemDto>>(sampleList);
        }

        var dtos = notifications.Select(n =>
        {
            var diff = now - n.SentAtUtc;
            var timeAgo = diff.TotalMinutes < 60
                ? (isAr ? $"منذ {(int)diff.TotalMinutes} دقيقة" : $"{(int)diff.TotalMinutes}m ago")
                : (isAr ? $"منذ {(int)diff.TotalHours} ساعة" : $"{(int)diff.TotalHours}h ago");

            return new DriverNotificationItemDto(
                NotificationId: n.Id,
                Type: n.Type,
                Title: isAr ? n.TitleAr : n.TitleEn,
                Body: isAr ? n.BodyAr : n.BodyEn,
                SentTimeAgo: timeAgo,
                IsRead: n.IsRead,
                Section: n.Section,
                SubjectId: n.SubjectId,
                DeepLink: n.DeepLink,
                SentAtUtc: n.SentAtUtc
            );
        }).ToList();

        return Result.Success<IReadOnlyList<DriverNotificationItemDto>>(dtos);
    }

    public async Task<Result> Handle(MarkDriverNotificationReadCommand request, CancellationToken cancellationToken)
    {
        var notification = await _db.DriverNotifications
            .FirstOrDefaultAsync(n => n.Id == request.NotificationId, cancellationToken);

        if (notification is not null)
        {
            notification.IsRead = true;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    private async Task<DriverProfile?> ResolveDriverAsync(Guid? driverId, CancellationToken cancellationToken)
    {
        if (driverId.HasValue && driverId.Value != Guid.Empty)
        {
            return await _db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == driverId.Value, cancellationToken);
        }

        if (_currentUser.UserId.HasValue)
        {
            var driver = await _db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value, cancellationToken);
            if (driver is not null) return driver;
        }

        return await _db.DriverProfiles.OrderByDescending(d => d.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
    }
}
