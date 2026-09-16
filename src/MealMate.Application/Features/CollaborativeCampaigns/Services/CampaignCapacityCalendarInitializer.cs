using MealMate.Domain.CollaborativeCampaigns;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed record RestaurantCapacityInput(
    Guid RestaurantId,
    int DedicatedDailyCapacity);

public sealed record CapacityCalendarInitResult(
    DateOnly StartDate,
    DateOnly HorizonEndDate,
    int TotalDays,
    int TotalCapacitySlots,
    IReadOnlyList<CampaignRestaurantCapacity> Capacities);

public interface ICampaignCapacityCalendarInitializer
{
    CapacityCalendarInitResult Initialize(
        Guid campaignId,
        DateTime salesStartsAtUtc,
        DateTime salesEndsAtUtc,
        int maxSubscriptionDurationDays,
        IReadOnlyList<RestaurantCapacityInput> restaurants,
        int bufferDays = 1,
        Func<DateOnly, bool>? isServiceDayPredicate = null);
}

public sealed class CampaignCapacityCalendarInitializer : ICampaignCapacityCalendarInitializer
{
    public CapacityCalendarInitResult Initialize(
        Guid campaignId,
        DateTime salesStartsAtUtc,
        DateTime salesEndsAtUtc,
        int maxSubscriptionDurationDays,
        IReadOnlyList<RestaurantCapacityInput> restaurants,
        int bufferDays = 1,
        Func<DateOnly, bool>? isServiceDayPredicate = null)
    {
        if (salesStartsAtUtc > salesEndsAtUtc)
        {
            throw new ArgumentException("Sales start date must precede sales end date.", nameof(salesStartsAtUtc));
        }

        if (maxSubscriptionDurationDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSubscriptionDurationDays), "Max subscription duration days must be greater than zero.");
        }

        var startDate = DateOnly.FromDateTime(salesStartsAtUtc.Date);
        var salesEndDate = DateOnly.FromDateTime(salesEndsAtUtc.Date);
        var horizonEndDate = salesEndDate.AddDays(maxSubscriptionDurationDays + Math.Max(0, bufferDays));

        if (restaurants.Count == 0)
        {
            var days = (horizonEndDate.DayNumber - startDate.DayNumber) + 1;
            return new CapacityCalendarInitResult(startDate, horizonEndDate, days, 0, Array.Empty<CampaignRestaurantCapacity>());
        }

        var capacities = new List<CampaignRestaurantCapacity>();
        int totalDays = 0;

        for (var current = startDate; current <= horizonEndDate; current = current.AddDays(1))
        {
            if (isServiceDayPredicate != null && !isServiceDayPredicate(current))
            {
                continue;
            }

            totalDays++;

            foreach (var rest in restaurants)
            {
                capacities.Add(new CampaignRestaurantCapacity
                {
                    CampaignId = campaignId,
                    RestaurantId = rest.RestaurantId,
                    ServiceDate = current,
                    MaxBoxes = rest.DedicatedDailyCapacity,
                    ReservedBoxes = 0
                });
            }
        }

        var totalSlots = capacities.Sum(c => c.MaxBoxes);

        return new CapacityCalendarInitResult(
            startDate,
            horizonEndDate,
            totalDays,
            totalSlots,
            capacities);
    }
}
