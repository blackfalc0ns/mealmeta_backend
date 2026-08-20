namespace MealMate.Application.Features.CalendarMeals;

public enum CompensationType
{
    ReassignRestaurant = 1,
    WalletCredit = 2,
    SubscriptionExtension = 3
}

public sealed record AdminCompensationRecord(
    Guid DayId,
    CompensationType Type,
    Guid AdminUserId,
    string Reason,
    DateTime CreatedAtUtc,
    Guid? TargetRestaurantId = null,
    Guid? CustomerId = null,
    decimal? Amount = null,
    Guid? SubscriptionId = null,
    int? AdditionalDays = null);

public interface IAdminCompensationService
{
    bool IsSlaBreached(DateTime dispatchedAtUtc, int slaHours, DateTime currentUtc);

    AdminCompensationRecord CreateReassignAction(
        Guid dayId,
        Guid targetRestaurantId,
        Guid adminUserId,
        string reason);

    AdminCompensationRecord CreateWalletRefundAction(
        Guid dayId,
        Guid customerId,
        decimal amount,
        Guid adminUserId,
        string reason);

    AdminCompensationRecord CreateSubscriptionExtensionAction(
        Guid dayId,
        Guid subscriptionId,
        int additionalDays,
        Guid adminUserId,
        string reason);
}

public sealed class AdminCompensationService : IAdminCompensationService
{
    public const int DefaultSlaHours = 3;

    public bool IsSlaBreached(DateTime dispatchedAtUtc, int slaHours, DateTime currentUtc)
    {
        if (slaHours <= 0) slaHours = DefaultSlaHours;
        return currentUtc >= dispatchedAtUtc.AddHours(slaHours);
    }

    public AdminCompensationRecord CreateReassignAction(
        Guid dayId,
        Guid targetRestaurantId,
        Guid adminUserId,
        string reason)
    {
        return new AdminCompensationRecord(
            DayId: dayId,
            Type: CompensationType.ReassignRestaurant,
            AdminUserId: adminUserId,
            Reason: reason,
            CreatedAtUtc: DateTime.UtcNow,
            TargetRestaurantId: targetRestaurantId);
    }

    public AdminCompensationRecord CreateWalletRefundAction(
        Guid dayId,
        Guid customerId,
        decimal amount,
        Guid adminUserId,
        string reason)
    {
        return new AdminCompensationRecord(
            DayId: dayId,
            Type: CompensationType.WalletCredit,
            AdminUserId: adminUserId,
            Reason: reason,
            CreatedAtUtc: DateTime.UtcNow,
            CustomerId: customerId,
            Amount: amount);
    }

    public AdminCompensationRecord CreateSubscriptionExtensionAction(
        Guid dayId,
        Guid subscriptionId,
        int additionalDays,
        Guid adminUserId,
        string reason)
    {
        return new AdminCompensationRecord(
            DayId: dayId,
            Type: CompensationType.SubscriptionExtension,
            AdminUserId: adminUserId,
            Reason: reason,
            CreatedAtUtc: DateTime.UtcNow,
            SubscriptionId: subscriptionId,
            AdditionalDays: additionalDays);
    }
}
