using System.ComponentModel.DataAnnotations;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

/// <summary>
/// Tunables of the driver delivery-map navigation builder. Bound from the
/// "DriverNavigation" configuration section; the defaults below are the
/// production values, so no configuration entry needs to exist.
/// </summary>
public sealed class DriverNavigationOptions
{
    public const string SectionName = "DriverNavigation";

    /// <summary>
    /// A delivery-tracking point older than this (seconds) is reported as
    /// stale instead of a live location. Default 120s per the page contract.
    /// </summary>
    [Range(1, 3600)]
    public int MaxLocationAgeSeconds { get; set; } = 120;

    /// <summary>
    /// Only successful real routes are cached, for this many seconds, per
    /// driver + stop + exact origin/destination coordinates.
    /// </summary>
    [Range(1, 600)]
    public int RouteCacheSeconds { get; set; } = 30;
}
