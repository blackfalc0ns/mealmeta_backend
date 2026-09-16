namespace MealMate.Domain.CollaborativeCampaigns.Enums;

public enum CampaignLedgerEntryType
{
    CustomerPayment = 1,
    RestaurantSettlement = 2,
    PaymentProcessingFee = 3,
    CustomerRefund = 4,
    RestaurantSettlementReversal = 5,
    PaymentFeeReversal = 6,
    ManualFinancialAdjustment = 7,
}
