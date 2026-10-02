using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.DriverDelivery;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record DriverAppSettingsResponseDto(
    string? PreferredLanguage,
    string? PreferredNavigationApp,
    bool? AutoLaunchNavigation,
    bool? SoundAlertsEnabled,
    bool? VibrationEnabled,
    bool? BiometricLoginEnabled,
    DriverAppSettingsOfflineSyncDto OfflineSync);

/// <summary>
/// Offline-queue state that lives on the device: the server has no offline-queue
/// table, so the count and last-sync timestamp are always null here and the client
/// renders its own local values.
/// </summary>
public sealed record DriverAppSettingsOfflineSyncDto(
    bool? AutoSyncOnReconnect,
    int? PendingSyncItemsCount = null,
    DateTime? LastSyncedAtUtc = null);

public sealed record GetDriverAppSettingsQuery : IQuery<DriverAppSettingsResponseDto>;

public sealed record UpdateDriverAppSettingsCommand(
    string? PreferredLanguage,
    string? PreferredNavigationApp,
    bool? AutoLaunchNavigation,
    bool? SoundAlertsEnabled,
    bool? VibrationEnabled,
    bool? BiometricLoginEnabled,
    bool? AutoSyncOnReconnect) : ICommand<DriverAppSettingsResponseDto>;

public sealed class GetDriverAppSettingsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetDriverAppSettingsQuery, DriverAppSettingsResponseDto>
{
    public async Task<Result<DriverAppSettingsResponseDto>> Handle(GetDriverAppSettingsQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
            return Error.Unauthorized(MessageCodes.Common.Unauthorized);
        if (!currentUser.IsInRole(AppRoles.Driver))
            return Error.Forbidden(MessageCodes.Auth.Forbidden);

        // Read-only ownership lookup. Never accept a driver ID from the client or repair ownership during a GET.
        var driver = await db.DriverProfiles.AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == currentUser.UserId.Value, cancellationToken);
        if (driver is null)
            return Error.NotFound(MessageCodes.Dispatcher.DriverNotFound);

        var settings = await db.DriverAppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id, cancellationToken);

        return Result.Success(Map(settings));
    }

    internal static DriverAppSettingsResponseDto Map(DriverAppSettings? settings) => new(
        settings?.PreferredLanguage,
        settings?.PreferredNavigationApp,
        settings?.AutoLaunchNavigation,
        settings?.SoundAlertsEnabled,
        settings?.VibrationEnabled,
        settings?.BiometricLoginEnabled,
        new DriverAppSettingsOfflineSyncDto(settings?.AutoSyncOnReconnect));
}

public sealed class UpdateDriverAppSettingsCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateDriverAppSettingsCommand, DriverAppSettingsResponseDto>
{
    private static readonly string[] NavigationApps = ["GoogleMaps", "Waze", "AppleMaps"];

    public async Task<Result<DriverAppSettingsResponseDto>> Handle(UpdateDriverAppSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
            return Error.Unauthorized(MessageCodes.Common.Unauthorized);
        if (!currentUser.IsInRole(AppRoles.Driver))
            return Error.Forbidden(MessageCodes.Auth.Forbidden);

        string? language = null;
        if (request.PreferredLanguage is { } requestedLanguage)
        {
            language = requestedLanguage.Trim().ToLowerInvariant();
            if (language is not ("ar" or "en"))
                return Error.Validation(MessageCodes.DriverSettings.LanguageInvalid);
        }

        string? navigationApp = null;
        if (request.PreferredNavigationApp is { } requestedNavigationApp)
        {
            navigationApp = NavigationApps.FirstOrDefault(a =>
                string.Equals(a, requestedNavigationApp.Trim(), StringComparison.OrdinalIgnoreCase));
            if (navigationApp is null)
                return Error.Validation(MessageCodes.DriverSettings.NavigationAppInvalid);
        }

        var driver = await db.DriverProfiles.AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == currentUser.UserId.Value, cancellationToken);
        if (driver is null)
            return Error.NotFound(MessageCodes.Dispatcher.DriverNotFound);

        var settings = await db.DriverAppSettings
            .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id, cancellationToken);
        if (settings is null)
        {
            settings = new DriverAppSettings { DriverProfileId = driver.Id };
            db.DriverAppSettings.Add(settings);
        }

        // Partial update: fields the client omitted (null) keep their stored value; unsetting to null is not supported.
        if (language is not null) settings.PreferredLanguage = language;
        if (navigationApp is not null) settings.PreferredNavigationApp = navigationApp;
        if (request.AutoLaunchNavigation.HasValue) settings.AutoLaunchNavigation = request.AutoLaunchNavigation;
        if (request.SoundAlertsEnabled.HasValue) settings.SoundAlertsEnabled = request.SoundAlertsEnabled;
        if (request.VibrationEnabled.HasValue) settings.VibrationEnabled = request.VibrationEnabled;
        if (request.BiometricLoginEnabled.HasValue) settings.BiometricLoginEnabled = request.BiometricLoginEnabled;
        if (request.AutoSyncOnReconnect.HasValue) settings.AutoSyncOnReconnect = request.AutoSyncOnReconnect;

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(GetDriverAppSettingsQueryHandler.Map(settings));
    }
}
