-- =============================================================
--  MealMate — Production Database Reset
--  Deletes ALL business data, keeps only Admin users + Roles +
--  Foundation data (Countries, Tenants, Regions, Programs, Bundles).
--  The Seeder will re-create Admin accounts on next startup.
-- =============================================================
--  ⚠️  DESTRUCTIVE — run on staging/production only after backup.
--  Run inside a transaction so you can ROLLBACK if needed.
-- =============================================================

BEGIN;

-- ── 1. Driver Delivery ──────────────────────────────────────
TRUNCATE TABLE
    "TripDriverAssignments",
    "TripStops",
    "Trips"
CASCADE;

-- ── 2. Restaurant Orders / Barcodes / Labels ─────────────────
TRUNCATE TABLE
    "MealBarcodes",
    "RestaurantDailyOrders",
    "PrepDocuments"
CASCADE;

-- ── 3. Calendar & Meals ──────────────────────────────────────
TRUNCATE TABLE
    "SubscriptionDayMealSnapshots",
    "SubscriptionRestaurantDaySnapshots",
    "SubscriptionRestaurantSnapshots",
    "SubscriptionRestaurantPoolSnapshots",
    "SubscriptionSnapshotMigrationIssues",
    "MealChangeRequests",
    "DailyMealSelections",
    "DailyRestaurantSelections",
    "SubscriptionDays"
CASCADE;

-- ── 4. Subscriptions ─────────────────────────────────────────
TRUNCATE TABLE
    "DiscountsAndReferralLogs",
    "SubscriptionFreezeRequests",
    "SubscriptionRenewals",
    "SubscriptionTierUpgrades",
    "SubscriptionOperations",
    "SubscriptionSnapshots",
    "FamilyMemberPermissions",
    "FamilyMemberAccesses",
    "FamilyMembers",
    "FamilyGroups",
    "FamilySubscriptions",
    "Subscriptions"
CASCADE;

-- ── 5. Capacity ───────────────────────────────────────────────
TRUNCATE TABLE
    "CapacityHolds",
    "DailyCapacities",
    "BusyStatuses"
CASCADE;

-- ── 6. Restaurant Operations ─────────────────────────────────
TRUNCATE TABLE
    "RestaurantSettingsChangeRequests",
    "RestaurantNotifications",
    "RestaurantNotificationPreferences",
    "RestaurantReviewHistories",
    "RestaurantReviewItems",
    "RestaurantRegistrationFieldReviews",
    "DocumentVerifications",
    "Documents",
    "BranchStaffAssignments",
    "StaffInvitations",
    "RestaurantDriverLinks",
    "DeliveryManagerRegistrations",
    "RestaurantBranches",
    "RestaurantCustomRoles",
    "RestaurantRolePermissions",
    "RestaurantServiceRegions",
    "RestaurantOperatingSchedules",
    "RestaurantProfiles"
CASCADE;

-- ── 7. Restaurant Registrations ──────────────────────────────
TRUNCATE TABLE
    "RestaurantRegistrations"
CASCADE;

-- ── 8. Menu & Meals ──────────────────────────────────────────
TRUNCATE TABLE
    "WeekdayMealAssignments",
    "MenuPlanAudits",
    "MenuPlanPrograms",
    "MenuDayMeals",
    "MenuDays",
    "MenuPlanVersions",
    "MenuPlans",
    "Menus",
    "MealFoodPreferences",
    "MealAllergens",
    "MealIngredients",
    "MealImages",
    "MealProgramAssignments",
    "Meals",
    "IngredientFoodPreferences",
    "Ingredients",
    "PackageContentVersions",
    "RestaurantBundlePricings",
    "RestaurantProgramBundleOffers",
    "ClassificationRuns",
    "RestaurantClassifications",
    "PricingRuleVersions"
CASCADE;

-- ── 9. Customer Profiles & Finance ───────────────────────────
TRUNCATE TABLE
    "CustomerPushDeliveryLogs",
    "CustomerAllergyPreferences",
    "CustomerDietaryPreferences",
    "CustomerAddresses",
    "CustomerProfiles"
CASCADE;

-- ── 10. Driver Profiles & Registrations ──────────────────────
TRUNCATE TABLE
    "DriverNotifications",
    "DriverDeviceTokens",
    "DriverRegistrations",
    "DriverProfiles"
CASCADE;

-- ── 11. Influencers ───────────────────────────────────────────
TRUNCATE TABLE
    "InfluencerProfiles"
CASCADE;

-- ── 12. Campaigns & Offers ────────────────────────────────────
TRUNCATE TABLE
    "CampaignParticipants"
CASCADE;

TRUNCATE TABLE
    "Campaigns"
CASCADE;

TRUNCATE TABLE
    "Offers"
CASCADE;

-- ── 13. Finance ───────────────────────────────────────────────
TRUNCATE TABLE
    "WalletTransactions"
CASCADE;

TRUNCATE TABLE
    "CustomerWallets"
CASCADE;

-- ── 14. Complaints / Support ─────────────────────────────────
TRUNCATE TABLE
    "ComplaintMessages",
    "Complaints"
CASCADE;

-- ── 15. Outbox & Idempotency ──────────────────────────────────
TRUNCATE TABLE
    "OutboxMessages",
    "IdempotencyRecords"
CASCADE;

-- ── 16. Auth sessions (keep roles & users) ───────────────────
TRUNCATE TABLE
    "RefreshTokens",
    "OtpChallenges",
    "AuthLoginChallenges",
    "AuditLogs"
CASCADE;

-- ── 17. Notifications (device tokens for non-admin users) ────
TRUNCATE TABLE
    "CustomerDeviceTokens",
    "RestaurantDeviceTokens",
    "AdminDeviceTokens",
    "AdminNotifications"
CASCADE;

-- ── 18. Promo / Referral codes ────────────────────────────────
TRUNCATE TABLE
    "PromoCodes",
    "ReferralCodes"
CASCADE;

-- ── 19. Admin Invitations ─────────────────────────────────────
TRUNCATE TABLE
    "AdminInvitations"
CASCADE;

-- ── 20. Delete non-Admin ASP.NET Identity users ───────────────
-- Remove user-role links for non-admin users first
DELETE FROM "AspNetUserRoles"
WHERE "UserId" IN (
    SELECT "Id" FROM "AspNetUsers"
    WHERE "UserType" != 4  -- 4 = UserType.Admin
);

-- Remove claims, tokens, logins for non-admin users
DELETE FROM "AspNetUserClaims"  WHERE "UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "UserType" != 4);
DELETE FROM "AspNetUserTokens"  WHERE "UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "UserType" != 4);
DELETE FROM "AspNetUserLogins"  WHERE "UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "UserType" != 4);

-- Finally delete non-admin users
DELETE FROM "AspNetUsers" WHERE "UserType" != 4;

-- Verify what remains
SELECT "Email", "UserType", "AccountStatus" FROM "AspNetUsers";

COMMIT;
-- To undo everything above, replace COMMIT with ROLLBACK before running.
