-- ============================================================================
-- MealMate — Restaurant Service Capacity Reconciliation (DRY-RUN, READ-ONLY)
-- Plan task 1 (docs/superpowers/plans/2026-10-03-restaurant-service-availability-plan.md)
--
-- Purpose: inventory the real capacity data per restaurant/date, reconcile the
-- three live accounting formulas (booking eligibility / capacity transfer /
-- admin reassignment), and expose every unclassified or inconsistent source
-- BEFORE any unified capacity reader or period partitioning is enabled.
--
-- Guarantees: every statement is a SELECT (no INSERT/UPDATE/DELETE, no locks
-- beyond brief shared reads, no data correction). Safe to run repeatedly.
--
-- Run (psql):
--   psql "<connection>" -v from_date=2026-10-03 -v to_date=2026-11-02 \
--        -f scripts/restaurant-service-capacity-dry-run.sql
--
-- Enum literals used (src/MealMate.Domain/Enums/Enums.cs):
--   CapacityHoldStatus      1=PendingPayment 2=Confirmed 3=Expired 4=Released 5=PaymentConfirmed
--   SubscriptionDayStatus   1=Open 2=AutoSelected 3=CustomerSelected 4=Locked72h 5=SentToRestaurant
--                           6=ConfirmedByRestaurant 7=ReplacementRequired 8=ReplacementWindowOpen
--                           9=Preparing 10=OutForDelivery 11=Delivered 12=Frozen 13=Cancelled 14=Failed
--   SubscriptionStatus      1=Draft 2=PendingPayment 3=Paid 4=Scheduled 5=Active 6=Frozen 7=Completed
--                           8=Cancelled 9=CancellationPending 10=Upgraded 11=Expired 12=SuspendedForReview 13=PaymentFailed
--   CapacityType            1=Base 2=Campaign 3=DateOverride
--
-- All identifiers are quoted PascalCase; enum columns store ints.
-- ============================================================================

-- ---------------------------------------------------------------------------
-- S1. Per restaurant/date: the three live accounting formulas side by side.
--     eligibility_load = non-cancelled days + live PendingPayment/PaymentConfirmed holds
--     transfer_load    = live PendingPayment + PaymentConfirmed + Confirmed holds (days ignored)
--     admin_load       = non-cancelled days + live PendingPayment/PaymentConfirmed holds
--     (eligibility and admin use the same formula today; listed separately so a
--      future unified reader can be diffed against both.)
-- ---------------------------------------------------------------------------
WITH params AS (
    SELECT :'from_date'::date AS from_date, :'to_date'::date AS to_date
),
day_load AS (
    SELECT sd."RestaurantId" AS restaurant_id, sd."ServiceDate" AS service_date,
           COUNT(*) AS days_count
    FROM "SubscriptionDays" sd
    CROSS JOIN params p
    WHERE sd."RestaurantId" IS NOT NULL
      AND sd."Status" <> 13            -- <> Cancelled
      AND sd."IsDeleted" = false
      AND sd."ServiceDate" BETWEEN p.from_date AND p.to_date
    GROUP BY 1, 2
),
hold_load AS (
    SELECT h."RestaurantId" AS restaurant_id, h."ServiceDate" AS service_date,
           SUM(h."Quantity") FILTER (WHERE h."Status" = 1 AND h."ExpiresAtUtc" > NOW() AT TIME ZONE 'UTC') AS pending_live_qty,
           SUM(h."Quantity") FILTER (WHERE h."Status" = 5) AS payment_confirmed_qty,
           SUM(h."Quantity") FILTER (WHERE h."Status" = 2) AS confirmed_qty
    FROM "CapacityHolds" h
    CROSS JOIN params p
    WHERE h."ServiceDate" BETWEEN p.from_date AND p.to_date
      AND h."IsDeleted" = false
    GROUP BY 1, 2
),
effective_limit AS (
    SELECT x.restaurant_id, x.service_date,
           COALESCE(
               (SELECT dc."MaxBoxes" FROM "DailyCapacities" dc
                WHERE dc."RestaurantId" = x.restaurant_id AND dc."ServiceDate" = x.service_date
                  AND dc."CapacityType" = 3 AND dc."IsDeleted" = false LIMIT 1),
               (SELECT dc."MaxBoxes" FROM "DailyCapacities" dc
                WHERE dc."RestaurantId" = x.restaurant_id AND dc."CapacityType" = 1
                  AND dc."ServiceDate" <= x.service_date AND dc."IsDeleted" = false
                ORDER BY dc."ServiceDate" DESC LIMIT 1)
           ) AS limit_boxes
    FROM (SELECT DISTINCT restaurant_id, service_date FROM day_load
          UNION SELECT DISTINCT restaurant_id, service_date FROM hold_load) x
)
SELECT el.restaurant_id,
       el.service_date,
       el.limit_boxes,
       COALESCE(dl.days_count, 0) AS days_count,
       COALESCE(hl.pending_live_qty, 0) AS pending_live_qty,
       COALESCE(hl.payment_confirmed_qty, 0) AS payment_confirmed_qty,
       COALESCE(hl.confirmed_qty, 0) AS confirmed_qty,
       -- Formula 1: booking eligibility (RestaurantEligibilityService)
       COALESCE(dl.days_count, 0) + COALESCE(hl.pending_live_qty, 0) + COALESCE(hl.payment_confirmed_qty, 0) AS eligibility_load,
       -- Formula 2: calendar transfer (CapacityTransferService)
       COALESCE(hl.pending_live_qty, 0) + COALESCE(hl.payment_confirmed_qty, 0) + COALESCE(hl.confirmed_qty, 0) AS transfer_load,
       -- Formula 3: admin reassignment (AdminCheckoutOperationsService)
       COALESCE(dl.days_count, 0) + COALESCE(hl.pending_live_qty, 0) + COALESCE(hl.payment_confirmed_qty, 0) AS admin_load,
       el.limit_boxes IS NULL AS limit_unknown,
       (COALESCE(dl.days_count, 0) + COALESCE(hl.pending_live_qty, 0) + COALESCE(hl.confirmed_qty, 0))
       - (COALESCE(hl.pending_live_qty, 0) + COALESCE(hl.payment_confirmed_qty, 0) + COALESCE(hl.confirmed_qty, 0)) AS eligibility_minus_transfer
FROM effective_limit el
LEFT JOIN day_load   dl ON dl.restaurant_id = el.restaurant_id AND dl.service_date = el.service_date
LEFT JOIN hold_load  hl ON hl.restaurant_id = el.restaurant_id AND hl.service_date = el.service_date
ORDER BY el.restaurant_id, el.service_date;

-- ---------------------------------------------------------------------------
-- S2. Ghost holds — Confirmed holds that keep consuming capacity without a
--     matching live commitment. (Cancellation releases PendingPayment only;
--     freeze nulls the day's RestaurantId but keeps the hold.)
-- ---------------------------------------------------------------------------
-- S2a. Confirmed holds on cancelled / superseded subscriptions.
SELECT h."RestaurantId", h."ServiceDate", COUNT(*) AS ghost_holds, SUM(h."Quantity") AS ghost_qty,
       STRING_AGG(DISTINCT s."Status"::text, ',') AS subscription_statuses
FROM "CapacityHolds" h
JOIN "Subscriptions" s ON s."Id" = h."SubscriptionId"
WHERE h."Status" = 2                          -- Confirmed
  AND h."IsDeleted" = false
  AND s."Status" IN (8, 9, 10, 13)            -- Cancelled, CancellationPending, Upgraded, PaymentFailed
GROUP BY 1, 2
ORDER BY 1, 2;

-- S2b. Confirmed holds whose day moved to another restaurant (or was detached by freeze).
SELECT h."RestaurantId" AS hold_restaurant_id, h."ServiceDate",
       sd."RestaurantId" AS day_restaurant_id, sd."Status" AS day_status,
       COUNT(*) AS mismatched_holds
FROM "CapacityHolds" h
JOIN "SubscriptionDays" sd ON sd."SubscriptionId" = h."SubscriptionId" AND sd."ServiceDate" = h."ServiceDate"
WHERE h."Status" = 2 AND h."IsDeleted" = false
  AND sd."RestaurantId" IS DISTINCT FROM h."RestaurantId"
GROUP BY 1, 2, 3, 4
ORDER BY 1, 2;

-- S2c. Confirmed holds with no day row at all for the same (subscription, date).
SELECT h."RestaurantId", h."ServiceDate", COUNT(*) AS holds_without_day
FROM "CapacityHolds" h
LEFT JOIN "SubscriptionDays" sd ON sd."SubscriptionId" = h."SubscriptionId" AND sd."ServiceDate" = h."ServiceDate"
WHERE h."Status" = 2 AND h."IsDeleted" = false AND sd."Id" IS NULL
GROUP BY 1, 2
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------
-- S3. Committed days with NO active hold (hold-less load).
--     Invisible to CapacityTransferService accounting. Includes auto-selected
--     days (routine at 72h lock), campaign-purchased days, and admin
--     reassignments that found no existing hold.
-- ---------------------------------------------------------------------------
SELECT sd."RestaurantId", sd."ServiceDate", sd."Status" AS day_status, COUNT(*) AS holdless_days
FROM "SubscriptionDays" sd
LEFT JOIN "CapacityHolds" h
       ON h."SubscriptionId" = sd."SubscriptionId" AND h."ServiceDate" = sd."ServiceDate"
      AND h."IsDeleted" = false
      AND (h."Status" = 2 OR h."Status" = 5 OR (h."Status" = 1 AND h."ExpiresAtUtc" > NOW() AT TIME ZONE 'UTC'))
WHERE sd."RestaurantId" IS NOT NULL
  AND sd."Status" <> 13                        -- <> Cancelled
  AND sd."IsDeleted" = false
  AND h."Id" IS NULL
GROUP BY 1, 2, 3
ORDER BY 1, 2, 3;

-- ---------------------------------------------------------------------------
-- S4. Identity audit — more than one committed day or hold row per
--     (SubscriptionId, ServiceDate). Expect 0 rows (unique indexes); any row
--     here is a blocker for the unified commitment identity.
-- ---------------------------------------------------------------------------
SELECT 'days' AS relation, sd."SubscriptionId", sd."ServiceDate", COUNT(*) AS row_count
FROM "SubscriptionDays" sd
GROUP BY 1, 2, 3
HAVING COUNT(*) > 1
UNION ALL
SELECT 'holds', h."SubscriptionId", h."ServiceDate", COUNT(*)
FROM "CapacityHolds" h
GROUP BY 1, 2, 3
HAVING COUNT(*) > 1;

-- ---------------------------------------------------------------------------
-- S5. Payment review backlog — SuspendedForReview subscriptions with stuck
--     PendingPayment holds (never released by the expiry sweeper).
-- ---------------------------------------------------------------------------
SELECT s."Id" AS subscription_id, s."Status", s."LastOperationAtUtc",
       COUNT(h."Id") AS stuck_holds, MIN(h."ExpiresAtUtc") AS oldest_expiry
FROM "Subscriptions" s
JOIN "CapacityHolds" h ON h."SubscriptionId" = s."Id" AND h."Status" = 1 AND h."IsDeleted" = false
WHERE s."Status" = 12                            -- SuspendedForReview
GROUP BY 1, 2, 3
ORDER BY oldest_expiry;

-- ---------------------------------------------------------------------------
-- S6. Campaign capacity reconciliation.
--     S6a: legacy/unused CapacityType=2 rows on DailyCapacities.
--     S6b: CampaignRestaurantCapacities.ReservedBoxes vs actual allocation sums.
--     S6c: allocation dates with no campaign capacity row (sales for those
--          dates cannot be reconciled without creating rows via an explicit
--          command — see gap G39).
--     S6d: dedicated daily capacity exceeding the restaurant's own base limit.
-- ---------------------------------------------------------------------------
SELECT dc."RestaurantId", dc."ServiceDate", dc."MaxBoxes", dc."ReservedBoxes"
FROM "DailyCapacities" dc
WHERE dc."CapacityType" = 2;                     -- Campaign rows on the general table

-- S6b: CampaignRestaurantCapacities.ReservedBoxes vs actual allocation sums.
SELECT c."CampaignId", c."RestaurantId", c."ServiceDate", c."MaxBoxes", c."ReservedBoxes",
       COALESCE(a.allocated, 0) AS allocated_units,
       c."ReservedBoxes" - COALESCE(a.allocated, 0) AS reserved_minus_allocated
FROM "CampaignRestaurantCapacities" c
LEFT JOIN (
    SELECT "CampaignId", "RestaurantId", "ServiceDate", SUM("Units") AS allocated
    FROM "CampaignReservationAllocations"
    GROUP BY 1, 2, 3
) a ON a."CampaignId" = c."CampaignId" AND a."RestaurantId" = c."RestaurantId" AND a."ServiceDate" = c."ServiceDate"
ORDER BY c."CampaignId", c."RestaurantId", c."ServiceDate";

-- S6c: allocation dates with no campaign capacity row (gap G39).
SELECT a."CampaignId", a."RestaurantId", a."ServiceDate", SUM(a."Units") AS allocated_units
FROM "CampaignReservationAllocations" a
LEFT JOIN "CampaignRestaurantCapacities" c
       ON c."CampaignId" = a."CampaignId" AND c."RestaurantId" = a."RestaurantId" AND c."ServiceDate" = a."ServiceDate"
WHERE c."Id" IS NULL
GROUP BY 1, 2, 3
ORDER BY 1, 2, 3;

-- S6d: dedicated daily capacity exceeding the restaurant's own base limit.
SELECT e."CampaignId", e."RestaurantId", e."DedicatedDailyCapacity",
       (SELECT dc."MaxBoxes" FROM "DailyCapacities" dc
        WHERE dc."RestaurantId" = e."RestaurantId" AND dc."CapacityType" = 1 AND dc."IsDeleted" = false
        ORDER BY dc."ServiceDate" DESC LIMIT 1) AS latest_base_limit
FROM "CampaignRestaurantEnrollments" e
WHERE e."DedicatedDailyCapacity" IS NOT NULL
  AND e."DedicatedDailyCapacity" > COALESCE((
      SELECT dc."MaxBoxes" FROM "DailyCapacities" dc
      WHERE dc."RestaurantId" = e."RestaurantId" AND dc."CapacityType" = 1 AND dc."IsDeleted" = false
      ORDER BY dc."ServiceDate" DESC LIMIT 1), 0)
ORDER BY e."CampaignId", e."RestaurantId";

-- ---------------------------------------------------------------------------
-- S7. Restaurants with NO Base capacity row at all — their limit is Unknown,
--     yet the overview fabricates a default of 40 for them
--     (RestaurantCapacityHandlers.GetCurrentBaseCapacityAsync).
-- ---------------------------------------------------------------------------
SELECT r."Id" AS restaurant_id, r."TradeNameEn"
FROM "RestaurantProfiles" r
LEFT JOIN "DailyCapacities" dc ON dc."RestaurantId" = r."Id" AND dc."CapacityType" = 1 AND dc."IsDeleted" = false
WHERE r."IsDeleted" = false AND r."IsArchived" = false AND dc."Id" IS NULL
ORDER BY 2;

-- ---------------------------------------------------------------------------
-- S8. Vestigial counter audit — DailyCapacities.ReservedBoxes is only written
--     by manual date-override edits and read by no business logic. Non-zero
--     values here are display residue, not live reservations.
-- ---------------------------------------------------------------------------
SELECT dc."RestaurantId", dc."ServiceDate", dc."CapacityType", dc."MaxBoxes", dc."ReservedBoxes"
FROM "DailyCapacities" dc
WHERE dc."ReservedBoxes" <> 0
ORDER BY dc."RestaurantId", dc."ServiceDate";
