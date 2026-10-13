# Family capacity and manager calendar handoff — 13 October 2026

## Delivered backend behavior

- A roster member selected with `subscribeNow: false` remains in the family and occupies a package seat. They are not priced, activated, scheduled or shown as a service calendar until a later paid checkout.
- A manager with a live individual subscription keeps that subscription's identity, plan, dates, service days and calendar. The personal subscription is not rewritten into the family payment anchor and does not consume a family seat. It can qualify with one newly paid member for first family activation.
- A manager starting a family without live personal service must be selected and paid in the first family checkout. Two funded participants are required for initial activation; two deferred roster entries do not satisfy it.
- A family with no live service can be reactivated only with two funded participants. Active families may add one member at a time. Expired service does not silently qualify forever and does not remove family administration.
- The summary now distinguishes occupied seats, available seats, deferred members, current purchase count, qualifying participants, manager exemption and manager-live state. Use these values instead of deriving capacity from `rosterCount` or quote lines.
- The primary customer subscription screen prefers the manager's own current/upcoming family service when the actor is the family manager. It must never substitute a child's calendar for the main calendar. Child calendars remain family-management actions.

## Required calls

1. Load `/api/v1/customer/family-subscription-flow/current` and the summary after every member, plan, date, address, allergy or selection mutation.
2. Set payment intent with `PUT /members/{memberId}/checkout-selection` and `{ "subscribeNow": true|false }`. Preserve returned `batchId` and `batchVersion`.
3. For every selected payer, complete plan, start date, covered address, allergies and account linkage before checkout. Unselected members may remain incomplete.
4. Re-submit the latest `batchId`, `batchVersion`, `clientContractVersion: 1`, idempotency key and payment method to checkout. On `family.quote_changed`, reload summary and require confirmation of the new amount.
5. For a deferred member in an active group, expose `اشترك لهذا العضو / Subscribe for this member`; this opens the current family flow and re-evaluates catalog, restaurant eligibility, dates, prices and offers at payment time.

## UI state labels

Use semantic API codes and the existing Arabic/English resource dictionaries for: `manager_subscription_required`, `family.min_members`, `family.max_members`, `family.checkout_no_selection`, `family.quote_changed`, `family.checkout_pending`, `family.address_required`, `family.start_date_required`, `family.allergies_required`, `restaurant.not_eligible`.

Show separate labels for `paid`, `payment_pending`, `deferred`, `expired`, `manager_individual_live`, `manager_family_service` and `no_personal_service`. Do not infer a deferred state from a zero price.

## Upgrade and renewal UX

Personal tier upgrade uses the existing subscription upgrade endpoints and quote; show only higher tiers and preserve delivered/locked days. Family capacity upgrade is a separate operation: select a larger package, show new max seats and future discount, reject same/smaller packages or occupied-seat overflow, preserve paid subscriptions and history, and apply new package conditions to later purchases/renewals. These capacity-upgrade endpoints are not yet present in this handoff's backend commit and must not be called until shipped.

If the manager's personal service ends, keep family administration enabled. Offer individual renewal or family purchase when a seat is available; if full, offer a larger family package. The main calendar shows the manager's own current/upcoming service or a localized empty state, never another member's schedule.

## Acceptance matrix

| Scenario | Expected |
|---|---|
| Manager individual + one paid member | First family activation allowed; manager excluded from family seats; personal calendar retained |
| Manager no individual + two children selected | Blocked with `family.manager_subscription_required` |
| Manager no individual + manager and one child selected | Allowed; manager consumes a seat |
| Deferred member | Seat occupied, price/service/calendar absent |
| Expired manager individual | Family administration remains; manager not live/qualifying until family purchase or renewal |
| Main calendar with active child only | Empty personal state; child calendar available under family management |
| Price/restaurant changes before deferred payment | Current summary recalculated and confirmed before payment |

Mobile source is not present in this repository; runtime mobile rendering, RTL/LTR and language switching must be verified in the mobile application.
