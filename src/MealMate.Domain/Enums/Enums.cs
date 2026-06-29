namespace MealMate.Domain.Enums;

public enum UserType
{
    Customer = 1,
    Restaurant = 2,
    Driver = 3,
    Admin = 4,
    Influencer = 5,
}

public enum AccountStatus
{
    Registered = 1,
    PhonePending = 2,
    Active = 3,
    Suspended = 4,
    Closed = 5,
}

public enum VerificationStatus
{
    Draft = 1,
    Submitted = 2,
    UnderReview = 3,
    Approved = 4,
    Rejected = 5,
    Expired = 6,
    ResubmissionRequired = 7,
}

/// <summary>Dynamic restaurant classification per program/bundle.</summary>
public enum RestaurantTier
{
    Basic = 1,
    Platinum = 2,
    Elite = 3,
}

public enum SubscriptionStatus
{
    Draft = 1,
    PendingPayment = 2,
    Active = 3,
    Frozen = 4,
    Completed = 5,
    Cancelled = 6,
}

/// <summary>Daily lifecycle inside the smart calendar / 72h flow.</summary>
public enum SubscriptionDayStatus
{
    Open = 1,
    AutoSelected = 2,
    CustomerSelected = 3,
    Locked72h = 4,
    SentToRestaurant = 5,
    ConfirmedByRestaurant = 6,
    ReplacementRequired = 7,
    ReplacementWindowOpen = 8,
    Preparing = 9,
    OutForDelivery = 10,
    Delivered = 11,
    Frozen = 12,
    Cancelled = 13,
    Failed = 14,
}

public enum OrderStatus
{
    Created = 1,
    AwaitingRestaurantConfirmation = 2,
    RestaurantConfirmed = 3,
    RestaurantRejected = 4,
    ReplacementPending = 5,
    Preparing = 6,
    ReadyForPickup = 7,
    AssignedToDriver = 8,
    PickedUp = 9,
    OnTheWay = 10,
    Hold = 11,
    SecondAttemptScheduled = 12,
    Delivered = 13,
    DeliveryFailed = 14,
    Cancelled = 15,
}

public enum PaymentStatus
{
    Pending = 1,
    Authorized = 2,
    Succeeded = 3,
    Failed = 4,
    Refunded = 5,
    PartiallyRefunded = 6,
}
