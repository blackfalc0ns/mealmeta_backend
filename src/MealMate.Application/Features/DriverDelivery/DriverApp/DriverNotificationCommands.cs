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
    string? DeviceId = null,
    Guid? RegistrationId = null) : ICommand;

public sealed record UnregisterDriverDeviceTokenCommand(
    string Token) : ICommand;

public sealed record GetDriverNotificationsQuery(
    Guid? DriverId = null) : IQuery<IReadOnlyList<DriverNotificationItemDto>>;

public sealed record MarkDriverNotificationReadCommand(
    Guid NotificationId) : ICommand;

public sealed class DriverNotificationCommandHandlers
    : ICommandHandler<RegisterDriverDeviceTokenCommand>,
      ICommandHandler<UnregisterDriverDeviceTokenCommand>,
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

        Guid? resolvedUserId = _currentUser.UserId;
        DriverProfile? driver = null;

        // 1. If not authenticated, check RegistrationId (new registration flow before login)
        if (!resolvedUserId.HasValue && request.RegistrationId.HasValue && request.RegistrationId.Value != Guid.Empty)
        {
            var registration = await _db.DriverRegistrations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.RegistrationId.Value, cancellationToken);

            if (registration is not null && registration.SubmittedByUserId.HasValue)
            {
                resolvedUserId = registration.SubmittedByUserId.Value;
            }
        }

        // 2. If DriverId is provided
        if (!resolvedUserId.HasValue && request.DriverId.HasValue && request.DriverId.Value != Guid.Empty)
        {
            driver = await _db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == request.DriverId.Value, cancellationToken);
            if (driver is not null)
            {
                resolvedUserId = driver.UserId;
            }
        }

        // 3. Fallback resolution
        if (!resolvedUserId.HasValue)
        {
            driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
            if (driver is not null)
            {
                resolvedUserId = driver.UserId;
            }
        }

        if (!resolvedUserId.HasValue)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق لتسجيل الجهاز.");
        }

        var userId = resolvedUserId.Value;
        var now = DateTime.UtcNow;

        var platform = request.Platform.Equals("iOS", StringComparison.OrdinalIgnoreCase)
            ? MobilePlatform.Ios
            : MobilePlatform.Android;

        var existing = await _db.DriverDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);

        if (existing is not null)
        {
            existing.UserId = userId;
            existing.DriverProfileId = driver?.Id ?? existing.DriverProfileId;
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

    public async Task<Result> Handle(UnregisterDriverDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Result.Success();

        var token = request.Token.Trim();
        var existing = await _db.DriverDeviceTokens
            .Where(t => t.Token == token)
            .ToListAsync(cancellationToken);

        foreach (var row in existing)
        {
            row.IsActive = false;
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

    private Task<DriverProfile?> ResolveDriverAsync(Guid? driverId, CancellationToken cancellationToken)
        => DriverResolutionHelper.ResolveDriverAsync(_db, _currentUser, driverId, cancellationToken);
}
