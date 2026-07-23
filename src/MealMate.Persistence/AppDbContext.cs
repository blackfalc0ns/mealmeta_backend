using System.Linq.Expressions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Common;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Influencers;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantFinance;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence.Common;
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
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IUnitOfWork
{
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IDomainEventDispatcher domainEventDispatcher)
        : base(options)
        => _domainEventDispatcher = domainEventDispatcher;

    // ── Phase 0 Foundation ──────────────────────────────────────────
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<RuleVersion> RuleVersions => Set<RuleVersion>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVerification> DocumentVerifications => Set<DocumentVerification>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    // ── M01 Identity & Access ───────────────────────────────────────
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AccessScope> AccessScopes => Set<AccessScope>();
    public DbSet<UserScope> UserScopes => Set<UserScope>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<RestaurantProfile> RestaurantProfiles => Set<RestaurantProfile>();
    public DbSet<DriverProfile> DriverProfiles => Set<DriverProfile>();
    public DbSet<InfluencerProfile> InfluencerProfiles => Set<InfluencerProfile>();
    public DbSet<RestaurantRegistration> RestaurantRegistrations => Set<RestaurantRegistration>();
    public DbSet<DriverRegistration> DriverRegistrations => Set<DriverRegistration>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // ── M02 Programs & Subscriptions ────────────────────────────────
    public DbSet<Program> Programs => Set<Program>();
    public DbSet<Bundle> Bundles => Set<Bundle>();
    public DbSet<RestaurantBundlePricing> RestaurantBundlePricings => Set<RestaurantBundlePricing>();
    public DbSet<ClassificationRun> ClassificationRuns => Set<ClassificationRun>();
    public DbSet<RestaurantClassification> RestaurantClassifications => Set<RestaurantClassification>();
    public DbSet<PricingRuleVersion> PricingRuleVersions => Set<PricingRuleVersion>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionSnapshot> SubscriptionSnapshots => Set<SubscriptionSnapshot>();
    public DbSet<FamilySubscription> FamilySubscriptions => Set<FamilySubscription>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<ReferralCode> ReferralCodes => Set<ReferralCode>();
    public DbSet<SubscriptionTierUpgrade> SubscriptionTierUpgrades => Set<SubscriptionTierUpgrade>();
    public DbSet<SubscriptionRenewal> SubscriptionRenewals => Set<SubscriptionRenewal>();

    // ── M03 Calendar & Meals ────────────────────────────────────────
    public DbSet<SubscriptionCalendar> SubscriptionCalendars => Set<SubscriptionCalendar>();
    public DbSet<SubscriptionDay> SubscriptionDays => Set<SubscriptionDay>();
    public DbSet<DailyRestaurantSelection> DailyRestaurantSelections => Set<DailyRestaurantSelection>();
    public DbSet<DailyMealSelection> DailyMealSelections => Set<DailyMealSelection>();
    public DbSet<CustomerAllergyPreference> CustomerAllergyPreferences => Set<CustomerAllergyPreference>();
    public DbSet<MealChangeRequest> MealChangeRequests => Set<MealChangeRequest>();
    public DbSet<SubscriptionFreeze> SubscriptionFreezes => Set<SubscriptionFreeze>();
    public DbSet<FreezeLedgerEntry> FreezeLedgerEntries => Set<FreezeLedgerEntry>();
    public DbSet<DiversificationLimitSnapshot> DiversificationLimitSnapshots => Set<DiversificationLimitSnapshot>();

    // ── M04 Restaurant Operations ───────────────────────────────────
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<MealAllergen> MealAllergens => Set<MealAllergen>();
    public DbSet<RestaurantServiceRegion> RestaurantServiceRegions => Set<RestaurantServiceRegion>();
    public DbSet<DailyCapacity> DailyCapacities => Set<DailyCapacity>();
    public DbSet<BusyStatus> BusyStatuses => Set<BusyStatus>();
    public DbSet<RestaurantDailyOrder> RestaurantDailyOrders => Set<RestaurantDailyOrder>();
    public DbSet<RestaurantConfirmation> RestaurantConfirmations => Set<RestaurantConfirmation>();
    public DbSet<MealInvoice> MealInvoices => Set<MealInvoice>();
    public DbSet<MealBarcode> MealBarcodes => Set<MealBarcode>();
    public DbSet<MealLabel> MealLabels => Set<MealLabel>();
    public DbSet<OrderHandover> OrderHandovers => Set<OrderHandover>();
    public DbSet<RestaurantDriverLink> RestaurantDriverLinks => Set<RestaurantDriverLink>();
    public DbSet<RestaurantRating> RestaurantRatings => Set<RestaurantRating>();
    public DbSet<MealRating> MealRatings => Set<MealRating>();
    public DbSet<RestaurantContractTermination> RestaurantContractTerminations => Set<RestaurantContractTermination>();

    // ── M05 Driver & Delivery ───────────────────────────────────────
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<DriverAssignment> DriverAssignments => Set<DriverAssignment>();
    public DbSet<PickupScan> PickupScans => Set<PickupScan>();
    public DbSet<DeliveryTrackingPoint> DeliveryTrackingPoints => Set<DeliveryTrackingPoint>();
    public DbSet<SafeContactLog> SafeContactLogs => Set<SafeContactLog>();
    public DbSet<DeliveryHold> DeliveryHolds => Set<DeliveryHold>();
    public DbSet<SecondDeliveryAttempt> SecondDeliveryAttempts => Set<SecondDeliveryAttempt>();
    public DbSet<DeliveryConfirmationScan> DeliveryConfirmationScans => Set<DeliveryConfirmationScan>();
    public DbSet<DriverRating> DriverRatings => Set<DriverRating>();
    public DbSet<DriverPerformanceStat> DriverPerformanceStats => Set<DriverPerformanceStat>();

    // ── M06 Complaints & Support ────────────────────────────────────
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

    // ── M07 Central Accounting ──────────────────────────────────────
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<FinancialSetting> FinancialSettings => Set<FinancialSetting>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<DeferredRevenue> DeferredRevenues => Set<DeferredRevenue>();
    public DbSet<DailyRevenueRecognition> DailyRevenueRecognitions => Set<DailyRevenueRecognition>();
    public DbSet<RestaurantPayableLedger> RestaurantPayableLedgers => Set<RestaurantPayableLedger>();
    public DbSet<InfluencerPayableLedger> InfluencerPayableLedgers => Set<InfluencerPayableLedger>();
    public DbSet<PaymentGatewayConfig> PaymentGatewayConfigs => Set<PaymentGatewayConfig>();
    public DbSet<TaxRecord> TaxRecords => Set<TaxRecord>();
    public DbSet<OperatingExpense> OperatingExpenses => Set<OperatingExpense>();
    public DbSet<FinancialSettlement> FinancialSettlements => Set<FinancialSettlement>();
    public DbSet<FinancialAuditTrail> FinancialAuditTrails => Set<FinancialAuditTrail>();

    // ── M08 Customer Finance ────────────────────────────────────────
    public DbSet<CustomerCashWallet> CustomerCashWallets => Set<CustomerCashWallet>();
    public DbSet<CustomerPromotionalWallet> CustomerPromotionalWallets => Set<CustomerPromotionalWallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
    public DbSet<SubscriptionCancellation> SubscriptionCancellations => Set<SubscriptionCancellation>();
    public DbSet<RefundCalculation> RefundCalculations => Set<RefundCalculation>();
    public DbSet<CancellationFeeRecord> CancellationFeeRecords => Set<CancellationFeeRecord>();
    public DbSet<CustomerWithdrawalRequest> CustomerWithdrawalRequests => Set<CustomerWithdrawalRequest>();
    public DbSet<CustomerBankDetails> CustomerBankDetails => Set<CustomerBankDetails>();

    // ── M09 Restaurant Finance ──────────────────────────────────────
    public DbSet<MealPayable> MealPayables => Set<MealPayable>();
    public DbSet<RestaurantCommissionRecord> RestaurantCommissionRecords => Set<RestaurantCommissionRecord>();
    public DbSet<ComplaintDeductionRecord> ComplaintDeductionRecords => Set<ComplaintDeductionRecord>();
    public DbSet<RestaurantMonthlyInvoice> RestaurantMonthlyInvoices => Set<RestaurantMonthlyInvoice>();
    public DbSet<RestaurantPartialPayment> RestaurantPartialPayments => Set<RestaurantPartialPayment>();
    public DbSet<RestaurantPayout> RestaurantPayouts => Set<RestaurantPayout>();
    public DbSet<TransferReceipt> TransferReceipts => Set<TransferReceipt>();
    public DbSet<RestaurantAccountStatement> RestaurantAccountStatements => Set<RestaurantAccountStatement>();

    // ── M10 Influencers ─────────────────────────────────────────────
    public DbSet<InfluencerCustomerLink> InfluencerCustomerLinks => Set<InfluencerCustomerLink>();
    public DbSet<InfluencerCode> InfluencerCodes => Set<InfluencerCode>();
    public DbSet<InfluencerCommissionCalculation> InfluencerCommissionCalculations => Set<InfluencerCommissionCalculation>();
    public DbSet<InfluencerEligibilityRule> InfluencerEligibilityRules => Set<InfluencerEligibilityRule>();
    public DbSet<InfluencerCancellationImpact> InfluencerCancellationImpacts => Set<InfluencerCancellationImpact>();
    public DbSet<InfluencerWithdrawalRequest> InfluencerWithdrawalRequests => Set<InfluencerWithdrawalRequest>();
    public DbSet<InfluencerWithdrawalApproval> InfluencerWithdrawalApprovals => Set<InfluencerWithdrawalApproval>();
    public DbSet<InfluencerPayoutHistory> InfluencerPayoutHistories => Set<InfluencerPayoutHistory>();

    // ── M11 Collaborative Campaigns ─────────────────────────────────
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignDiscountSplit> CampaignDiscountSplits => Set<CampaignDiscountSplit>();
    public DbSet<CampaignProgram> CampaignPrograms => Set<CampaignProgram>();
    public DbSet<CampaignBundle> CampaignBundles => Set<CampaignBundle>();
    public DbSet<CampaignRestaurantEnrollment> CampaignRestaurantEnrollments => Set<CampaignRestaurantEnrollment>();
    public DbSet<CampaignRestaurantCapacity> CampaignRestaurantCapacities => Set<CampaignRestaurantCapacity>();
    public DbSet<CampaignSubscriberCap> CampaignSubscriberCaps => Set<CampaignSubscriberCap>();
    public DbSet<CampaignCapacityReview> CampaignCapacityReviews => Set<CampaignCapacityReview>();
    public DbSet<CampaignRestaurantApproval> CampaignRestaurantApprovals => Set<CampaignRestaurantApproval>();
    public DbSet<CampaignAccountingEntry> CampaignAccountingEntries => Set<CampaignAccountingEntry>();

    // ── M12 Admin Dashboard ─────────────────────────────────────────
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<ProfitabilityAlert> ProfitabilityAlerts => Set<ProfitabilityAlert>();
    public DbSet<AdminNotification> AdminNotifications => Set<AdminNotification>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var domainEvents = ChangeTracker
            .Entries<BaseEntity>()
            .Select(e => e.Entity)
            .SelectMany(e =>
            {
                var events = e.DomainEvents.ToList();
                e.ClearDomainEvents();
                return events;
            })
            .ToList();

        var result = await base.SaveChangesAsync(cancellationToken);

        if (domainEvents.Count != 0)
        {
            await _domainEventDispatcher.DispatchAsync(domainEvents, cancellationToken);
        }

        return result;
    }

    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => new EfTransaction(await Database.BeginTransactionAsync(cancellationToken));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

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
