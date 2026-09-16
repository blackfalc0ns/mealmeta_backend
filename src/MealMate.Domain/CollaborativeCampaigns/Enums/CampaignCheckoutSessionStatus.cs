namespace MealMate.Domain.CollaborativeCampaigns.Enums;

public enum CampaignCheckoutSessionStatus
{
    Pending = 1,
    PaymentPending = 2,
    Paid = 3,
    Committing = 4,
    Completed = 5,
    PaymentFailed = 6,
    Expired = 7,
    RefundPending = 8,
    Refunded = 9
}
