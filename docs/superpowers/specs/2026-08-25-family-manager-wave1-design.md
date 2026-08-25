# Family Manager Wave 1 — Design Spec

> **Status:** Draft for review  
> **Date:** 2026-08-25  
> **Audience:** Backend + Flutter mobile  
> **Scope:** Post-payment family management by the **family manager** (screens 08.xx), not MealMate platform admin.

---

## 1. Goal

Enable the paid family manager to:

1. Open **08.01 Family Manage** as the family tab home.
2. Manage members (list, detail, add, access, permissions).
3. Select meals per member using the **same calendar rules as individual** subscriptions.
4. Allow both manager and authorized members to edit a member’s day (last write wins) until −72h.

Out of Wave 1 implementation (icons may appear on 08.01 as stubs): Addresses, Subscriptions detail pages, Payments, Reports (Waves 2–3).

---

## 2. Locked product decisions

| Decision | Value |
|----------|--------|
| Actor | Family manager on mobile (not platform admin) |
| Family tab entry | **08.01** dashboard |
| Calendar rules | **Identical to individual** (draft → Done → `CustomerSelected` → restaurant at −72h only) |
| No early send | Manager cannot dispatch to restaurant before −72h |
| Who can edit meals | Manager **and** member with `editDailyMeals` (last save wins) |
| Permissions | Full 08.20 / 08.21 in Wave 1 |
| Architecture | **Family Hub** APIs + reuse `CustomerCalendarEngine` with access gate |
| Delivery order | Contract → Wave 1 code → Wave 2 addresses/subscriptions → Wave 3 payments/reports |

---

## 3. Architecture

```
┌─────────────────────────────────────────┐
│  Family Hub (new)                       │
│  /api/v1/customer/family/...            │
│  home, members, access, permissions     │
└──────────────────┬──────────────────────┘
                   │ resolves memberSubscriptionId
                   ▼
┌─────────────────────────────────────────┐
│  CustomerCalendarEngine (existing)      │
│  /api/v1/customer/subscriptions/{id}/…  │
│  + FamilyCalendarAccessGate             │
└──────────────────┬──────────────────────┘
                   │ −72h lock job
                   ▼
┌─────────────────────────────────────────┐
│  Restaurant orders (existing)           │
│  SentToRestaurant → confirm → daily     │
└─────────────────────────────────────────┘
```

**Why this shape**

- Single source of truth for −72h, Done, repetition limit, frozen pool, restaurant dispatch.
- Hub owns family administration; calendar owns the daily box.
- Authorization at the boundary (`FamilyCalendarAccessGate`), not a forked calendar engine.

**Explicit change to existing code**

- Remove blind reject `calendar.individual_only` when `FamilyGroupId != null`.
- Replace with gate: caller must be manager of that group **or** linked member with permission on that member’s subscription.

---

## 4. Domain model additions

Existing: `FamilyGroup`, `FamilyMember` (`MemberSubscriptionId`, plan fields), per-member `Subscription` after checkout.

### 4.1 `FamilyMemberAccess`

| Field | Type | Notes |
|-------|------|--------|
| `FamilyMemberId` | Guid (PK/FK) | 1:1 with member |
| `HasSeparateAccount` | bool | 08.20 toggle |
| `LinkedCustomerUserId` | Guid? | Set when invite accepted |
| `InviteTokenHash` | string? | Opaque invite |
| `InviteExpiresAtUtc` | DateTime? | |
| `IsPaused` | bool | Blocks meal edits |
| `PausedAtUtc` | DateTime? | |
| `RemovedAtUtc` | DateTime? | Soft-remove from family |
| `RowVersion` | byte[] | Concurrency |

### 4.2 `FamilyMemberPermissions`

| Field | Type | Default (non-manager member) |
|-------|------|------------------------------|
| `FamilyMemberId` | Guid | |
| `EditDailyMeals` | bool | false |
| `ChangeDeliveryTime` | bool | false |
| `ViewProgram` | bool | false |
| `ChangeProgram` | bool | false |
| `EditMealsAndDays` | bool | false |
| `EditAddress` | bool | false |
| `UnitExchange` | bool | false |
| `ChangePassword` | bool | false |
| `RowVersion` | byte[] | |

**Manager:** always has full effective permissions; no row required (or row ignored).

**On member create:** create Access + Permissions rows with defaults above.

---

## 5. Authorization matrix

| Action | Manager | Linked member (own) | Other member |
|--------|---------|---------------------|--------------|
| `GET family/home` | Yes | Limited: self summary only **or** 403 — **Wave 1: manager-only home**; member uses own calendar entry points | No |
| List/manage all members | Yes | No | No |
| PUT access / permissions | Yes | No | No |
| Pause / remove member | Yes (not self) | No | No |
| Read member calendar | Yes | Yes if linked to that member | No |
| Write calendar (restaurant/slot/complete) | Yes if not paused day lock | Yes if `EditDailyMeals` and not paused and day editable | No |
| Change program (Wave 1 API stub if needed) | Yes | Only if `ChangeProgram` | No |

**Wave 1 clarification:** `GET /family/home` and member CRUD/access/permissions are **manager-only**. Linked members open meal selection via their own `memberSubscriptionId` calendar routes after login.

---

## 6. Screens ↔ APIs (Wave 1)

Base Hub: `/api/v1/customer/family`  
Auth: Bearer customer JWT.

### 6.1 Home — 08.01

```http
GET /api/v1/customer/family/home
```

Response (conceptual):

- `group`: id, name, status, package label, price, currency, renewal/end label
- `members[]`: memberId, displayName, avatarUrl?, role, isPaused, memberSubscriptionId?, programLabel?, todayMealSummary?
- `todaySchedule[]`: memberId, displayName, uiStatus, filledCount, requiredCount
- `shortcuts[]`: `{ code: addresses|subscriptions|payments|reports, enabled: false, unavailableReason: "wave2"|"wave3" }` for Wave 1

### 6.2 Members — 08.02 / 08.03

```http
GET    /api/v1/customer/family/members
GET    /api/v1/customer/family/members/{memberId}
POST   /api/v1/customer/family/members
```

`POST` body: displayName, relationship, gender, age, goal, activity, programId, bundleId, tier, durationDays, includeFriday (align with existing draft member fields where possible).

**Gap control — add member after paid group:**  
If family package capacity allows another seat without extra payment → create member + subscription fulfillment path consistent with existing family checkout rules.  
If extra payment required → return `409 family.member_requires_payment` with payload pointing to Wave 2/3 payment flow (do **not** silently create unpaid seats).

### 6.3 Access — 08.20

```http
GET /api/v1/customer/family/members/{memberId}/access
PUT /api/v1/customer/family/members/{memberId}/access
```

PUT fields: `hasSeparateAccount`, `isPaused`, `remove` (bool).  
Removing manager → `409 family.cannot_remove_manager`.  
Pause → blocks calendar writes for that member.

### 6.4 Permissions — 08.21

```http
GET /api/v1/customer/family/members/{memberId}/permissions
PUT /api/v1/customer/family/members/{memberId}/permissions
```

Body: the eight boolean flags. Manager target → `409 family.manager_permissions_immutable`.

### 6.5 Calendar — 08.04 → 08.10

Reuse individual contract exactly:

Base: `/api/v1/customer/subscriptions/{memberSubscriptionId}`

| Purpose | Method | Path |
|---------|--------|------|
| Month/days | GET | `/calendar` |
| Day restaurants | GET | `/days/{date}/restaurants` |
| Restaurant meals | GET | `/days/{date}/restaurants/{restaurantId}/meals?slot=` |
| Select restaurant | POST | `/days/{date}/restaurant` |
| Slot meal draft | POST | `/days/{date}/meals/slot` |
| Done | POST | `/days/{date}/complete` + `Idempotency-Key` |
| Auto-select | POST | `/days/{date}/auto-select` |

UI status mapping unchanged from individual calendar doc.  
Member switcher on 08.04 uses Hub member list → switch `memberSubscriptionId`.

### 6.6 Separate account invite (minimal Wave 1)

When `hasSeparateAccount` becomes true and no `linkedCustomerUserId`:

1. Create invite token (hashed at rest).
2. Return `inviteDeepLink` / expiry in access GET (and optionally notify via existing email/OTP channels if already wired).
3. Accept endpoint (Wave 1 required to close the loop):

```http
POST /api/v1/customer/family/invites/{token}/accept
```

Authenticated customer becomes `LinkedCustomerUserId` for that member (one active personal family membership). Conflicts → `409 family.invite_conflict`.

---

## 7. Calendar gate algorithm

On every calendar read/write for subscription `S`:

1. Load `S`. If `FulfillmentCompletedAtUtc` null → `calendar.fulfillment_not_ready`.
2. If `S.FamilyGroupId` is null → existing individual owner check (`S.CustomerUserId == caller`).
3. If family:
   - Find `FamilyMember` where `MemberSubscriptionId == S.Id` and not removed.
   - If caller is `FamilyGroup.ManagerCustomerUserId` → allow (writes still respect day lock + pause).
   - Else if caller == `Access.LinkedCustomerUserId` and not paused:
     - Read: allow.
     - Write: require `Permissions.EditDailyMeals`.
   - Else → `403` / not found (no leakage across members).
4. If `Access.IsPaused` and write → `409 family.member_paused`.
5. Existing day lock / deadline rules unchanged.
6. Writes include concurrency token: if `SubscriptionDay` rowVersion mismatch → `409 concurrency.conflict`.

---

## 8. Operational flow (no gaps vs restaurant)

```
Manager/member completes box → CustomerSelected
        ↓
SubscriptionDayLockJob at −72h
        ↓
SentToRestaurant + notifier (existing dispatch)
        ↓
Restaurant pending confirmation → confirm/reject
```

Family days use the same jobs and restaurant APIs; restaurant sees `RestaurantId` on the day as today. No separate family order pipeline in Wave 1.

---

## 9. Error catalog (Wave 1)

| Code | When |
|------|------|
| `auth.forbidden` | Not manager / not linked member |
| `family.member_paused` | Write while paused |
| `family.permission_denied` | Missing flag |
| `family.cannot_remove_manager` | Remove manager |
| `family.manager_permissions_immutable` | Edit manager permission row |
| `family.member_requires_payment` | Add seat needs payment |
| `family.invite_conflict` | Accept invite invalid state |
| `family.group_not_active` | Group not paid/active |
| `calendar.selection_locked` | Past −72h / locked statuses |
| `calendar.fulfillment_not_ready` | Member sub not fulfilled |
| `concurrency.conflict` | Stale day write |

---

## 10. Testing plan (mandatory)

1. Manager completes member box → lock job → restaurant pending contains day.
2. Linked member with `EditDailyMeals` can complete; without flag → 403.
3. Paused member → writes fail; manager can still manage access.
4. Two writers: second with stale version → conflict; fresh version wins.
5. Member A cannot read/write member B subscription (403/404).
6. −72h auto-select on family member subscription respects allergies + repetition.
7. Existing individual calendar tests remain green (non-family path unchanged).
8. Remove manager blocked; remove non-manager soft-deletes and hides from lists.

---

## 11. Explicit non-goals (Wave 1)

- Platform admin family console.
- Addresses / payments / reports page implementations.
- Early restaurant dispatch before −72h.
- Forked `FamilyCalendarEngine`.
- Changing individual calendar product rules.

---

## 12. Implementation sequence (after spec approval)

1. Persistence: Access + Permissions + migration.
2. `FamilyCalendarAccessGate` + calendar engine ownership change.
3. Family Hub controllers/handlers (home, members, access, permissions, invite accept).
4. Unit tests (matrix + calendar gate + E2E lock→restaurant).
5. Mobile contract doc under `docs/mobile/family-manager/` mirroring this API table.
6. Waves 2–3 separate specs.

---

## 13. Spec self-review checklist

- [x] No TBD placeholders for Wave 1 behavior
- [x] Calendar = individual (consistent with locked decisions)
- [x] Add-member payment gap handled via explicit error, not silent create
- [x] Invite accept included so separate-account toggle is not a dead end
- [x] Manager-only Hub vs member calendar access split stated
- [x] Concurrency and pause/remove edge cases covered
- [x] Scope split Waves 2–3 explicit

---

## Approval

Please review this file and confirm:

- **Approve** → proceed to implementation plan (`writing-plans`)
- **Changes** → list edits; spec will be updated before planning
