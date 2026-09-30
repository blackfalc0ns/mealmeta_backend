# MealMate Restaurant Settings & Admin Review Operator Runbook

## 1. Overview and Core Invariant
This runbook guides operators, site reliability engineers (SRE), and platform administrators in maintaining, migrating, and supporting the Restaurant Settings Review System in MealMate.

### Non-Negotiable Core Policy
Every permanent mutation to an approved restaurant's:
1. Legal / Business Identity (`Profile`)
2. Bank Details & IBAN (`Profile`)
3. Primary Location Coordinates & Address (`Coverage`)
4. Service Regions & Delivery Windows (`Coverage`)
5. Branch Structure (Create / Update / Toggle / Primary) (`Branch`)
6. Weekly Working Schedule (`Schedule`)
7. Base Capacity limits (`Capacity`)

**MUST NOT take effect without explicit review and approval by an authorized Admin (`RestaurantRegApprove`).**
Direct writes by restaurants to approved aggregates are blocked at the domain and API layers.

The **ONLY** authorized immediate mutation is an **Emergency Override** (temporary closure or capacity reduction), which has strict time boundaries, mandatory reason, audit trail, and admin notification.

---

## 2. Review State Machine Lifecycle
```text
  [Draft] <------------------------------------+
     |                                         |
     v                                         |
 [Submitted]  (BaseVersion snapshot recorded)   |
     |                                         |
     +--------------> [UnderReview]            |
     |                     |                   |
     v                     v                   |
[Approved]           [NeedsChanges] -----------+ (Increments PayloadVersion)
     |                     |
 (Atomic Apply)      [Rejected] (Terminal)
```

- **Draft:** Mutable by restaurant owner/staff. Does not affect active live operations.
- **Submitted / UnderReview:** Immutable payload. Cannot submit duplicate proposals in the same category.
- **NeedsChanges:** Admin sent back proposal with mandatory notes. Restaurant owner can revise and resubmit (which increments `PayloadVersion`).
- **Approved:** Admin approved. Atomic application of proposed payload to live aggregates (`RestaurantRegistration`, `RestaurantProfile`, `RestaurantBranch`, `RestaurantServiceRegion`, etc.) and bumps `ApprovedVersion`.
- **Rejected:** Terminal state. Proposal will not take effect; restaurant may prepare a new proposal based on current live state.

---

## 3. Database Migration & Rollback Procedure

### Forward Migration
1. Apply the versioning and review requests migration:
   ```bash
   dotnet ef database update --project src/MealMate.Persistence --startup-project src/MealMate.Api
   ```
2. Confirm the presence of table `RestaurantSettingsChangeRequests` and columns:
   - `RestaurantRegistrations.ApprovedVersion` (bigint, default 1)
   - `RestaurantProfiles.ApprovedVersion` (bigint, default 1)
   - `RestaurantEmergencyOverrides` table

### Data Backfill for Existing Open Requests
Existing open settings requests are automatically mapped with Category `Profile`. Run the following SQL query to verify backfilled rows:
```sql
SELECT "Id", "RestaurantId", "Category", "Status", "BaseVersion", "PayloadVersion"
FROM "RestaurantSettingsChangeRequests"
WHERE "Category" = 1 -- Profile
ORDER BY "CreatedAtUtc" DESC
LIMIT 50;
```

### Rollback Runbook
If an emergency rollback of the application service is required:
1. Revert application deployment to previous release tag.
2. The database schema is additive and backward compatible (new columns have safe defaults and nullable constraints). Do NOT drop columns or tables while traffic is draining.
3. If database rollback is strictly necessary:
   ```bash
   dotnet ef database update <PreviousMigrationName> --project src/MealMate.Persistence --startup-project src/MealMate.Api
   ```

---

## 4. Admin Handling of Stuck Proposals

### Symptom: Proposal stuck in `Submitted` or `UnderReview`
1. Check the open proposal for the restaurant:
   ```sql
   SELECT "Id", "RestaurantId", "Category", "Status", "BaseVersion", "CreatedAtUtc"
   FROM "RestaurantSettingsChangeRequests"
   WHERE "RestaurantId" = '<RESTAURANT_GUID>'
     AND "Status" IN (2, 3); -- Submitted, UnderReview
   ```
2. Check for version drift:
   ```sql
   SELECT "Id", "ApprovedVersion"
   FROM "RestaurantRegistrations"
   WHERE "ResultingRestaurantId" = '<RESTAURANT_GUID>';
   ```
3. If `BaseVersion` does not match current `ApprovedVersion`:
   - An admin approved a conflicting change in another category that modified the aggregate version.
   - Action: Admin should reject or mark as `NeedsChanges` with reason: `"Stale base version due to concurrent update. Please review current live values and resubmit."`
   - Restaurant owner reloads, verifies values, and resubmits.

---

## 5. Emergency Override Procedures

### Verification of Emergency Closure / Capacity Reduction
1. Check active emergency overrides:
   ```sql
   SELECT "Id", "RestaurantId", "StartDate", "EndDate", "ReducedCapacity", "IsClosed", "Reason", "IsResolved"
   FROM "RestaurantEmergencyOverrides"
   WHERE "RestaurantId" = '<RESTAURANT_GUID>'
     AND "IsResolved" = false
     AND "EndDate" >= CURRENT_DATE;
   ```
2. To manually resolve an expired or remediated emergency override via Admin API:
   ```http
   POST /api/v1/restaurant-operations/emergency-override/resolve
   Authorization: Bearer <TOKEN>
   Content-Type: application/json

   {
     "overrideId": "<OVERRIDE_GUID>",
     "reason": "Emergency situation resolved and normal operations verified."
   }
   ```

---

## 6. One-Time Historical Audit Script
This read-only query identifies approved restaurants whose active records were modified after approval without an accompanying approved change request:

```sql
-- Audit query: Detect approved restaurants with modifications lacking change request history
SELECT 
    p."Id" AS "RestaurantId",
    p."TradeNameEn",
    p."TradeNameAr",
    p."ApprovedVersion",
    p."ModifiedAtUtc" AS "ProfileModifiedAtUtc",
    r."ModifiedAtUtc" AS "RegistrationModifiedAtUtc",
    COUNT(cr."Id") AS "ApprovedChangeRequestsCount"
FROM "RestaurantProfiles" p
JOIN "RestaurantRegistrations" r ON r."ResultingRestaurantId" = p."Id"
LEFT JOIN "RestaurantSettingsChangeRequests" cr 
    ON cr."RestaurantId" = p."Id" AND cr."Status" = 4 -- Approved
WHERE p."VerificationStatus" = 4 -- Approved
GROUP BY p."Id", p."TradeNameEn", p."TradeNameAr", p."ApprovedVersion", p."ModifiedAtUtc", r."ModifiedAtUtc"
ORDER BY p."ModifiedAtUtc" DESC NULLS LAST;
```

> **POLICY NOTE:** Do not rewrite historical aggregate rows automatically. Any newly identified discrepancies must be reconciled through explicit administrative review.
