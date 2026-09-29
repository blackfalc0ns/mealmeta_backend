using MealMate.Api.Common;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using MealMate.Persistence.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// ⚠️ DESTRUCTIVE — database reset utility for Super Admins only.
/// Wipes all business data and keeps admin accounts + foundation data.
/// </summary>
[Route("api/v1/admin/database")]
[Authorize(Policy = "AdminPolicy")]
public sealed class DatabaseAdminController : ApiControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServiceProvider _services;
    private readonly ILogger<DatabaseAdminController> _logger;

    public DatabaseAdminController(
        AppDbContext db,
        IServiceProvider services,
        ILogger<DatabaseAdminController> logger)
    {
        _db = db;
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// Wipes all business data (subscriptions, orders, restaurants, customers,
    /// drivers, trips …) and keeps only Admin users + roles + foundation data.
    /// Requires header  X-Confirm-Reset: WIPE-ALL-DATA
    /// </summary>
    [HttpPost("reset-keep-admins")]
    public async Task<IActionResult> ResetKeepAdmins(
        [FromHeader(Name = "X-Confirm-Reset")] string? confirmHeader,
        CancellationToken cancellationToken)
    {
        if (confirmHeader != "WIPE-ALL-DATA")
        {
            return BadRequest(new
            {
                error = "Missing or incorrect confirmation header.",
                hint = "Send header: X-Confirm-Reset: WIPE-ALL-DATA"
            });
        }

        _logger.LogWarning("⚠️ Database RESET initiated by {User} — wiping all business data",
            User.Identity?.Name ?? "unknown");

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await ExecuteRawSqlAsync("""
                -- Driver Delivery
                TRUNCATE TABLE "TripDriverAssignments","TripStops","Trips" CASCADE;

                -- Restaurant Orders / Barcodes
                TRUNCATE TABLE "MealBarcodes","RestaurantDailyOrders","PrepDocuments" CASCADE;

                -- Calendar & Meal selections
                TRUNCATE TABLE
                    "SubscriptionDayMealSnapshots","SubscriptionRestaurantDaySnapshots",
                    "SubscriptionRestaurantSnapshots","SubscriptionRestaurantPoolSnapshots",
                    "SubscriptionSnapshotMigrationIssues","MealChangeRequests",
                    "DailyMealSelections","DailyRestaurantSelections","SubscriptionDays"
                CASCADE;

                -- Subscriptions
                TRUNCATE TABLE
                    "DiscountsAndReferralLogs","SubscriptionFreezeRequests","SubscriptionRenewals",
                    "SubscriptionTierUpgrades","SubscriptionOperations","SubscriptionSnapshots",
                    "FamilyMemberPermissions","FamilyMemberAccesses","FamilyMembers",
                    "FamilyGroups","FamilySubscriptions","Subscriptions"
                CASCADE;

                -- Capacity
                TRUNCATE TABLE "CapacityHolds","DailyCapacities","BusyStatuses" CASCADE;

                -- Restaurant Profiles & Operations
                TRUNCATE TABLE
                    "RestaurantSettingsChangeRequests","RestaurantNotifications",
                    "RestaurantNotificationPreferences","RestaurantReviewHistories",
                    "RestaurantReviewItems","RestaurantRegistrationFieldReviews",
                    "DocumentVerifications","Documents","BranchStaffAssignments",
                    "StaffInvitations","RestaurantDriverLinks","DeliveryManagerRegistrations",
                    "RestaurantBranches","RestaurantCustomRoles","RestaurantRolePermissions",
                    "RestaurantServiceRegions","RestaurantOperatingSchedules","RestaurantProfiles"
                CASCADE;

                TRUNCATE TABLE "RestaurantRegistrations" CASCADE;

                -- Menu & Meals
                TRUNCATE TABLE
                    "WeekdayMealAssignments","MenuPlanAudits","MenuPlanPrograms",
                    "MenuDayMeals","MenuDays","MenuPlanVersions","MenuPlans","Menus",
                    "MealFoodPreferences","MealAllergens","MealIngredients","MealImages",
                    "MealProgramAssignments","Meals","IngredientFoodPreferences","Ingredients",
                    "PackageContentVersions","RestaurantBundlePricings",
                    "RestaurantProgramBundleOffers","ClassificationRuns",
                    "RestaurantClassifications","PricingRuleVersions"
                CASCADE;

                -- Customer Profiles
                TRUNCATE TABLE
                    "CustomerPushDeliveryLogs","CustomerAllergyPreferences",
                    "CustomerDietaryPreferences","CustomerAddresses","CustomerProfiles"
                CASCADE;

                -- Driver Profiles
                TRUNCATE TABLE
                    "DriverNotifications","DriverDeviceTokens",
                    "DriverRegistrations","DriverProfiles"
                CASCADE;

                -- Influencers
                TRUNCATE TABLE "InfluencerProfiles" CASCADE;

                -- Finance
                TRUNCATE TABLE "WalletTransactions" CASCADE;
                TRUNCATE TABLE "CustomerWallets" CASCADE;

                -- Outbox & Idempotency
                TRUNCATE TABLE "OutboxMessages","IdempotencyRecords" CASCADE;

                -- Auth sessions (keep users & roles)
                TRUNCATE TABLE "RefreshTokens","OtpChallenges","AuthLoginChallenges","AuditLogs" CASCADE;

                -- Device tokens & notifications
                TRUNCATE TABLE
                    "CustomerDeviceTokens","RestaurantDeviceTokens",
                    "AdminDeviceTokens","AdminNotifications"
                CASCADE;

                -- Promo / Referral codes
                TRUNCATE TABLE "PromoCodes","ReferralCodes" CASCADE;

                -- Admin Invitations
                TRUNCATE TABLE "AdminInvitations" CASCADE;

                -- Delete non-Admin ASP.NET Identity users (UserType = 4 → Admin)
                DELETE FROM "AspNetUserRoles"
                WHERE "UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "UserType" != 4);

                DELETE FROM "AspNetUserClaims"
                WHERE "UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "UserType" != 4);

                DELETE FROM "AspNetUserTokens"
                WHERE "UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "UserType" != 4);

                DELETE FROM "AspNetUserLogins"
                WHERE "UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "UserType" != 4);

                DELETE FROM "AspNetUsers" WHERE "UserType" != 4;
                """, cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Database reset FAILED — rolled back");
            return StatusCode(500, new { error = "Reset failed, rolled back.", detail = ex.Message });
        }

        // Re-seed admin accounts so they are present on next login
        try
        {
            await IdentityDataSeeder.SeedAsync(
                _services,
                IdentityDataSeeder.DefaultAdminPassword,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Admin re-seed after reset encountered an issue (non-fatal)");
        }

        // Count remaining admin users
        var adminCount = await _db.Users
            .Where(u => u.UserType == MealMate.Domain.Enums.UserType.Admin)
            .CountAsync(cancellationToken);

        var adminEmails = await _db.Users
            .Where(u => u.UserType == MealMate.Domain.Enums.UserType.Admin)
            .Select(u => u.Email)
            .ToListAsync(cancellationToken);

        _logger.LogWarning("✅ Database reset completed. {Count} admin user(s) remain.", adminCount);

        return Ok(new
        {
            message = "Database reset completed successfully.",
            adminUsersRemaining = adminCount,
            adminEmails
        });
    }

    private async Task ExecuteRawSqlAsync(string sql, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }
}
