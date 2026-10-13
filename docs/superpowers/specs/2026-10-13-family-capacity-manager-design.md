# Family capacity, manager subscription and upgrades

Approved for implementation by the user on 2026-10-13. The conversation is the authority; this document records its final decisions.

## Rules

- Family membership, occupied package seats, qualifying funded participants and purchase selection are separate quantities.
- A deferred member occupies one seat and keeps their profile and plan, but receives no subscription/service/calendar until verified payment. Removed members release a seat; expiry/freeze does not release ordinary members' seats.
- A manager starting without a live individual subscription occupies a seat and must be selected and paid in the first family purchase. Initial activation requires two funded participants, not two roster entries. A manager with a live paid individual subscription can qualify alongside one newly paid member but consumes no family seat.
- Preserve an existing manager individual subscription's identity, plan, amounts, dates, service days, meal selections, address and original individual ownership. Joining a family must not rewrite its FamilyGroupId or FamilyMemberId. Historical converted families remain readable and payable without destructive automatic migration.
- The manager who joined with individual service remains capacity-exempt after that service ends until choosing a family purchase. A family purchase for that manager requires a seat and no overlapping individual service. Individual renewal must remain possible while managing a family if the manager has no live family service.
- Manager personal calendar is the primary calendar regardless of purchase source; other members' calendars are entered through family management. With no current personal service show an empty personal-calendar state, never substitute another member or a financial payment anchor. Future personal service is distinguishable from current service. Family administration remains available after personal expiry and after all service ends; history is preserved.
- First activation and reactivation after all service ended require two qualifying participants. Existing live family permits one-member add-on/renewal. Include valid paid/scheduled/frozen service; expired dates or terminal statuses do not qualify. Never count an old converted pointer forever.
- The selected family size package is based on occupied seats, not automatic reselection from paid counts. Active family purchases/renewals apply the approved package discount snapshot; a larger package upgrade updates the snapshot for later purchases without retroactive repricing. No separate capacity-upgrade fee exists in the current settings model, so this operation changes capacity/discount only. Reject same/smaller packages and packages below occupancy.
- Deferred purchase uses current catalog/prices/start dates/eligibility at purchase time. Temporary offers use their existing validity rules at payment time; no invented offers or frozen restaurant promises.
- Existing personal tier-upgrade services remain the sole pricing/payment path: higher tiers only, remaining adjustable service days, existing financial verification, original end date, locked/delivered days preserved. Highest tier must not block family creation.
- Before payment recheck manager funding, minimum, capacity, completeness, address, start date and availability. Summary and checkout must agree on deterministic readiness; actual capacity must also be rechecked in the transactional reservation. Preserve batch versioning, idempotency, ownership/permissions and verified payment activation.

## UX contract

Expose occupied/available/max seats, deferred count, qualifying participants, current purchase count, manager seat exemption and localized reason codes; explicit per-member selection/payment/service state and available action routes. Provide manager personal-calendar current/upcoming IDs independently from group payment anchors. Distinct capacity-upgrade and personal-tier-upgrade actions. Customer mobile UI source is absent in this repository; implement API behavior and a precise bilingual mobile handoff, and do not claim UI implementation.

## Constraints and validation

No fake business data. New product copy belongs in Arabic and English message resources. Preserve business Kuwait timezone, currency and public identifiers. Work only in the isolated backend worktree; unrelated campaign work in the original checkout is excluded. No deployment/push. TDD for changed behavior; scoped unit/API/integration tests and final review. Database migration must be additive, reversible, model-snapshot consistent, and not applied to a live database in this task.
