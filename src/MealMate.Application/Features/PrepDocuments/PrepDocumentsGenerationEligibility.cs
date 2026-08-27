using MealMate.Domain.Enums;

namespace MealMate.Application.Features.PrepDocuments;

public static class PrepDocumentsGenerationEligibility
{
    public static readonly SubscriptionDayStatus[] EligibleStatuses =
    [
        SubscriptionDayStatus.ConfirmedByRestaurant,
        SubscriptionDayStatus.Preparing,
        SubscriptionDayStatus.OutForDelivery,
        SubscriptionDayStatus.Delivered,
    ];

    public static bool IsPrepWindowStarted(DateOnly serviceDate, DateTime? selectionDeadlineUtc, DateTime utcNow)
    {
        if (selectionDeadlineUtc is { } deadline)
            return utcNow >= deadline.AddHours(48);

        return utcNow >= serviceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-24);
    }

    public static bool IsEligibleForGeneration(
        SubscriptionDayStatus status,
        DateOnly serviceDate,
        DateTime? selectionDeadlineUtc,
        bool hasBarcode,
        DateTime utcNow)
    {
        if (hasBarcode)
            return false;

        if (!EligibleStatuses.Contains(status))
            return false;

        return IsPrepWindowStarted(serviceDate, selectionDeadlineUtc, utcNow);
    }
}
