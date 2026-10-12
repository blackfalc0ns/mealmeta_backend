using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record RegisterDriverDeviceTokenCommand(
    Guid? DriverId,
    string Token,
    string Platform = "Android",
    string? DeviceId = null,
    Guid? RegistrationId = null) : ICommand<DeviceTokenRegistrationResult>;

public sealed record UnregisterDriverDeviceTokenCommand(
    string Token) : ICommand;

public sealed record GetDriverNotificationsQuery(
    Guid? DriverId = null) : IQuery<IReadOnlyList<DriverNotificationItemDto>>;

public sealed record MarkDriverNotificationReadCommand(
    Guid NotificationId) : ICommand;

public sealed class DriverNotificationCommandHandlers
    : ICommandHandler<RegisterDriverDeviceTokenCommand, DeviceTokenRegistrationResult>,
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

    public async Task<Result<DeviceTokenRegistrationResult>> Handle(RegisterDriverDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Token is required.");
        }

        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication required to register device token.");
        }

        var userId = _currentUser.UserId.Value;

        // If registration ID was supplied, verify it belongs to current user
        if (request.RegistrationId.HasValue && request.RegistrationId.Value != Guid.Empty)
        {
            var reg = await _db.DriverRegistrations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.RegistrationId.Value, cancellationToken);
            if (reg is not null && reg.SubmittedByUserId.HasValue && reg.SubmittedByUserId.Value != userId
                && !_currentUser.Roles.Any(AppRoles.IsAdminRole))
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "Cannot register device token for another user's registration.");
            }
        }

        // If driver ID was supplied, verify it belongs to current user
        DriverProfile? driver = null;
        if (request.DriverId.HasValue && request.DriverId.Value != Guid.Empty)
        {
            driver = await _db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == request.DriverId.Value, cancellationToken);
            if (driver is not null && driver.UserId != userId && !_currentUser.Roles.Any(AppRoles.IsAdminRole))
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "Cannot register device token for another user's driver profile.");
            }
        }
        else
        {
            driver = await ResolveDriverAsync(null, cancellationToken);
        }

        var now = DateTime.UtcNow;

        var platform = request.Platform.Equals("iOS", StringComparison.OrdinalIgnoreCase)
            ? MobilePlatform.Ios
            : MobilePlatform.Android;

        // The voice-route rebind is contractual: only an explicit, non-blank
        // installation id in THIS request may move a live session's binding — a
        // stale value remembered on the registry row is never enough.
        var requestedInstallation = string.IsNullOrWhiteSpace(request.DeviceId) ? null : request.DeviceId.Trim();

        // Serialize the whole read-modify-write per (owner, installation); the token
        // rows' xmin concurrency token is the cross-instance backstop behind the gate.
        var gate = requestedInstallation is null
            ? null
            : MealMate.Application.Features.VoiceCalls.Services.VoiceCallPushRouteRebinder.RegistrationGate(userId, requestedInstallation);
        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken);
        }

        try
        {
            const int maxConcurrencyAttempts = 3;
            for (var attempt = 0; ; attempt++)
            {
                var existing = await _db.DriverDeviceTokens
                    .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);

                List<MealMate.Domain.VoiceCalls.VoiceCallDeviceSession> touchedSessions;
                List<DriverDeviceToken> touchedRows;

                if (existing is not null)
                {
                    // A token claimed by a new account must stop serving the previous
                    // owner's voice sessions before ownership moves.
                    var detached = await MealMate.Application.Features.VoiceCalls.Services.VoiceCallPushRouteRebinder
                        .DetachRoutesClaimedByAnotherAccountAsync(_db, existing.Id, userId, now, cancellationToken);
                    existing.UserId = userId;
                    existing.DriverProfileId = driver?.Id ?? existing.DriverProfileId;
                    existing.Platform = platform;
                    existing.DeviceId = request.DeviceId ?? existing.DeviceId;
                    existing.IsActive = true;
                    existing.LastSeenAtUtc = now;
                    var rebound = await MealMate.Application.Features.VoiceCalls.Services.VoiceCallPushRouteRebinder
                        .RebindLiveSessionsAsync(_db, userId, requestedInstallation, platform, existing.Id, now, cancellationToken);
                    touchedSessions = detached.Concat(rebound).ToList();
                    touchedRows = [existing];
                }
                else
                {
                    // Same-atomic-save supersession as the customer flow: the previous
                    // routes of this installation are deactivated in the same commit
                    // that rebinds its live voice sessions to the new registry row.
                    List<DriverDeviceToken> superseded = [];
                    if (!string.IsNullOrWhiteSpace(requestedInstallation))
                    {
                        superseded = await _db.DriverDeviceTokens
                            .Where(x => x.UserId == userId && x.DeviceId == requestedInstallation)
                            .ToListAsync(cancellationToken);

                        foreach (var row in superseded)
                        {
                            row.IsActive = false;
                            row.ModifiedAtUtc = now;
                        }
                    }

                    existing = new DriverDeviceToken
                    {
                        UserId = userId,
                        DriverProfileId = driver?.Id,
                        Token = request.Token,
                        Platform = platform,
                        DeviceId = request.DeviceId,
                        IsActive = true,
                        LastSeenAtUtc = now
                    };
                    _db.DriverDeviceTokens.Add(existing);
                    touchedSessions = await MealMate.Application.Features.VoiceCalls.Services.VoiceCallPushRouteRebinder
                        .RebindLiveSessionsAsync(_db, userId, requestedInstallation, platform, existing.Id, now, cancellationToken);
                    touchedRows = [existing, .. superseded];
                }

                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return new DeviceTokenRegistrationResult(existing.Id);
                }
                catch (DbUpdateConcurrencyException) when (attempt < maxConcurrencyAttempts - 1)
                {
                    // A concurrent registration for this installation moved the same
                    // rows between our read and commit: discard and reread.
                    foreach (var row in touchedRows)
                    {
                        _db.DetachTrackedEntity(row);
                    }

                    foreach (var session in touchedSessions)
                    {
                        _db.DetachTrackedEntity(session);
                    }
                }
            }
        }
        finally
        {
            gate?.Release();
        }
    }

    public async Task<Result> Handle(UnregisterDriverDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Result.Success();

        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication required.");
        }

        var token = request.Token.Trim();
        var query = _db.DriverDeviceTokens.Where(t => t.Token == token);
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

    public async Task<Result<IReadOnlyList<DriverNotificationItemDto>>> Handle(
        GetDriverNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication required to view notifications.");
        }

        var callerUserId = _currentUser.UserId.Value;
        DriverProfile? driver = null;

        if (request.DriverId.HasValue && request.DriverId.Value != Guid.Empty)
        {
            driver = await _db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == request.DriverId.Value, cancellationToken);
            if (driver is not null && driver.UserId != callerUserId
                && !_currentUser.Roles.Any(AppRoles.IsAdminRole) && !_currentUser.HasPermission(PermissionCodes.DriverRegView))
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "Cannot view notifications for another driver.");
            }
        }
        else
        {
            driver = await ResolveDriverAsync(null, cancellationToken);
        }

        var driverId = driver?.Id;
        var isAr = _messages.IsArabic();

        // Support notifications linked by UserId (pre-approval) or DriverProfileId (post-approval)
        var query = _db.DriverNotifications
            .AsNoTracking()
            .Where(n => n.UserId == callerUserId || (driverId.HasValue && n.DriverProfileId == driverId.Value));

        var notifications = await query
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
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication required.");
        }

        var notification = await _db.DriverNotifications
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

    private Task<DriverProfile?> ResolveDriverAsync(Guid? driverId, CancellationToken cancellationToken)
        => DriverResolutionHelper.ResolveDriverAsync(_db, _currentUser, driverId, cancellationToken);
}
