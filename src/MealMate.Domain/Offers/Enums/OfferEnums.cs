namespace MealMate.Domain.Offers.Enums;

public enum OfferStatus
{
    Draft = 1,
    Scheduled = 2,
    Active = 3,
    Paused = 4,
    Stopped = 5,
    Ended = 6,
    Cancelled = 7
}

public enum OfferType
{
    StandardDiscount = 1,
    FirstSubscriptionPromotion = 2,
    RenewalReward = 3,
    TierUpgradeIncentive = 4
}

public enum DiscountType
{
    Percentage = 1,
    FixedAmount = 2,
    FreeDelivery = 3
}

public enum OfferFundingMode
{
    PlatformFull = 1,
    RestaurantFull = 2,
    Shared = 3
}

public enum CustomerSegmentFilter
{
    All = 0,
    NewCustomersOnly = 1,
    ExistingCustomersOnly = 2
}
