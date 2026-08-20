# Family per-member draft plan (partial PUT) — Design

- **Date:** 2026-08-20  
- **Status:** implemented (2026-08-20)  
- **Related:** `2026-08-20-family-catalog-eligibility-design.md`, `docs/mobile/family-subscription-mobile-flow-guide.md`  
- **Decision locked:** Approach **1** — same endpoint `PUT .../members/{memberId}/plan` with **optional fields + server-side merge**; save on every selection; keep dependent fields when still compatible.  
- **Plan:** `docs/superpowers/plans/2026-08-20-family-partial-member-plan.md`

---

## 1) Problem

Family setup screens need:

1. One **program** screen for all members (manager first, then switch chips).  
2. One **bundle + duration + Friday + tier** screen with the same member strip.  
3. **Persist immediately** on each catalog tap so switching members / killing the app does not lose choices.  
4. Today `SetFamilyMemberPlanRequest` requires the **full** plan and runs full eligibility — cannot save `programId` alone.

---

## 2) Product rules (locked)

| Rule | Behavior |
| --- | --- |
| Member strip | Visible on program, package/tier, and payment screens; `selectedMemberId` is client-local |
| Save timing | Every selection for the **current** member triggers `PUT .../plan` immediately |
| Persistence | Server is source of truth; refresh UI from response `members[]` |
| Program change | Keep `bundleId` / `durationDays` / `includeFriday` / `selectedTier` **if still valid** with the new program; otherwise clear only the incompatible fields |
| Checkout | Unchanged: `canCheckout` requires every member `setupStatus === Ready` (full plan + address + profile) |
| Catalog | Still family-only (`AvailableForFamily` on program and bundle) |

---

## 3) API contract

### 3.1 Endpoint (unchanged path)

`PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan`

Auth: customer JWT; draft group owned by manager; member must belong to that group.

### 3.2 Request — all fields optional

```csharp
public sealed record SetFamilyMemberPlanRequest(
    Guid? ProgramId = null,
    Guid? BundleId = null,
    RestaurantTier? Tier = null,
    int? DurationDays = null,
    bool? IncludeFriday = null);
```

JSON semantics (best / locked):

| Client sends | Meaning |
| --- | --- |
| Field **omitted** | Keep current member value |
| Field present with value | Set that value |
| Field present as JSON `null` | **Clear** that field (and run compatibility cleanup for dependents) |

Empty body (no properties): `400` + `family.plan_empty` — at least one field required.

### 3.3 Response

Same `FamilyGroupScreenDto` as today (full `members[]`, `currentStep`, package bounds).

### 3.4 Backward compatibility

Clients that still send the full five fields behave as today (full replace of plan fields + full validation). Partial clients send only changed keys.

---

## 4) Server merge + compatibility (best)

Algorithm in `SetFamilyMemberPlan` handler:

1. Load tracked draft group + member.  
2. Start from current member plan fields.  
3. Apply request patches (omit = keep; null = clear).  
4. **Compatibility cleanup** after apply:
   - If `programId` is set and `bundleId` is set: bundle must belong to that program, active, `AvailableForFamily`. Else clear `bundleId` and `selectedTier`.  
   - If `bundleId` cleared: clear `selectedTier`.  
   - If `programId` / `bundleId` / `tier` combination fails tier eligibility (when all three present): clear `selectedTier` only.  
   - If `durationDays` / `includeFriday` present (or retained): when validating, if duration not allowed → return error **only when that field was in the request**; if retained from before and became invalid after other changes, clear duration (and leave includeFriday) so the member stays `InProgress` instead of hard-failing a program-only save.  
5. **Validation intensity:**
   - `programId` alone: program exists, active, `AvailableForFamily`.  
   - `bundleId` (with resolved program): family catalog pair check.  
   - Full plan fields all non-null after merge: existing `FamilyPlanEligibilityService.ValidateAsync` (durations, tiers, friday).  
   - Partial incomplete plans: do **not** require full eligibility; only validate fields that are present after merge.  
6. Persist (`SaveChangesAsync`), recompute `setupStatus` via `FamilyMemberPlanRules`, return `MapScreen`.

`FamilyMemberPlanRules` stays: any plan field → `InProgress`; all plan + address + name/age → `Ready`.

---

## 5) Mobile UX (contract for Flutter docs)

### Screen [2] Program

- Strip of all `members[]`.  
- Tap program card → `PUT { "programId": "..." }` for `selectedMemberId`.  
- Switch member → UI reads that member’s `programId` from last response (highlight selected).  
- “Next” enabled when **every** member has `programId != null` (product gate on client; server still allows incomplete).

### Screen [3] Bundle + days + tier

- Same strip.  
- Each tap → partial PUT for that field(s). Prefer one field per request for simplicity; bundling multiple fields in one PUT is also allowed.  
- Switching members reloads that member’s `bundleId` / `durationDays` / `includeFriday` / `selectedTier`.  
- “Next” to payment when every member is `Ready` (or rely on summary `canCheckout`).

### Screen [4] Summary

- Unchanged summary/checkout APIs.

---

## 6) Errors

| Code | When |
| --- | --- |
| `family.plan_empty` | Body has no patch fields |
| `family.not_found` | Group/member missing |
| `family.not_draft` | Group not Draft |
| `family.catalog_not_available` | Program/bundle not family-eligible |
| `family.bundle_not_found` | Bundle not under program |
| `family.duration_not_available` / `family.friday_unavailable` | Duration rules fail when duration/friday are being set or full validation runs |
| Existing tier/pricing errors | When full eligibility runs |

---

## 7) Tests (acceptance)

API / handler tests:

1. PUT only `programId` → persists; `setupStatus` InProgress; other plan fields unchanged/null.  
2. PUT `programId` then switch-style PUT for second member → both members keep own `programId` after `GET current`.  
3. Change `programId` when old bundle belongs to old program → bundle + tier cleared.  
4. Change `programId` when somehow same bundle still valid (same program id) → keep bundle.  
5. PUT `bundleId` + `durationDays` + `tier` + `includeFriday` after program → Ready when address present.  
6. Full five-field PUT still Ready (regression).  
7. Empty body → `family.plan_empty`.  
8. Non-family program → `family.catalog_not_available`.

Update mobile guides: partial PUT examples; remove “must send full plan in one request” wording where it conflicts.

---

## 8) Out of scope

- New endpoints / PATCH verb.  
- Changing checkout, pricing, or MyFatoorah.  
- Auto-selecting tier/bundle when program changes.  
- Flutter app code in this repo (docs only on mobile side).

---

## 9) Implementation touchpoints (preview)

| Area | Files (expected) |
| --- | --- |
| DTO | `FamilyDraftCommands.cs` — nullable request fields |
| Handler | `FamilyDraftHandlers.cs` — merge + compatibility + staged validation |
| Eligibility | Possibly small helpers on `FamilyPlanEligibilityService` for program-only / pair-only checks |
| Controller | Likely unchanged signature |
| Tests | `CustomerFamilySubscriptionFlowControllerTests.cs` (+ unit if merge extracted) |
| Docs | `family-subscription-mobile-flow-guide.md`, `family-subscription-api.md`, `family-subscription-flutter-changes.md` |

---

## 10) Success criteria

- Manager selects program → saved; switch to second member → select program → saved; both visible after reload.  
- Same for bundle/duration/tier on the next screen.  
- Changing program does not leave an invalid bundle/tier attached.  
- Checkout still blocked until every member is Ready.
