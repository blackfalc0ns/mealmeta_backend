using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DeliveryManagerApp;

public sealed record RegisterDeliveryManagerDeviceTokenCommand(
    Guid? RestaurantId,
    string Token,
    string Platform = "Android",
    string? DeviceId = null) : ICommand;

public sealed record UnregisterDeliveryManagerDeviceTokenCommand(
    string Token) : ICommand;

public sealed record GetDeliveryManagerNotificationsQuery : IQuery<IReadOnlyList<DeliveryManagerNotificationItemDto>>;

public sealed record MarkDeliveryManagerNotificationReadCommand(
    Guid NotificationId) : ICommand;

public sealed class DeliveryManagerNotificationCommandHandlers
    : ICommandHandler<RegisterDeliveryManagerDeviceTokenCommand>,
      ICommandHandler<UnregisterDeliveryManagerDeviceTokenCommand>,
      IQueryHandler<GetDeliveryManagerNotificationsQuery, IReadOnlyList<DeliveryManagerNotificationItemDto>>,
      ICommandHandler<MarkDeliveryManagerNotificationReadCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public DeliveryManagerNotificationCommandHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result> Handle(RegisterDeliveryManagerDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Token is required.");
        }

        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication required to register device token.");
        }

        if (_currentUser.UserType != UserType.DeliveryManager && !_currentUser.Roles.Any(AppRoles.IsAdminRole))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Only delivery managers can register a device token here.");
        }

        var userId = _currentUser.UserId.Value;
        var restaurantId = _currentUser.RestaurantId ?? request.RestaurantId;
        var now = DateTime.UtcNow;

        var platform = request.Platform.Equals("iOS", StringComparison.OrdinalIgnoreCase)
            ? MobilePlatform.Ios
            : MobilePlatform.Android;

        var existing = await _db.DeliveryManagerDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);

        if (existing is not null)
        {
            existing.UserId = userId;
            existing.RestaurantId = restaurantId ?? existing.RestaurantId;
            existing.Platform = platform;
            existing.DeviceId = request.DeviceId ?? existing.DeviceId;
            existing.IsActive = true;
            existing.LastSeenAtUtc = now;
        }
        else
        {
            _db.DeliveryManagerDeviceTokens.Add(new DeliveryManagerDeviceToken
            {
                UserId = userId,
                RestaurantId = restaurantId,
                Token = request.Token,
                Platform = platform,
                DeviceId = request.DeviceId,
                IsActive = true,
                LastSeenAtUtc = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(UnregisterDeliveryManagerDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Result.Success();

        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication required.");
        }

        var token = request.Token.Trim();
        var query = _db.DeliveryManagerDeviceTokens.Where(t => t.Token == token);
        if (!_currentUser.Roles.Any(AppRoles.IsAdminRole))
        {
            query = query.Where(t => t.UserId == _currentUser.UserId.Value);
        }

        var existing = await query.ToListAsync(cancellationToken);

        foreach (var row in existing)
        {
            row.IsActive = false;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<DeliveryManagerNotificationItemDto>>> Handle(
        GetDeliveryManagerNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication required to view notifications.");
        }

        var isAr = _messages.IsArabic();
        var now = DateTime.UtcNow;

        var notifications = await _db.DeliveryManagerNotifications
            .AsNoTracking()
            .Where(n => n.UserId == _currentUser.UserId.Value)
            .OrderByDescending(n => n.SentAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        var dtos = notifications.Select(n =>
        {
            var diff = now - n.SentAtUtc;
            var timeAgo = diff.TotalMinutes < 60
                ? (isAr ? $"منذ {(int)diff.TotalMinutes} دقيقة" : $"{(int)diff.TotalMinutes}m ago")
                : (isAr ? $"منذ {(int)diff.TotalHours} ساعة" : $"{(int)diff.TotalHours}h ago");

            return new DeliveryManagerNotificationItemDto(
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

        return Result.Success<IReadOnlyList<DeliveryManagerNotificationItemDto>>(dtos);
    }

    public async Task<Result> Handle(MarkDeliveryManagerNotificationReadCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication required.");
        }

        var notification = await _db.DeliveryManagerNotifications
            .FirstOrDefaultAsync(n => n.Id == request.NotificationId, cancellationToken);

        if (notification is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "Notification not found.");
        }

        if (notification.UserId != _currentUser.UserId.Value
            && !_currentUser.Roles.Any(AppRoles.IsAdminRole))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Cannot modify another user's notification.");
        }

        notification.IsRead = true;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed record DeliveryManagerNotificationItemDto(
    Guid NotificationId,
    string Type,
    string Title,
    string Body,
    string SentTimeAgo,
    bool IsRead,
    string? Section,
    Guid? SubjectId,
    string? DeepLink,
    DateTime SentAtUtc);
