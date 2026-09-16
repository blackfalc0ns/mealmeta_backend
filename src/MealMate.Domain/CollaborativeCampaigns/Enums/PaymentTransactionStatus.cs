namespace MealMate.Domain.CollaborativeCampaigns.Enums;

public enum PaymentTransactionStatus
{
    Initiated = 1,
    Processing = 2,
    Succeeded = 3,
    Failed = 4,
    RefundPending = 5,
    Refunded = 6,
}
