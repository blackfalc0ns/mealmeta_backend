using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Entities;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
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
    DbSet<RestaurantProfile> RestaurantProfiles { get; }
    DbSet<DriverProfile> DriverProfiles { get; }
    DbSet<InfluencerProfile> InfluencerProfiles { get; }
    DbSet<RestaurantRegistration> RestaurantRegistrations { get; }
    DbSet<DriverRegistration> DriverRegistrations { get; }
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
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<SystemSetting> SystemSettings { get; }

    // M02 Programs & Subscriptions
    DbSet<Program> Programs { get; }
    DbSet<Bundle> Bundles { get; }
    DbSet<PackageContentVersion> PackageContentVersions { get; }
    DbSet<RestaurantBundlePricing> RestaurantBundlePricings { get; }
    DbSet<ClassificationRun> ClassificationRuns { get; }
    DbSet<RestaurantClassification> RestaurantClassifications { get; }
    DbSet<PricingRuleVersion> PricingRuleVersions { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<SubscriptionSnapshot> SubscriptionSnapshots { get; }
    DbSet<SubscriptionOperation> SubscriptionOperations { get; }
    DbSet<DiscountsAndReferralLog> DiscountsAndReferralLogs { get; }
    DbSet<FamilySubscription> FamilySubscriptions { get; }
    DbSet<PromoCode> PromoCodes { get; }
    DbSet<ReferralCode> ReferralCodes { get; }
    DbSet<SubscriptionTierUpgrade> SubscriptionTierUpgrades { get; }
    DbSet<SubscriptionRenewal> SubscriptionRenewals { get; }
    DbSet<SubscriptionFreezeRequest> SubscriptionFreezeRequests { get; }

    // Payment Gateways
    DbSet<MyFatoorahGatewayConfig> MyFatoorahGatewayConfigs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
