# Family Subscription Checkout — Design Spec

**Status:** draft (2026-09-07)  
**Date:** 2026-09-07  
**Delivery:** Backend APIs + customer mobile contract / workflow updates. Flutter is not in this repo.  
**Approach:** Extend existing `/api/v1/customer/family-subscription-flow` (roster + multi-line cart). Do not invent a second family stack.  
**Supersedes (checkout only):** `workflows/customer/F19_family_subscription.md` “members after payment” and `00_mobile_subscription_app.md` §3.1 “family members activated after payment”. Post-pay manager panel (pause/detach/permissions) stays F19 / `/api/v1/customer/family`.

## 1. Problem

Figma (family setup wizard) and product decisions require:

1. A **saved family roster** independent of payment.
2. **Choose whom to subscribe now**; remaining members stay in the family with no subscription.
3. **Per-member** program, bundle, weekly 5/6 days (`includeFriday`), duration (26/12/6/custom), tier, allergies, address, start date.
4. **One manager payment** for the selected cart.
5. Phone is a **lookup key** in family management, not a “link existing user” button and not a password form.

Existing backend already has draft group, per-member plans, family catalog (`availableForFamily`), quote, and checkout. Gaps: phone lookup, occupation, persist members without a plan after the group is active, cart selection, required allergies, per-member start date, discount recount on later add-ons, and documenting the Figma screens for Flutter.

## 2. Locked decisions

| Topic | Decision |
|--------|----------|
| Family vs subscription | Family is a persistent roster. A member may exist with **no subscription**. Unsubscribed members do **not** appear in admin/subscription UIs as subscribers. |
| Who pays | Manager pays **one checkout** for selected members. |
| Plans | Each selected member has an independent program + bundle + duration + `includeFriday` + tier. |
| 5 / 6 days | Weekly service days: 5 = Friday off (`includeFriday=false`), 6 = Friday on. Duration remains 26 / 12 / 6 / custom. |
| Tiers | Figma Select / Plus / Max = Basic / Platinum / Elite. Labels come from admin `tierLabels`. |
| Address | **Per member**, not one family address. |
| Allergies | Required save per **selected** member before pay, including `{ code: "NONE" }`. No skip. Disliked ingredients allowed on the same screen; they are not a checkout gate. |
| Dietary preferences screen | **Not** in family checkout (absent from Figma). Program choice is the diet signal. |
| Add member | Family **management**, not the pay wizard. Phone first: if the number has a MealMate customer, return that owner’s display data; if not, empty form and create a new customer **without password**. No “add existing user” CTA. If the phone already belongs to **another family**, `409 family.already_in_family`. |
| Password | Not collected on add. Login/activation of the member account stays in family management (out of checkout). |
| One family | A customer belongs to **at most one** family. |
| Manager in cart | Manager may subscribe with the family. If the manager has an active **individual** subscription: notice to **upgrade into the family cart** or **keep the current individual plan**. |
| Discount (first cart) | Family size package resolved from **count of members being paid in this checkout**. |
| Discount (later add-on) | Recount **active family subscribers + new payable members**. Apply the new package rate to **new subscriptions only**. Do not retroactively change existing subscription prices. |
| Catalog | Same active programs/bundles as individual (restaurant offers required). Durations/tiers reuse individual subscription-flow endpoints. |
| Client totals | Ignored. Server recomputes quote. Checkout is idempotent. |

## 3. Domain

### FamilyGroup

- `Name`, `ManagerCustomerUserId`, `Status` (`Draft`, `CheckoutPending`, `Active`, …).
- Roster can be edited while `Draft` / `CheckoutPending`.
- After `Active`, the group **stays**. New members can be added without paying. A later checkout covers only unpaid selected members.
- Do not require a user-picked family size package on the Figma first screen. Resolve package from payable count and snapshot `SelectedFamilyPackageId` + `DiscountPercentSnapshot` at quote/checkout.

### FamilyMember

Keep existing plan fields (`ProgramId`, `BundleId`, `SelectedTier`, `DurationDays`, `IncludeFriday`, `DeliveryAddressId`, `SetupStatus`, `MemberSubscriptionId`).

Add / persist:

- `PhoneNumber` (E.164 or tenant-normalized national number).
- `Occupation` (optional string, Figma).
- `StartDate` (`DateOnly?`) — subscription start; **not** on the add-member management form.
- `LinkedCustomerUserId` (via existing `FamilyMemberAccess` or column on member — one source of truth). New phone with no account creates a customer user **without password**.
- `SelectedForCheckout` is **not** a durable roster flag. Checkout selection is the set of members included in the current quote/pay who have no active family-linked subscription (plus manager upgrade choice).

A member with `MemberSubscriptionId == null` is “من غير خطة”: visible in family management only.

### Subscription

Unchanged per-customer subscription. On successful family pay, create one subscription per payable member. Snapshot family discount on that subscription. Set `FamilyGroupId` / member link as today.

## 4. Screens (Figma mapping)

Progress: **1 الأعضاء → 2 البرنامج والباقة → 3 الحساسية والموقع → 4 الدفع**. Horizontal member strip from step 2.

Phone lookup and profile fields are **إدارة العيلة**. Checkout never creates accounts or passwords.

### Screen 1 — Family setup (07.01) — management

- Family name.
- Avatar strip + `+`.
- Add member: **phone first** → lookup fills owner name when found, else blank fields.
- Fields: full name, occupation, relationship, age. No password. No “existing user” button.
- Status chip: no plan until paid.
- Start date **not** here.
- `إضافة فرد` saves to roster without subscribing.
- `التالي: البرنامج` continues to subscribe-selected members (those without an active family subscription). Manager individual-sub banner: upgrade vs keep.

### Screen 2 — Program (07.04)

- Member strip; selected member highlighted.
- Family-only program cards.
- `التالي: الباقة والأيام` disabled until a program is set for the current member.

### Screen 3 — Bundle, weekly days, duration, tier (07.05)

- Bundle (e.g. 2 meals + snack vs 1 meal).
- 5 days / 6 days → `includeFriday`.
- Duration 26 / 12 / 6 / custom (**added vs Figma**).
- Select / Plus / Max = Basic / Platinum / Elite labels.
- `التالي: الحساسية`. Walk remaining selected members through screens 2–3, then allergies.

### Screen 4 — Allergies (07.02)

- Allergy chips + “no allergies”.
- Disliked-ingredient search/tags (not a pay gate).
- **No skip.** Save required, including NONE.
- `التالي: الموقع`.

### Screen 5 — Delivery location (per member)

- Map + pin.
- Governorate, area, block, street, building, floor, apartment. `regionId` required.
- May reuse an address already on the linked customer, or create one for that member.
- Confirm location; next member or `التالي: الدفع`. Block pay if no restaurants cover the address.

### Screen 6 — Payment (07.06)

- Card per **cart** member: photo/name, plan, duration, line price after family discount.
- Delivery fees if the individual engine returns them.
- Total: monthly/cart total for N payable members.
- Methods: card, Apple Pay, K-Net (existing gateway list).
- Comparison table of selected members’ slots/options.
- Roster members not in the cart are omitted from total and from admin subscriber views.
- `تأكيد الدفع` / `السابق: الموقع`.

After success: each payable member gets a subscription and calendar. Others remain no-plan in family management.

## 5. API

Base (existing): `/api/v1/customer/family-subscription-flow`  
Manager JWT only. Customer role required.

### Keep

| Method | Path | Role |
|--------|------|------|
| GET | `/metadata`, `/relationships`, `/packages`, `/current` | roster metadata |
| PUT | `/group` | name |
| POST | `/convert-from-individual` | manager upgrade path |
| GET | `/programs`, `/programs/{id}/bundles` | family catalog |
| PUT | `/members/{id}/plan` | per-member plan patch |
| GET | `/summary` | payment screen quote |
| GET | `/payment-methods` | methods for quoted amount |
| POST | `/checkout` | one payment, N subscriptions |
| DELETE | `/members/{id}` | remove unpaid member |

Durations/tiers: existing individual `GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations` and `/tiers`.

### Change / add

**Lookup (management, not checkout):**

`POST /api/v1/customer/family-subscription-flow/members/lookup`  
Body: `{ "phone": "+965..." }`

- Hit: `{ "found": true, "displayName", "ageYears?", "gender?" }` — limited fields, not full PII.
- Miss: `{ "found": false }`.
- Other family: `409 family.already_in_family`.
- Rate-limit by manager id + IP.

**Add member (management):**

Extend `FamilyMemberWriteDto`:

```json
{
  "phone": "+96550000000",
  "displayName": "أحمد",
  "occupation": "طالب",
  "relationshipCode": "son",
  "gender": "Male",
  "ageYears": 16
}
```

- Found phone → attach that customer user; fill name from lookup if client sends empty displayName.
- Miss → create customer user with that phone. Manager never sets a password. Server may store a random unusable password hash so Identity stays valid; login/activation stays in family management (out of checkout).
- Do **not** require program/plan. `SetupStatus` stays not-ready until plan + address + allergies for checkout.
- After group is `Active`, adding a member is still allowed (roster grow). Do not keep `family.not_draft` for add/update of unpaid members.

**Select / quote:**

- Summary/checkout payable set = members with a complete plan who are selected and have no active family subscription (manager keep-individual → manager line price 0 / omitted as today for converted prepaid manager).
- First checkout: family package from **payable count**.
- Later: package from **active subscribers with family-linked subs + new payable members**. Discount applies to new lines only.

**Start date:** include `startDate` on `SetFamilyMemberPlanRequest` (optional JSON). Server validates against coverage / schedule rules used by individual checkout.

**Member address & allergies (manager on behalf of member):**

- `GET/POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses`
- `PUT /api/v1/customer/family-subscription-flow/members/{memberId}/allergies`  
  Body same as individual `{ "allergies": [ { "code": "NONE" } ] }`.
- `PUT .../disliked-ingredients` optional, not a gate.

**Readiness (per selected member):** `ADDRESS` then `ALLERGIES` then plan-complete. No `DIETARY_PREFERENCES`. `canCheckout` false until every payable member is ready and each address has delivering restaurants.

**Checkout:** existing `POST .../checkout` with idempotency/`correlationId`. Recompute quote server-side. Create subscriptions only for payable members.

### Errors (stable codes)

| Code | HTTP | When |
|------|------|------|
| `family.phone_required` | 400 | add/lookup without phone |
| `family.already_in_family` | 409 | phone’s user already in a family |
| `family.max_members` | 409 | over admin max |
| `family.plan_incomplete` | 409 | pay with incomplete selected member |
| `family.allergies_required` | 409 | selected member has not saved allergies |
| `subscription_flow.no_restaurants_for_address` | 409 | member address uncovered |
| `conversion.convert_required` / manager keep vs upgrade | 409 | existing individual-sub rules, plus explicit keep |

Never accept client `finalPrice`.

## 6. Pricing

1. Price each payable member with `PreviewCustomerPricingQuery` (same as individual).
2. Subtotal = sum of payable lines (manager keep-individual / already-paid converted manager = 0).
3. Resolve family size package from **discount-count** (see locked table).
4. `familyDiscountAmount = round(subtotal * package.discountPercent, 3, AwayFromZero)` when count is in `[minMembers, maxMembers]`.
5. `finalPrice = subtotal - familyDiscountAmount`.
6. Persist snapshot on group and on each **new** subscription.

Existing `FamilyQuoteService` uses `group.Members.Count` and `SelectedFamilyPackageId`. Change to payable/discount-count as specified; do not use full roster size when unpaid relatives exist.

## 7. Admin

`/api/v1/admin/family-groups` remains monitoring. List/detail members with `MemberSubscriptionId` as subscribers. Unpaid roster members may appear as members with setup/no-plan — **not** as subscription rows, dispute, or checkout KPIs.

Admin does not create family passwords in this spec.

## 8. Docs to update (implementation)

- `workflows/customer/F19_family_subscription.md` — roster first, subscribe selected, phone lookup, no password on add.
- `workflows/admin/F19_family_subscription.md` — unpaid members are not subscribers.
- `workflows/customer/00_mobile_subscription_app.md` — family wizard screens + family-subscription-flow table; remove “members after payment only”.
- `workflows/customer/00_screens_flowcharts.md` — replace F19 four-screen post-pay-only chart with the six Figma screens plus duration.

## 9. Testing

- Lookup found / miss / already-in-other-family / rate limit.
- Add without plan; member has no subscription; admin list does not treat them as subscriber.
- Per-member program/bundle/duration/friday/tier priced independently.
- Allergies NONE saved vs missing blocks checkout.
- Uncovered address blocks that member’s checkout, not other ready members? **Decision:** whole cart cannot pay until every **selected** member is ready. Unselected unpaid members do not block.
- Discount: cart of 3 uses 3-person package; later add 1 with 3 actives uses 4-person rate on the new line only; old prices unchanged.
- Manager keep individual vs upgrade.
- Checkout idempotency; server total ≠ client total.
- One family per customer.

## 10. Out of scope

- Flutter UI implementation (contract only).
- Member login OTP/password-set UX details (family management, not checkout).
- Detach / convert-to-individual / pause permissions (existing F19 `/api/v1/customer/family`).
- Changing already-paid member plans in the same wizard (keep current lock for converted manager plan unless a later spec).
- Restaurant dashboard.

## 11. Current code (gap summary)

| Area | Today | Target |
|------|--------|--------|
| Add member DTO | name, relationship, gender, age, address | + phone, occupation; address/allergies/startDate in later steps |
| Phone | unused on add | lookup + create-or-attach |
| Password | n/a | never on add |
| Members after Active | add blocked (`family.not_draft`) | add unpaid members; later checkout |
| Quote count | all `group.Members` | payable / active+new |
| Allergies | not on family flow | required for selected members |
| Start date | not on member | per selected member |
| Dietary gateway | individual flow | omit for family |
