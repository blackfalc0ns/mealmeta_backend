# Family Roster & Unsubscribed Members Flow — Design Spec

**Status:** approved  
**Date:** 2026-09-14  
**Delivery:** Backend API contract + documentation for mobile customer app & admin dashboard.  
**Approach:** Decouple family roster management from subscription checkout. Allow adding family members to the roster without immediate subscription, excluding them from cart/pricing until explicitly subscribed, while presenting them in family management as "Unsubscribed / No Plan".

---

## 1. Problem & Context

In MealMate's family subscription flow, users previously felt compelled to configure a plan, address, and allergies for all members at once upon group setup. However, in real-world scenarios:
- A manager might want to add their spouse, children, or parents to the family roster first (e.g. for profile management, address linkage).
- The manager may choose to subscribe and pay only for specific members initially (e.g. manager + 1 child), leaving other members in the family without an active subscription.
- Unsubscribed members must remain in the family group (visible in Family Manager), but should not block checkout for payable members, should not generate pricing lines or fees, and should not appear as active subscribers in Admin KPIs or kitchen meal schedules.
- Later, the manager can select any unsubscribed member and purchase a subscription for them as an add-on.

---

## 2. Core Decisions & Rules

| Topic | Decision |
|---|---|
| **Roster vs Subscription** | Family group is a persistent roster. Adding a member adds them to `FamilyMembers` table with `MemberSubscriptionId = null` and `SetupStatus = NotStarted` (or `InProgress` if partially configured). |
| **Selection in Wizard** | In the initial family setup wizard, the manager can select which members to subscribe now. Only selected members proceed to Program, Bundle, Allergies, and Address screens. |
| **Payable Evaluation (`FamilyCheckoutGate`)** | Evaluates ONLY members with a complete pricable plan (`HasPricablePlan`). Unsubscribed/unconfigured members in the group do NOT block `canCheckout` as long as at least 1 member is payable (or meets the minimum for the selected package). |
| **Pricing & Quoting (`FamilyQuoteService`)** | Only payable members are quoted and summed into `subtotal` and `finalPrice`. Discount tiers calculate based on the number of payable subscriptions (plus active subscribers for add-on checkouts). |
| **Post-Payment Activation (`FamilyCheckoutService`)** | Creates subscriptions, schedules, and capacity allocations strictly for payable members. Unsubscribed members remain in the group with `MemberSubscriptionId = null`. |
| **Family Manager UI / Endpoints** | `GET /api/v1/customer/family-manager/members` lists ALL members. Unsubscribed members have `MemberSubscriptionId = null`, remaining days = 0, and setup status displayed. |
| **Admin Dashboard** | In family group detail, members without subscriptions are clearly marked as "Unsubscribed" / "Roster Only" and excluded from active subscriber metrics. |

---

## 3. Data Flow & Endpoints

### 3.1 Family Subscription Flow (Customer)
- `POST /api/v1/customer/family-subscription-flow/members`: Adds member to the roster (displayName, phone, relationship, age, gender).
- `PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan`: Sets/clears plan fields for selected members only.
- `GET /api/v1/customer/family-subscription-flow/summary`: Summarizes quote for payable members. `canCheckout` is true if payable members have complete plans, addresses, and allergies.
- `POST /api/v1/customer/family-subscription-flow/checkout`: Triggers MyFatoorah payment for the payable members.

### 3.2 Family Manager (Post-Active)
- `GET /api/v1/customer/family-manager/members`: Returns all members in roster with their subscription status.
- `GET /api/v1/customer/family-manager/home`: Shows family overview; unsubscribed members appear in the member list with no meal schedule.
- `POST /api/v1/customer/family-subscription-flow`: Can be reopened for adding plans to unsubscribed members or adding new members.

---

## 4. Verification & Testing

1. **Unit Tests (`FamilyCheckoutGateTests.cs`):** Verify `canCheckout` returns `true` when a family group contains 1 ready/payable member and 2 unsubscribed/unconfigured members.
2. **Quote Tests (`FamilyQuoteServiceTests.cs`):** Verify quote lines and subtotal exclude members who have no pricable plan.
3. **Checkout Execution Tests (`FamilyCheckoutServiceTests.cs`):** Verify that checking out a group with 1 payable member and 1 unconfigured member creates 1 subscription and leaves the 2nd member in the group with `MemberSubscriptionId == null`.
4. **Family Manager Tests (`FamilyManagerHandlerTests.cs`):** Verify `GetFamilyMembersQuery` and `GetFamilyHomeQuery` return both subscribed and unsubscribed members properly mapped.
