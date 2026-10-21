using System.Linq.Expressions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Advertising;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Common;
using MealMate.Domain.Entities;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Influencers;
using MealMate.Domain.Offers;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantFinance;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.VoiceCalls;
using MealMate.Persistence.Common;
using MealMate.Persistence.Features.DriverDelivery;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence;

/// <summary>
/// Application database context. Backed by PostgreSQL and ASP.NET Core Identity.
/// Also acts as the unit of work and dispatches domain events after persistence.
/// </summary>
public sealed class AppDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IUnitOfWork, IAppDbContext
{
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly IDateTimeProvider? _dateTimeProvider;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IDateTimeProvider? dateTimeProvider = null)
        : base(options)
    {
        _domainEventDispatcher = domainEventDispatcher;
        _dateTimeProvider = dateTimeProvider;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    // â”€â”€ Phase 0 Foundation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public async Task<string?> GetUserPhoneAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.PhoneNumber)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetUserDisplayNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => user.FullName).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetActiveAccountUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0) return new HashSet<Guid>();
        return await Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id) && user.AccountStatus == MealMate.Domain.Enums.AccountStatus.Active)
            .Select(user => user.Id)
            .ToHashSetAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetDeliveryManagerUserIdsAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        return await Users.AsNoTracking()
            .Where(user => user.UserType == MealMate.Domain.Enums.UserType.DeliveryManager
                && user.AccountStatus == MealMate.Domain.Enums.AccountStatus.Active
                && (user.RestaurantId == restaurantId
                    || (user.RestaurantId == null && DeliveryManagerRegistrations.Any(registration =>
                        registration.RestaurantId == restaurantId && registration.ResultingUserId == user.Id
                        && registration.Status == MealMate.Domain.Enums.VerificationStatus.Approved && !registration.IsDeleted))))
            .Select(user => user.Id).ToHashSetAsync(cancellationToken);
    }

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<RuleVersion> RuleVersions => Set<RuleVersion>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVerification> DocumentVerifications => Set<DocumentVerification>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    // â”€â”€ M01 Identity & Access â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AccessScope> AccessScopes => Set<AccessScope>();
    public DbSet<UserScope> UserScopes => Set<UserScope>();
    public DbSet<AdminInvitation> AdminInvitations => Set<AdminInvitation>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<RestaurantProfile> RestaurantProfiles => Set<RestaurantProfile>();
    public DbSet<DriverProfile> DriverProfiles => Set<DriverProfile>();
    public DbSet<InfluencerProfile> InfluencerProfiles => Set<InfluencerProfile>();
    public DbSet<RestaurantRegistration> RestaurantRegistrations => Set<RestaurantRegistration>();
    public DbSet<RestaurantRegistrationFieldReview> RestaurantRegistrationFieldReviews => Set<RestaurantRegistrationFieldReview>();
    public DbSet<RestaurantReviewItem> RestaurantReviewItems => Set<RestaurantReviewItem>();
    public DbSet<RestaurantReviewHistory> RestaurantReviewHistories => Set<RestaurantReviewHistory>();
    public DbSet<RestaurantNotification> RestaurantNotifications => Set<RestaurantNotification>();
    public DbSet<RestaurantNotificationPreference> RestaurantNotificationPreferences =>
        Set<RestaurantNotificationPreference>();
    public DbSet<CustomerDeviceToken> CustomerDeviceTokens => Set<CustomerDeviceToken>();
    public DbSet<CustomerPushDeliveryLog> CustomerPushDeliveryLogs => Set<CustomerPushDeliveryLog>();
    public DbSet<CustomerNotification> CustomerNotifications => Set<CustomerNotification>();
    public DbSet<AdminDeviceToken> AdminDeviceTokens => Set<AdminDeviceToken>();
    public DbSet<RestaurantDeviceToken> RestaurantDeviceTokens => Set<RestaurantDeviceToken>();
    public DbSet<DriverDeviceToken> DriverDeviceTokens => Set<DriverDeviceToken>();
    public DbSet<DriverAppSettings> DriverAppSettings => Set<DriverAppSettings>();
    public DbSet<DriverNotification> DriverNotifications => Set<DriverNotification>();
    public DbSet<DeliveryManagerDeviceToken> DeliveryManagerDeviceTokens => Set<DeliveryManagerDeviceToken>();
    public DbSet<DeliveryManagerNotification> DeliveryManagerNotifications => Set<DeliveryManagerNotification>();
    public DbSet<RestaurantSettingsChangeRequest> RestaurantSettingsChangeRequests => Set<RestaurantSettingsChangeRequest>();
    public DbSet<DriverRegistration> DriverRegistrations => Set<DriverRegistration>();
    public DbSet<DeliveryManagerRegistration> DeliveryManagerRegistrations => Set<DeliveryManagerRegistration>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<AuthLoginChallenge> AuthLoginChallenges => Set<AuthLoginChallenge>();

    // â”€â”€ M02 Programs & Subscriptions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<Program> Programs => Set<Program>();
    public DbSet<Bundle> Bundles => Set<Bundle>();
    public DbSet<PackageContentVersion> PackageContentVersions => Set<PackageContentVersion>();
    public DbSet<RestaurantBundlePricing> RestaurantBundlePricings => Set<RestaurantBundlePricing>();
    public DbSet<RestaurantProgramBundleOffer> RestaurantProgramBundleOffers => Set<RestaurantProgramBundleOffer>();
    public DbSet<ClassificationRun> ClassificationRuns => Set<ClassificationRun>();
    public DbSet<RestaurantClassification> RestaurantClassifications => Set<RestaurantClassification>();
    public DbSet<PricingRuleVersion> PricingRuleVersions => Set<PricingRuleVersion>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionSnapshot> SubscriptionSnapshots => Set<SubscriptionSnapshot>();
    public DbSet<SubscriptionOperation> SubscriptionOperations => Set<SubscriptionOperation>();
    public DbSet<DiscountsAndReferralLog> DiscountsAndReferralLogs => Set<DiscountsAndReferralLog>();
    public DbSet<FamilySubscription> FamilySubscriptions => Set<FamilySubscription>();
    public DbSet<FamilyGroup> FamilyGroups => Set<FamilyGroup>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<FamilyMemberAccess> FamilyMemberAccesses => Set<FamilyMemberAccess>();
    public DbSet<FamilyCredentialOperation> FamilyCredentialOperations => Set<FamilyCredentialOperation>();
    public DbSet<FamilyMemberPermissions> FamilyMemberPermissions => Set<FamilyMemberPermissions>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<PromoCodeRedemption> PromoCodeRedemptions => Set<PromoCodeRedemption>();
    public DbSet<ReferralCode> ReferralCodes => Set<ReferralCode>();
    public DbSet<ReferralAttribution> ReferralAttributions => Set<ReferralAttribution>();
    public DbSet<SubscriptionTierUpgrade> SubscriptionTierUpgrades => Set<SubscriptionTierUpgrade>();
    public DbSet<SubscriptionFreezeRequest> SubscriptionFreezeRequests => Set<SubscriptionFreezeRequest>();
    public DbSet<SubscriptionRenewal> SubscriptionRenewals => Set<SubscriptionRenewal>();

    // â”€â”€ M03 Calendar & Meals â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<SubscriptionCalendar> SubscriptionCalendars => Set<SubscriptionCalendar>();
    public DbSet<SubscriptionDay> SubscriptionDays => Set<SubscriptionDay>();
    public DbSet<DailyRestaurantSelection> DailyRestaurantSelections => Set<DailyRestaurantSelection>();
    public DbSet<DailyMealSelection> DailyMealSelections => Set<DailyMealSelection>();
    public DbSet<CustomerAllergyPreference> CustomerAllergyPreferences => Set<CustomerAllergyPreference>();
    public DbSet<CustomerDietaryPreference> CustomerDietaryPreferences => Set<CustomerDietaryPreference>();
    public DbSet<SubscriptionDayMealSnapshot> SubscriptionDayMealSnapshots => Set<SubscriptionDayMealSnapshot>();
    public DbSet<SubscriptionRestaurantPoolSnapshot> SubscriptionRestaurantPoolSnapshots => Set<SubscriptionRestaurantPoolSnapshot>();
    public DbSet<SubscriptionRestaurantSnapshot> SubscriptionRestaurantSnapshots => Set<SubscriptionRestaurantSnapshot>();
    public DbSet<SubscriptionRestaurantDaySnapshot> SubscriptionRestaurantDaySnapshots => Set<SubscriptionRestaurantDaySnapshot>();
    public DbSet<SubscriptionSnapshotMigrationIssue> SubscriptionSnapshotMigrationIssues => Set<SubscriptionSnapshotMigrationIssue>();
    public DbSet<MealChangeRequest> MealChangeRequests => Set<MealChangeRequest>();
    public DbSet<SubscriptionFreeze> SubscriptionFreezes => Set<SubscriptionFreeze>();
    public DbSet<FreezeLedgerEntry> FreezeLedgerEntries => Set<FreezeLedgerEntry>();
    public DbSet<DiversificationLimitSnapshot> DiversificationLimitSnapshots => Set<DiversificationLimitSnapshot>();

    // â”€â”€ M04 Restaurant Operations â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<MenuPlan> MenuPlans => Set<MenuPlan>();
    public DbSet<MenuPlanVersion> MenuPlanVersions => Set<MenuPlanVersion>();
    public DbSet<MenuDay> MenuDays => Set<MenuDay>();
    public DbSet<MenuDayMeal> MenuDayMeals => Set<MenuDayMeal>();
    public DbSet<WeekdayMealAssignment> WeekdayMealAssignments => Set<WeekdayMealAssignment>();
    public DbSet<MenuPlanProgram> MenuPlanPrograms => Set<MenuPlanProgram>();
    public DbSet<ProgramMealSlotRequirement> ProgramMealSlotRequirements => Set<ProgramMealSlotRequirement>();
    public DbSet<ProgramNutritionStandard> ProgramNutritionStandards => Set<ProgramNutritionStandard>();
    public DbSet<MenuPlanAudit> MenuPlanAudits => Set<MenuPlanAudit>();
    public DbSet<MealProgramAssignment> MealProgramAssignments => Set<MealProgramAssignment>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<MealAllergen> MealAllergens => Set<MealAllergen>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<MealIngredient> MealIngredients => Set<MealIngredient>();
    public DbSet<MealImage> MealImages => Set<MealImage>();
    public DbSet<AllergenDefinition> AllergenDefinitions => Set<AllergenDefinition>();
    public DbSet<FoodPreferenceDefinition> FoodPreferenceDefinitions => Set<FoodPreferenceDefinition>();
    public DbSet<IngredientFoodPreference> IngredientFoodPreferences => Set<IngredientFoodPreference>();
    public DbSet<MealFoodPreference> MealFoodPreferences => Set<MealFoodPreference>();
    public DbSet<CapacityHold> CapacityHolds => Set<CapacityHold>();
    public DbSet<RestaurantServiceRegion> RestaurantServiceRegions => Set<RestaurantServiceRegion>();
    public DbSet<DailyCapacity> DailyCapacities => Set<DailyCapacity>();
    public DbSet<BusyStatus> BusyStatuses => Set<BusyStatus>();
    public DbSet<RestaurantOperatingSchedule> RestaurantOperatingSchedules => Set<RestaurantOperatingSchedule>();
    public DbSet<RestaurantOperatingDayException> RestaurantOperatingDayExceptions => Set<RestaurantOperatingDayException>();
    public DbSet<RestaurantEmergencyOverride> RestaurantEmergencyOverrides => Set<RestaurantEmergencyOverride>();
    public DbSet<RestaurantDailyOrder> RestaurantDailyOrders => Set<RestaurantDailyOrder>();
    public DbSet<RestaurantConfirmation> RestaurantConfirmations => Set<RestaurantConfirmation>();
    public DbSet<MealInvoice> MealInvoices => Set<MealInvoice>();
    public DbSet<MealBarcode> MealBarcodes => Set<MealBarcode>();
    public DbSet<MealLabel> MealLabels => Set<MealLabel>();
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<ReceiptTemplate> ReceiptTemplates => Set<ReceiptTemplate>();
    public DbSet<RestaurantReceiptSettings> RestaurantReceiptSettings => Set<RestaurantReceiptSettings>();
    public DbSet<ReceiptPrintLog> ReceiptPrintLogs => Set<ReceiptPrintLog>();
    public DbSet<MealBoxLabelTemplate> MealBoxLabelTemplates => Set<MealBoxLabelTemplate>();
    public DbSet<RestaurantMealLabelSettings> RestaurantMealLabelSettings => Set<RestaurantMealLabelSettings>();
    public DbSet<MealLabelPrintLog> MealLabelPrintLogs => Set<MealLabelPrintLog>();
    public DbSet<OrderHandover> OrderHandovers => Set<OrderHandover>();
    public DbSet<OrderHandoverRevocation> OrderHandoverRevocations => Set<OrderHandoverRevocation>();
    public DbSet<RestaurantDriverLink> RestaurantDriverLinks => Set<RestaurantDriverLink>();
    public DbSet<RestaurantBranch> RestaurantBranches => Set<RestaurantBranch>();
    public DbSet<BranchStaffAssignment> BranchStaffAssignments => Set<BranchStaffAssignment>();
    public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();
    public DbSet<RestaurantRolePermission> RestaurantRolePermissions => Set<RestaurantRolePermission>();
    public DbSet<RestaurantCustomRole> RestaurantCustomRoles => Set<RestaurantCustomRole>();
    public DbSet<RestaurantRating> RestaurantRatings => Set<RestaurantRating>();
    public DbSet<MealRating> MealRatings => Set<MealRating>();
    public DbSet<RestaurantContractTermination> RestaurantContractTerminations => Set<RestaurantContractTermination>();

    // â”€â”€ M05 Driver & Delivery â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripStop> TripStops => Set<TripStop>();
    public DbSet<DriverAssignment> DriverAssignments => Set<DriverAssignment>();
    public DbSet<DriverSupportHandover> DriverSupportHandovers => Set<DriverSupportHandover>();
    public DbSet<DriverSupportHandoverBox> DriverSupportHandoverBoxes => Set<DriverSupportHandoverBox>();
    public DbSet<PickupScan> PickupScans => Set<PickupScan>();
    public DbSet<DeliveryTrackingPoint> DeliveryTrackingPoints => Set<DeliveryTrackingPoint>();
    public DbSet<SafeContactLog> SafeContactLogs => Set<SafeContactLog>();
    public DbSet<DeliveryHold> DeliveryHolds => Set<DeliveryHold>();
    public DbSet<SecondDeliveryAttempt> SecondDeliveryAttempts => Set<SecondDeliveryAttempt>();
    public DbSet<DeliveryConfirmationScan> DeliveryConfirmationScans => Set<DeliveryConfirmationScan>();
    public DbSet<DriverRating> DriverRatings => Set<DriverRating>();
    public DbSet<DriverPerformanceStat> DriverPerformanceStats => Set<DriverPerformanceStat>();
    public DbSet<DriverWorkSession> DriverWorkSessions => Set<DriverWorkSession>();
    public DbSet<PickupValidation> PickupValidations => Set<PickupValidation>();
    public DbSet<PickupConditionPhoto> PickupConditionPhotos => Set<PickupConditionPhoto>();
    public DbSet<MealMate.Domain.Common.UploadFileRecord> UploadFileRecords => Set<MealMate.Domain.Common.UploadFileRecord>();

    // â”€â”€ M06 Complaints & Support â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<ComplaintEvidencePhoto> ComplaintEvidencePhotos => Set<ComplaintEvidencePhoto>();
    public DbSet<ComplaintClassification> ComplaintClassifications => Set<ComplaintClassification>();
    public DbSet<AdminComplaintReview> AdminComplaintReviews => Set<AdminComplaintReview>();
    public DbSet<RestaurantComplaintResponse> RestaurantComplaintResponses => Set<RestaurantComplaintResponse>();
    public DbSet<ResponsibilityDecision> ResponsibilityDecisions => Set<ResponsibilityDecision>();
    public DbSet<CustomerCompensation> CustomerCompensations => Set<CustomerCompensation>();
    public DbSet<RestaurantBoxDeduction> RestaurantBoxDeductions => Set<RestaurantBoxDeduction>();
    public DbSet<FreeDayCompensation> FreeDayCompensations => Set<FreeDayCompensation>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();

    // â”€â”€ M07 Central Accounting â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<FinancialSetting> FinancialSettings => Set<FinancialSetting>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<DeferredRevenue> DeferredRevenues => Set<DeferredRevenue>();
    public DbSet<DailyRevenueRecognition> DailyRevenueRecognitions => Set<DailyRevenueRecognition>();
    public DbSet<RestaurantPayableLedger> RestaurantPayableLedgers => Set<RestaurantPayableLedger>();
    public DbSet<InfluencerPayableLedger> InfluencerPayableLedgers => Set<InfluencerPayableLedger>();
    public DbSet<PaymentGatewayAttempt> PaymentGatewayAttempts => Set<PaymentGatewayAttempt>();
    public DbSet<PayzahGatewayConfig> PayzahGatewayConfigs => Set<PayzahGatewayConfig>();
    public DbSet<PaymentWebhookReceipt> PaymentWebhookReceipts => Set<PaymentWebhookReceipt>();
    public DbSet<PaymentGatewayConfig> PaymentGatewayConfigs => Set<PaymentGatewayConfig>();
    public DbSet<TaxRecord> TaxRecords => Set<TaxRecord>();
    public DbSet<OperatingExpense> OperatingExpenses => Set<OperatingExpense>();
    public DbSet<FinancialSettlement> FinancialSettlements => Set<FinancialSettlement>();
    public DbSet<FinancialAuditTrail> FinancialAuditTrails => Set<FinancialAuditTrail>();

    // â”€â”€ M08 Customer Finance â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<CustomerCashWallet> CustomerCashWallets => Set<CustomerCashWallet>();
    public DbSet<CustomerPromotionalWallet> CustomerPromotionalWallets => Set<CustomerPromotionalWallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
    public DbSet<SubscriptionCancellation> SubscriptionCancellations => Set<SubscriptionCancellation>();
    public DbSet<RefundCalculation> RefundCalculations => Set<RefundCalculation>();
    public DbSet<RefundOperation> RefundOperations => Set<RefundOperation>();
    public DbSet<CancellationFeeRecord> CancellationFeeRecords => Set<CancellationFeeRecord>();
    public DbSet<CustomerWithdrawalRequest> CustomerWithdrawalRequests => Set<CustomerWithdrawalRequest>();
    public DbSet<CustomerBankDetails> CustomerBankDetails => Set<CustomerBankDetails>();

    // â”€â”€ M09 Restaurant Finance â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<MealPayable> MealPayables => Set<MealPayable>();
    public DbSet<RestaurantCommissionRecord> RestaurantCommissionRecords => Set<RestaurantCommissionRecord>();
    public DbSet<ComplaintDeductionRecord> ComplaintDeductionRecords => Set<ComplaintDeductionRecord>();
    public DbSet<RestaurantMonthlyInvoice> RestaurantMonthlyInvoices => Set<RestaurantMonthlyInvoice>();
    public DbSet<RestaurantPartialPayment> RestaurantPartialPayments => Set<RestaurantPartialPayment>();
    public DbSet<RestaurantPayout> RestaurantPayouts => Set<RestaurantPayout>();
    public DbSet<TransferReceipt> TransferReceipts => Set<TransferReceipt>();
    public DbSet<RestaurantAccountStatement> RestaurantAccountStatements => Set<RestaurantAccountStatement>();

    // â”€â”€ M10 Influencers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<InfluencerCustomerLink> InfluencerCustomerLinks => Set<InfluencerCustomerLink>();
    public DbSet<InfluencerCode> InfluencerCodes => Set<InfluencerCode>();
    public DbSet<InfluencerCommissionCalculation> InfluencerCommissionCalculations => Set<InfluencerCommissionCalculation>();
    public DbSet<InfluencerEligibilityRule> InfluencerEligibilityRules => Set<InfluencerEligibilityRule>();
    public DbSet<InfluencerCancellationImpact> InfluencerCancellationImpacts => Set<InfluencerCancellationImpact>();
    public DbSet<InfluencerWithdrawalRequest> InfluencerWithdrawalRequests => Set<InfluencerWithdrawalRequest>();
    public DbSet<InfluencerWithdrawalApproval> InfluencerWithdrawalApprovals => Set<InfluencerWithdrawalApproval>();
    public DbSet<InfluencerPayoutHistory> InfluencerPayoutHistories => Set<InfluencerPayoutHistory>();

    // â”€â”€ M11 Collaborative Campaigns â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignDiscountSplit> CampaignDiscountSplits => Set<CampaignDiscountSplit>();
    public DbSet<CampaignProgram> CampaignPrograms => Set<CampaignProgram>();
    public DbSet<CampaignBundle> CampaignBundles => Set<CampaignBundle>();
    public DbSet<CampaignBundleDurationPrice> CampaignBundleDurationPrices => Set<CampaignBundleDurationPrice>();
    public DbSet<CampaignRestaurantEnrollment> CampaignRestaurantEnrollments => Set<CampaignRestaurantEnrollment>();
    public DbSet<CampaignRestaurantCapacity> CampaignRestaurantCapacities => Set<CampaignRestaurantCapacity>();
    public DbSet<CampaignSubscriberCap> CampaignSubscriberCaps => Set<CampaignSubscriberCap>();
    public DbSet<CampaignCapacityReview> CampaignCapacityReviews => Set<CampaignCapacityReview>();
    public DbSet<CampaignRestaurantApproval> CampaignRestaurantApprovals => Set<CampaignRestaurantApproval>();
    public DbSet<CampaignAccountingEntry> CampaignAccountingEntries => Set<CampaignAccountingEntry>();
    public DbSet<CampaignPaymentTransaction> CampaignPaymentTransactions => Set<CampaignPaymentTransaction>();
    public DbSet<CampaignFinancialLedger> CampaignFinancialLedgers => Set<CampaignFinancialLedger>();
    public DbSet<CampaignAuditLog> CampaignAuditLogs => Set<CampaignAuditLog>();
    public DbSet<CampaignTierSnapshot> CampaignTierSnapshots => Set<CampaignTierSnapshot>();
    public DbSet<CampaignProfitabilitySnapshot> CampaignProfitabilitySnapshots => Set<CampaignProfitabilitySnapshot>();
    public DbSet<CampaignCheckoutSession> CampaignCheckoutSessions => Set<CampaignCheckoutSession>();
    public DbSet<CampaignReservationAllocation> CampaignReservationAllocations => Set<CampaignReservationAllocation>();
    public DbSet<CampaignPricingPolicy> CampaignPricingPolicies => Set<CampaignPricingPolicy>();
    public DbSet<CustomerCampaignQuote> CustomerCampaignQuotes => Set<CustomerCampaignQuote>();

    // â”€â”€ Advertising â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<AdvertisingSettings> AdvertisingSettings => Set<AdvertisingSettings>();
    public DbSet<AdCommercialPolicy> AdCommercialPolicies => Set<AdCommercialPolicy>();
    public DbSet<AdSpace> AdSpaces => Set<AdSpace>();
    public DbSet<AdAuction> AdAuctions => Set<AdAuction>();
    public DbSet<AdBid> AdBids => Set<AdBid>();
    public DbSet<AdAuctionNextBidderOffer> AdAuctionNextBidderOffers => Set<AdAuctionNextBidderOffer>();
    public DbSet<AdPlacement> AdPlacements => Set<AdPlacement>();
    public DbSet<AdPaymentTransaction> AdPaymentTransactions => Set<AdPaymentTransaction>();
    public DbSet<AdFinancialLedgerEntry> AdFinancialLedgerEntries => Set<AdFinancialLedgerEntry>();
    public DbSet<AdPlacementEvent> AdPlacementEvents => Set<AdPlacementEvent>();

    // â”€â”€ Offers & Promotions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferProgram> OfferPrograms => Set<OfferProgram>();
    public DbSet<OfferBundle> OfferBundles => Set<OfferBundle>();
    public DbSet<OfferPackageTarget> OfferPackageTargets => Set<OfferPackageTarget>();
    public DbSet<OfferRestaurant> OfferRestaurants => Set<OfferRestaurant>();
    public DbSet<OfferRedemption> OfferRedemptions => Set<OfferRedemption>();
    public DbSet<OfferAuditLog> OfferAuditLogs => Set<OfferAuditLog>();
    public DbSet<CustomerSavedOffer> CustomerSavedOffers => Set<CustomerSavedOffer>();
    public DbSet<CustomerOfferCalendarEntry> CustomerOfferCalendarEntries => Set<CustomerOfferCalendarEntry>();
    public DbSet<MealMate.Domain.ProgramsSubscriptions.OfferCheckoutQuote> OfferCheckoutQuotes => Set<MealMate.Domain.ProgramsSubscriptions.OfferCheckoutQuote>();

    // â”€â”€ Transactional Outbox & Inbox â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxProcessedEvent> InboxProcessedEvents => Set<InboxProcessedEvent>();

    // â”€â”€ M12 Admin Dashboard â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<ProfitabilityAlert> ProfitabilityAlerts => Set<ProfitabilityAlert>();
    public DbSet<AdminNotification> AdminNotifications => Set<AdminNotification>();

    // â”€â”€ Voice Calls â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public DbSet<CallSession> CallSessions => Set<CallSession>();

    public DbSet<VoiceCallDeviceSession> VoiceCallDeviceSessions => Set<VoiceCallDeviceSession>();

    public DbSet<VoiceCallBusyParticipant> VoiceCallBusyParticipants => Set<VoiceCallBusyParticipant>();

    public DbSet<VoiceCallDeviceReceipt> VoiceCallDeviceReceipts => Set<VoiceCallDeviceReceipt>();

    public DbSet<VoiceCallEventDelivery> VoiceCallEventDeliveries => Set<VoiceCallEventDelivery>();

    public DbSet<DeliveryContactCase> DeliveryContactCases => Set<DeliveryContactCase>();

    public DbSet<DeliveryContactAttempt> DeliveryContactAttempts => Set<DeliveryContactAttempt>();

    public DbSet<CustomerPhoneRevealGrant> CustomerPhoneRevealGrants => Set<CustomerPhoneRevealGrant>();

    public DbSet<FamilyCheckoutBatch> FamilyCheckoutBatches => Set<FamilyCheckoutBatch>();

    public DbSet<FamilyCheckoutPaymentAttempt> FamilyCheckoutPaymentAttempts => Set<FamilyCheckoutPaymentAttempt>();

    public DbSet<FamilyCheckoutBatchLine> FamilyCheckoutBatchLines => Set<FamilyCheckoutBatchLine>();
    public DbSet<FamilySubscriptionDeliveryAmendment> FamilySubscriptionDeliveryAmendments => Set<FamilySubscriptionDeliveryAmendment>();

    /// <inheritdoc />
    public void DetachTrackedEntity(object entity) => Entry(entity).State = EntityState.Detached;

    /// <inheritdoc />
    public async Task ReloadEntityAsync(object entity, CancellationToken cancellationToken = default) =>
        await Entry(entity).ReloadAsync(cancellationToken);

    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var stagedOutbox = StageCustomerDeliveryInvalidations();

        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch
        {
            RemoveFailedSaveInvalidations(stagedOutbox);
            throw;
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var stagedOutbox = StageCustomerDeliveryInvalidations();
        var domainEvents = CollectDomainEvents();

        try
        {
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

            if (domainEvents.Count != 0)
            {
                await _domainEventDispatcher.DispatchAsync(domainEvents, cancellationToken);
            }

            return result;
        }
        catch
        {
            RemoveFailedSaveInvalidations(stagedOutbox);
            throw;
        }
    }

    private IReadOnlyList<OutboxMessage> StageCustomerDeliveryInvalidations()
        => CustomerDeliveryChangeOutboxCapture.Stage(this, _dateTimeProvider?.UtcNow ?? DateTime.UtcNow);

    private List<IDomainEvent> CollectDomainEvents()
        => ChangeTracker
            .Entries<BaseEntity>()
            .Select(e => e.Entity)
            .SelectMany(e =>
            {
                var events = e.DomainEvents.ToList();
                e.ClearDomainEvents();
                return events;
            })
            .ToList();

    private void RemoveFailedSaveInvalidations(IEnumerable<OutboxMessage> stagedOutbox)
    {
        foreach (var message in stagedOutbox)
        {
            if (Entry(message).State == EntityState.Added)
            {
                Entry(message).State = EntityState.Detached;
            }
        }
    }

    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => new EfTransaction(await Database.BeginTransactionAsync(cancellationToken));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        builder.Entity<CustomerSavedOffer>(b =>
        {
            b.HasIndex(x => new { x.CustomerUserId, x.OfferId }).IsUnique();
            b.HasOne(x => x.Offer).WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<CustomerOfferCalendarEntry>(b =>
        {
            b.HasIndex(x => new { x.CustomerUserId, x.OfferId, x.ScheduledDate }).IsUnique();
            b.HasOne(x => x.Offer).WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ReferralAttribution>(b =>
        {
            b.Property(x => x.RewardPercentSnapshot).HasColumnType("numeric(5,2)");
        });

        ConfigureIdentityTableNames(builder);
        ApplySoftDeleteQueryFilters(builder);
    }

    private static void ConfigureIdentityTableNames(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<ApplicationRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
    }

    private static void ApplySoftDeleteQueryFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            var filter = Expression.Lambda(Expression.Not(property), parameter);

            entityType.SetQueryFilter(filter);
        }
    }
}

