using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Entities;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Influencers;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Offers;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Application-facing database surface for Identity & Access, Programs & Subscriptions, and shared Foundation tables.
/// Implemented by <c>AppDbContext</c> in Persistence.
/// </summary>
public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Country> Countries { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<AccessScope> AccessScopes { get; }
    DbSet<UserScope> UserScopes { get; }
    DbSet<AdminInvitation> AdminInvitations { get; }
    DbSet<CustomerProfile> CustomerProfiles { get; }
    DbSet<CustomerAddress> CustomerAddresses { get; }
    DbSet<RestaurantProfile> RestaurantProfiles { get; }
    DbSet<DriverProfile> DriverProfiles { get; }
    DbSet<InfluencerProfile> InfluencerProfiles { get; }
    DbSet<RestaurantRegistration> RestaurantRegistrations { get; }
    DbSet<DriverRegistration> DriverRegistrations { get; }
    DbSet<DeliveryManagerRegistration> DeliveryManagerRegistrations { get; }
    DbSet<RestaurantDriverLink> RestaurantDriverLinks { get; }
    DbSet<RestaurantBranch> RestaurantBranches { get; }
    DbSet<BranchStaffAssignment> BranchStaffAssignments { get; }
    DbSet<StaffInvitation> StaffInvitations { get; }
    DbSet<RestaurantRolePermission> RestaurantRolePermissions { get; }
    DbSet<RestaurantCustomRole> RestaurantCustomRoles { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<OtpChallenge> OtpChallenges { get; }
    DbSet<AuthLoginChallenge> AuthLoginChallenges { get; }
    DbSet<Document> Documents { get; }
    DbSet<DocumentVerification> DocumentVerifications { get; }
    DbSet<RestaurantRegistrationFieldReview> RestaurantRegistrationFieldReviews { get; }
    DbSet<RestaurantReviewItem> RestaurantReviewItems { get; }
    DbSet<RestaurantReviewHistory> RestaurantReviewHistories { get; }
    DbSet<RestaurantNotification> RestaurantNotifications { get; }
    DbSet<RestaurantNotificationPreference> RestaurantNotificationPreferences { get; }
    DbSet<CustomerDeviceToken> CustomerDeviceTokens { get; }
    DbSet<CustomerPushDeliveryLog> CustomerPushDeliveryLogs { get; }
    DbSet<AdminDeviceToken> AdminDeviceTokens { get; }
    DbSet<AdminNotification> AdminNotifications { get; }
    DbSet<RestaurantDeviceToken> RestaurantDeviceTokens { get; }
    DbSet<DriverDeviceToken> DriverDeviceTokens { get; }
    DbSet<DriverNotification> DriverNotifications { get; }
    DbSet<RestaurantSettingsChangeRequest> RestaurantSettingsChangeRequests { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<SystemSetting> SystemSettings { get; }

    // M02 Programs & Subscriptions
    DbSet<Program> Programs { get; }
    DbSet<Bundle> Bundles { get; }
    DbSet<PackageContentVersion> PackageContentVersions { get; }
    DbSet<RestaurantBundlePricing> RestaurantBundlePricings { get; }
    DbSet<RestaurantProgramBundleOffer> RestaurantProgramBundleOffers { get; }
    DbSet<ClassificationRun> ClassificationRuns { get; }
    DbSet<RestaurantClassification> RestaurantClassifications { get; }
    DbSet<PricingRuleVersion> PricingRuleVersions { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<SubscriptionSnapshot> SubscriptionSnapshots { get; }
    DbSet<SubscriptionOperation> SubscriptionOperations { get; }
    DbSet<DiscountsAndReferralLog> DiscountsAndReferralLogs { get; }
    DbSet<FamilySubscription> FamilySubscriptions { get; }
    DbSet<FamilyGroup> FamilyGroups { get; }
    DbSet<FamilyMember> FamilyMembers { get; }
    DbSet<FamilyMemberAccess> FamilyMemberAccesses { get; }
    DbSet<FamilyMemberPermissions> FamilyMemberPermissions { get; }
    DbSet<PromoCode> PromoCodes { get; }
    DbSet<ReferralCode> ReferralCodes { get; }
    DbSet<SubscriptionTierUpgrade> SubscriptionTierUpgrades { get; }
    DbSet<SubscriptionRenewal> SubscriptionRenewals { get; }
    DbSet<SubscriptionFreezeRequest> SubscriptionFreezeRequests { get; }

    // M03 Calendar & Meals
    DbSet<SubscriptionDay> SubscriptionDays { get; }
    DbSet<DailyRestaurantSelection> DailyRestaurantSelections { get; }
    DbSet<DailyMealSelection> DailyMealSelections { get; }
    DbSet<MealChangeRequest> MealChangeRequests { get; }
    DbSet<CustomerAllergyPreference> CustomerAllergyPreferences { get; }
    DbSet<CustomerDietaryPreference> CustomerDietaryPreferences { get; }
    DbSet<SubscriptionDayMealSnapshot> SubscriptionDayMealSnapshots { get; }
    DbSet<SubscriptionRestaurantPoolSnapshot> SubscriptionRestaurantPoolSnapshots { get; }
    DbSet<SubscriptionRestaurantSnapshot> SubscriptionRestaurantSnapshots { get; }
    DbSet<SubscriptionRestaurantDaySnapshot> SubscriptionRestaurantDaySnapshots { get; }
    DbSet<SubscriptionSnapshotMigrationIssue> SubscriptionSnapshotMigrationIssues { get; }

    // M04 Restaurant Meals & Ingredients
    DbSet<Menu> Menus { get; }
    DbSet<MenuPlan> MenuPlans { get; }
    DbSet<MenuPlanVersion> MenuPlanVersions { get; }
    DbSet<MenuDay> MenuDays { get; }
    DbSet<MenuDayMeal> MenuDayMeals { get; }
    DbSet<WeekdayMealAssignment> WeekdayMealAssignments { get; }
    DbSet<MenuPlanProgram> MenuPlanPrograms { get; }
    DbSet<ProgramMealSlotRequirement> ProgramMealSlotRequirements { get; }
    DbSet<ProgramNutritionStandard> ProgramNutritionStandards { get; }
    DbSet<MenuPlanAudit> MenuPlanAudits { get; }
    DbSet<MealProgramAssignment> MealProgramAssignments { get; }
    DbSet<Meal> Meals { get; }
    DbSet<MealAllergen> MealAllergens { get; }
    DbSet<Ingredient> Ingredients { get; }
    DbSet<MealIngredient> MealIngredients { get; }
    DbSet<MealImage> MealImages { get; }
    DbSet<AllergenDefinition> AllergenDefinitions { get; }
    DbSet<FoodPreferenceDefinition> FoodPreferenceDefinitions { get; }
    DbSet<IngredientFoodPreference> IngredientFoodPreferences { get; }
    DbSet<MealFoodPreference> MealFoodPreferences { get; }
    DbSet<Region> Regions { get; }
    DbSet<RestaurantServiceRegion> RestaurantServiceRegions { get; }
    DbSet<DailyCapacity> DailyCapacities { get; }
    DbSet<CapacityHold> CapacityHolds { get; }
    DbSet<BusyStatus> BusyStatuses { get; }
    DbSet<RestaurantOperatingSchedule> RestaurantOperatingSchedules { get; }
    DbSet<RestaurantOperatingDayException> RestaurantOperatingDayExceptions { get; }
    DbSet<RestaurantRating> RestaurantRatings { get; }
    DbSet<DocumentTemplate> DocumentTemplates { get; }
    DbSet<MealInvoice> MealInvoices { get; }
    DbSet<MealBarcode> MealBarcodes { get; }
    DbSet<MealLabel> MealLabels { get; }
    DbSet<RestaurantDailyOrder> RestaurantDailyOrders { get; }
    DbSet<OrderHandover> OrderHandovers { get; }

    // Payment Gateways
    DbSet<MyFatoorahGatewayConfig> MyFatoorahGatewayConfigs { get; }
    DbSet<PaymentWebhookReceipt> PaymentWebhookReceipts { get; }

    // Customer Finance
    DbSet<CustomerCashWallet> CustomerCashWallets { get; }
    DbSet<CustomerPromotionalWallet> CustomerPromotionalWallets { get; }
    DbSet<WalletTransaction> WalletTransactions { get; }
    DbSet<SubscriptionPayment> SubscriptionPayments { get; }
    DbSet<CustomerBankDetails> CustomerBankDetails { get; }

    // Complaints
    DbSet<Complaint> Complaints { get; }

    // Influencers
    DbSet<InfluencerCustomerLink> InfluencerCustomerLinks { get; }
    DbSet<InfluencerCode> InfluencerCodes { get; }
    DbSet<InfluencerCommissionCalculation> InfluencerCommissionCalculations { get; }
    DbSet<InfluencerEligibilityRule> InfluencerEligibilityRules { get; }
    DbSet<InfluencerCancellationImpact> InfluencerCancellationImpacts { get; }
    DbSet<InfluencerWithdrawalRequest> InfluencerWithdrawalRequests { get; }
    DbSet<InfluencerWithdrawalApproval> InfluencerWithdrawalApprovals { get; }
    DbSet<InfluencerPayoutHistory> InfluencerPayoutHistories { get; }

    // Delivery & Trips
    DbSet<Trip> Trips { get; }
    DbSet<TripStop> TripStops { get; }
    DbSet<DriverAssignment> DriverAssignments { get; }
    DbSet<DeliveryTrackingPoint> DeliveryTrackingPoints { get; }
    DbSet<DriverPerformanceStat> DriverPerformanceStats { get; }
    DbSet<DriverRating> DriverRatings { get; }
    DbSet<PickupScan> PickupScans { get; }
    DbSet<DeliveryConfirmationScan> DeliveryConfirmationScans { get; }
    DbSet<DriverWorkSession> DriverWorkSessions { get; }
    DbSet<PickupValidation> PickupValidations { get; }
    DbSet<PickupConditionPhoto> PickupConditionPhotos { get; }
    DbSet<OrderHandoverRevocation> OrderHandoverRevocations { get; }

    // Collaborative Campaigns
    DbSet<Campaign> Campaigns { get; }
    DbSet<CampaignDiscountSplit> CampaignDiscountSplits { get; }
    DbSet<CampaignProgram> CampaignPrograms { get; }
    DbSet<CampaignBundle> CampaignBundles { get; }
    DbSet<CampaignBundleDurationPrice> CampaignBundleDurationPrices { get; }
    DbSet<CampaignRestaurantEnrollment> CampaignRestaurantEnrollments { get; }
    DbSet<CampaignRestaurantCapacity> CampaignRestaurantCapacities { get; }
    DbSet<CampaignSubscriberCap> CampaignSubscriberCaps { get; }
    DbSet<CampaignCapacityReview> CampaignCapacityReviews { get; }
    DbSet<CampaignRestaurantApproval> CampaignRestaurantApprovals { get; }
    DbSet<CampaignAccountingEntry> CampaignAccountingEntries { get; }
    DbSet<CampaignPaymentTransaction> CampaignPaymentTransactions { get; }
    DbSet<CampaignFinancialLedger> CampaignFinancialLedgers { get; }
    DbSet<CampaignAuditLog> CampaignAuditLogs { get; }
    DbSet<CampaignTierSnapshot> CampaignTierSnapshots { get; }
    DbSet<CampaignProfitabilitySnapshot> CampaignProfitabilitySnapshots { get; }
    DbSet<CampaignCheckoutSession> CampaignCheckoutSessions { get; }
    DbSet<CampaignReservationAllocation> CampaignReservationAllocations { get; }

    // Offers & Promotions
    DbSet<Offer> Offers { get; }
    DbSet<OfferProgram> OfferPrograms { get; }
    DbSet<OfferBundle> OfferBundles { get; }
    DbSet<OfferRestaurant> OfferRestaurants { get; }
    DbSet<OfferRedemption> OfferRedemptions { get; }
    DbSet<OfferAuditLog> OfferAuditLogs { get; }

    // Transactional Outbox & Inbox
    DbSet<MealMate.Domain.Common.OutboxMessage> OutboxMessages { get; }
    DbSet<MealMate.Domain.Common.InboxProcessedEvent> InboxProcessedEvents { get; }

    // Voice Calls
    DbSet<MealMate.Domain.VoiceCalls.CallSession> CallSessions { get; }

    DbSet<TEntity> Set<TEntity>() where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
