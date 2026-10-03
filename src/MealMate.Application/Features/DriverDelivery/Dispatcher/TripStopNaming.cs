using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Foundation;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

/// <summary>
/// Shared naming rules for trip stops created from subscription days:
/// the stop zone must be the customer's delivery region name only, while the
/// full address line is snapshotted separately into TripStop.FullAddress.
/// </summary>
internal static class TripStopNaming
{
    public static (string ZoneAr, string ZoneEn) ResolveZone(SubscriptionDay day, Region? region)
    {
        if (region is not null)
        {
            return (region.NameAr, region.NameEn);
        }

        // Legacy fallback for days without a resolved delivery region.
        if (!string.IsNullOrWhiteSpace(day.DeliveryAddressLine))
        {
            var legacy = day.DeliveryAddressLine.StartsWith("منطقة", StringComparison.Ordinal)
                ? day.DeliveryAddressLine
                : $"منطقة {day.DeliveryAddressLine}";
            return (legacy, legacy);
        }

        return ("منطقة العاصمة", "منطقة العاصمة");
    }
}
