namespace MealMate.Domain.Enums;

public enum RegionType
{
    Governorate = 1,
    Area = 2,
    Block = 3,
}

public enum DeliveryManagerCoverageType
{
    AllRestaurantCoverage = 1,
    Governorate = 2,
    Area = 3,
}

public enum UserType
{
    Customer = 1,
    Restaurant = 2,
    Driver = 3,
    Admin = 4,
    Influencer = 5,
    DeliveryManager = 6,
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
    NeedsChanges = 8,
}

public enum ScheduleMode
{
    Cycle = 1,
    Weekday = 2,
}

/// <summary>Dynamic restaurant classification per program/bundle.</summary>
public enum RestaurantTier
{
    Basic = 1,
    Platinum = 2,
    Elite = 3,
}

public enum ProgramStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Active = 4,
    Suspended = 5,
    Archived = 6,
    Rejected = 7,
}

public enum SubscriptionStatus
{
    Draft = 1,
    PendingPayment = 2,
    Paid = 3,
    Scheduled = 4,
    Active = 5,
    Frozen = 6,
    Completed = 7,
    Cancelled = 8,
    CancellationPending = 9,
    Upgraded = 10,
    Expired = 11,
    SuspendedForReview = 12,
    PaymentFailed = 13,
}

public enum FamilyGroupStatus
{
    Draft = 1,
    CheckoutPending = 2,
    Active = 3,
    Frozen = 4,
    Dispute = 5,
    PendingDetach = 6,
    Cancelled = 7,
}

public enum FamilyMemberRole
{
    Manager = 1,
    Member = 2,
}

public enum FamilyMemberSetupStatus
{
    NotStarted = 1,
    InProgress = 2,
    Ready = 3,
}

public enum FamilyActivityLevel
{
    Sedentary = 1,
    Moderate = 2,
    Active = 3,
}

public enum FamilyMemberGender
{
    Male = 1,
    Female = 2,
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

public enum DocumentOwnerType
{
    Customer = 1,
    Restaurant = 2,
    Driver = 3,
    Influencer = 4,
    Admin = 5,
    Registration = 6,
}

public enum DocumentType
{
    NationalId = 1,
    CommercialLicense = 2,
    FoodLicense = 3,
    DrivingLicense = 4,
    VehicleRegistration = 5,
    BankIbanProof = 6,
    Contract = 7,
    Other = 8,
    ProfileImage = 9,
    NationalIdFront = 10,
    NationalIdBack = 11,
    EmploymentContract = 12,
    CommercialRegistration = 13,
    AuthorizedSignatoryId = 14,
    AuthorizedSignatoryAuthorization = 15,
    BranchLicense = 16,
    BranchPhoto = 17,
    MealPhoto = 18,
    DrivingLicenseFront = 19,
    DrivingLicenseBack = 20,
}

public enum RuleApprovalStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Active = 5,
    Superseded = 6,
    Archived = 7,
}

public enum RuleScopeType
{
    Global = 1,
    Country = 2,
    Region = 3,
    Restaurant = 4,
    Program = 5,
    Bundle = 6,
    Campaign = 7,
    PaymentGateway = 8,
}

public enum AccessScopeType
{
    Global = 1,
    Country = 2,
    Region = 3,
    Restaurant = 4,
    Entity = 5,
    Tenant = 6,
}

public enum TenantStatus
{
    Setup = 1,
    Active = 2,
    Suspended = 3,
}

public enum PriceLockStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Locked = 4,
    ChangeRequested = 5,
    ApprovedWithEffectiveDate = 6,
    Superseded = 7,
}

public enum CampaignFundingMode
{
    MealMateFull = 1,
    RestaurantFull = 2,
    Shared = 3,
}

public enum CampaignStatus
{
    Draft = 1,
    EnrollmentOpen = 2,
    UnderReview = 3,
    Approved = 4,
    Launched = 5,
    Paused = 6,
    Completed = 7,
    Cancelled = 8,
}

public enum CapacityType
{
    Base = 1,
    Campaign = 2,
    DateOverride = 3,
}

/// <summary>Lifecycle of capacity reserved while a customer completes payment.</summary>
public enum CapacityHoldStatus
{
    PendingPayment = 1,
    Confirmed = 2,
    Expired = 3,
    Released = 4,
    PaymentConfirmed = 5,
}

public enum WalletType
{
    Cash = 1,
    Promotional = 2,
}

public enum WalletTransactionType
{
    Credit = 1,
    Debit = 2,
    Hold = 3,
    Release = 4,
    Refund = 5,
    Transfer = 6,
}

public enum ComplaintStatus
{
    Submitted = 1,
    UnderReview = 2,
    AwaitingRestaurant = 3,
    DecisionPending = 4,
    Resolved = 5,
    Rejected = 6,
    Closed = 7,
}

public enum ResponsibilityParty
{
    Restaurant = 1,
    Driver = 2,
    Platform = 3,
    Customer = 4,
    ForceMajeure = 5,
    Undetermined = 6,
}

public enum TripStatus
{
    Draft = 1,
    Planned = 2,
    Assigned = 3,
    InProgress = 4,
    Completed = 5,
    Cancelled = 6,
}

public enum JournalEntryStatus
{
    Draft = 1,
    Posted = 2,
    Reversed = 3,
}

public enum PayoutStatus
{
    Pending = 1,
    UnderReview = 2,
    Approved = 3,
    Rejected = 4,
    Paid = 5,
    Failed = 6,
}

public enum WithdrawalStatus
{
    Requested = 1,
    UnderReview = 2,
    Approved = 3,
    Rejected = 4,
    Paid = 5,
    Cancelled = 6,
}

public enum ProfitabilityAlertStatus
{
    Open = 1,
    KeepRestaurant = 2,
    MoveToHigherClassification = 3,
    ExcludeRestaurant = 4,
}

public enum NotificationChannel
{
    InApp = 1,
    Push = 2,
    Sms = 3,
    Email = 4,
}

public enum AccountType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Revenue = 4,
    Expense = 5,
}

/// <summary>Lifecycle of an admin invitation.</summary>
public enum AdminInvitationStatus
{
    Pending = 1,
    Accepted = 2,
    Revoked = 3,
    Expired = 4,
}

public enum DocumentTemplateKind
{
    Invoice = 1,
    BoxLabel = 2,
    MealLabel = 3,
}

public enum DocumentTemplateStatus
{
    Draft = 1,
    Published = 2,
    Archived = 3,
}

public enum MealLabelKind
{
    Box = 1,
    Meal = 2,
}
