using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

/// <summary>
/// Unified endpoint for registering a push notification device token for any staff member
/// (Driver or Delivery Manager) from the shared mobile app.
///
/// The command determines the correct storage table automatically based on the authenticated
/// user's role (JWT claims). This solves the problem of both roles using the same Firebase
/// project and the same app, so the mobile app doesn't need to know which backend table to use.
///
/// Routing logic:
///   - Role = "Driver"          → DriverDeviceTokens
///   - Role = "DeliveryManager" → RestaurantDeviceTokens
///   - Unknown / unauthenticated → returns 400 Bad Request
/// </summary>
public sealed record RegisterStaffDeviceTokenCommand(
    string Token,
    string Platform = "Android",
    string? DeviceId = null,
    /// <summary>
    /// Optional: explicit role override. Falls back to the authenticated JWT role.
    /// Useful only for pre-login flows where no JWT is present.
    /// Accepted values: "Driver", "DeliveryManager".
    /// </summary>
    string? Role = null) : ICommand;

public sealed class RegisterStaffDeviceTokenCommandHandler
    : ICommandHandler<RegisterStaffDeviceTokenCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILocalizedMessageProvider _messages;
    private readonly ILogger<RegisterStaffDeviceTokenCommandHandler> _logger;

    public RegisterStaffDeviceTokenCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILocalizedMessageProvider messages,
        ILogger<RegisterStaffDeviceTokenCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _messages = messages;
        _logger = logger;
    }

    public async Task<Result> Handle(
        RegisterStaffDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Error.Validation(MessageCodes.Validation.Required, "FCM token is required.");

        // Resolve role: prefer JWT claims, fall back to explicit request body role.
        var resolvedRole = ResolveRole(request.Role);
        if (resolvedRole is null)
        {
            return Error.Validation(
                MessageCodes.Validation.Required,
                "لم يتم التعرف على دور المستخدم. يرجى تسجيل الدخول أو تحديد الدور (Driver / DeliveryManager) في جسم الطلب.");
        }

        if (resolvedRole == AppRoles.Driver)
        {
            return await RegisterDriverTokenAsync(request, cancellationToken);
        }

        if (resolvedRole == AppRoles.DeliveryManager)
        {
            return await RegisterDeliveryManagerTokenAsync(request, cancellationToken);
        }

        return Error.Validation(
            MessageCodes.Validation.InvalidFormat,
            $"الدور '{resolvedRole}' غير مدعوم لتسجيل توكن الجهاز. الأدوار المدعومة: Driver, DeliveryManager.");
    }

    // ─── Driver path ────────────────────────────────────────────────────────────

    private async Task<Result> RegisterDriverTokenAsync(
        RegisterStaffDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue)
        {
            _logger.LogWarning("StaffDeviceToken: Driver token registration attempted without a valid UserId.");
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "يرجى تسجيل الدخول كسائق لتسجيل الجهاز.");
        }

        var userId = _currentUser.UserId.Value;
        var now = _clock.UtcNow;

        var platform = request.Platform.Equals("iOS", StringComparison.OrdinalIgnoreCase)
            ? MobilePlatform.Ios
            : MobilePlatform.Android;

        var existing = await _db.DriverDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);

        if (existing is not null)
        {
            existing.UserId = userId;
            existing.Platform = platform;
            existing.DeviceId = request.DeviceId ?? existing.DeviceId;
            existing.IsActive = true;
            existing.LastSeenAtUtc = now;
        }
        else
        {
            var driverProfile = await _db.DriverProfiles
                .AsNoTracking()
                .Where(d => d.UserId == userId)
                .Select(d => d.Id)
                .Cast<Guid?>()
                .FirstOrDefaultAsync(cancellationToken);

            _db.DriverDeviceTokens.Add(new DriverDeviceToken
            {
                UserId = userId,
                DriverProfileId = driverProfile,
                Token = request.Token,
                Platform = platform,
                DeviceId = request.DeviceId,
                IsActive = true,
                LastSeenAtUtc = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "StaffDeviceToken: Registered driver token for UserId={UserId}", userId);

        return Result.Success();
    }

    // ─── Delivery Manager path ───────────────────────────────────────────────────

    private async Task<Result> RegisterDeliveryManagerTokenAsync(
        RegisterStaffDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue)
        {
            _logger.LogWarning("StaffDeviceToken: Delivery Manager token registration attempted without a valid UserId.");
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "يرجى تسجيل الدخول كمسؤول توصيل لتسجيل الجهاز.");
        }

        var userId = _currentUser.UserId.Value;
        var now = _clock.UtcNow;

        var platform = request.Platform.Equals("iOS", StringComparison.OrdinalIgnoreCase)
            ? MobilePlatform.Ios
            : MobilePlatform.Android;

        var restaurantId = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Also check via UserScope (for DMs who are scoped to a restaurant but not the owner)
        if (!restaurantId.HasValue)
        {
            restaurantId = await _db.UserScopes
                .AsNoTracking()
                .Where(s => s.UserId == userId && s.AccessScope.IsActive)
                .Select(s => (Guid?)s.AccessScope.RestaurantId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var existing = await _db.RestaurantDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);

        if (existing is not null)
        {
            existing.UserId = userId;
            existing.RestaurantId = restaurantId ?? existing.RestaurantId;
            existing.Platform = platform;
            existing.DeviceId = request.DeviceId?.Trim() ?? existing.DeviceId;
            existing.IsActive = true;
            existing.LastSeenAtUtc = now;
            existing.ModifiedAtUtc = now;
        }
        else
        {
            _db.RestaurantDeviceTokens.Add(new RestaurantDeviceToken
            {
                UserId = userId,
                RestaurantId = restaurantId,
                Token = request.Token,
                Platform = platform,
                DeviceId = request.DeviceId?.Trim(),
                IsActive = true,
                LastSeenAtUtc = now,
                CreatedAtUtc = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "StaffDeviceToken: Registered delivery manager token for UserId={UserId}, RestaurantId={RestaurantId}",
            userId, restaurantId);

        return Result.Success();
    }

    // ─── Role resolution ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the effective role.
    /// Priority: JWT claims > explicit request body role.
    /// </summary>
    private string? ResolveRole(string? explicitRole)
    {
        // 1. Check authenticated roles from JWT
        if (_currentUser.IsInRole(AppRoles.Driver))
            return AppRoles.Driver;

        if (_currentUser.IsInRole(AppRoles.DeliveryManager))
            return AppRoles.DeliveryManager;

        // 2. Fall back to the explicit role provided in the request body
        if (!string.IsNullOrWhiteSpace(explicitRole))
        {
            if (string.Equals(explicitRole, AppRoles.Driver, StringComparison.OrdinalIgnoreCase))
                return AppRoles.Driver;

            if (string.Equals(explicitRole, AppRoles.DeliveryManager, StringComparison.OrdinalIgnoreCase))
                return AppRoles.DeliveryManager;
        }

        return null;
    }
}
