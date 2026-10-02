using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

/// <summary>
/// Server-stored preferences of the driver app (Screen 09.05). One row per driver,
/// created lazily on the first update; a missing row means "never saved on the server".
/// Device-local state (offline queue size, last sync time) is intentionally not persisted here.
/// </summary>
public sealed class DriverAppSettings : AuditableEntity
{
    public Guid DriverProfileId { get; set; }

    /// <summary>Canonical values: "ar" or "en". Null until the driver saves a language.</summary>
    public string? PreferredLanguage { get; set; }

    /// <summary>Canonical values: "GoogleMaps", "Waze", "AppleMaps". Null until saved.</summary>
    public string? PreferredNavigationApp { get; set; }

    public bool? AutoLaunchNavigation { get; set; }

    public bool? SoundAlertsEnabled { get; set; }

    public bool? VibrationEnabled { get; set; }

    public bool? BiometricLoginEnabled { get; set; }

    public bool? AutoSyncOnReconnect { get; set; }
}
