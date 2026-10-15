# Family mobile gap remediation implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Follow the ownership split below; do not edit another worker's files concurrently.

**Goal:** Repair all F01–F15 findings in the family mobile audit and deliver a verified backend contract for one customer login entry point.

**Architecture:** Retain one normal customer login, separate account provisioning from detachment, and enforce supported own rights for detached customers. Resolve account recovery without reviving obsolete temporary credentials. Derive UI state from the current actor's policy. Keep subscription service lineage, immutable paid delivery snapshots, financial ownership, and optimistic concurrency.

**Spec:** User explicitly authorized fixing every finding in `docs/audits/2026-10-15-family-mobile-management-audit.md`. Previous user decisions remain binding: same normal login, member retained read-only for manager after detach, subscription/calendar preserved, automatic supported own rights after detach.

**Tech stack:** .NET 9, EF Core, PostgreSQL, ASP.NET API, xUnit.

## Global constraints and decisions

- Preserve unrelated offers, advertising and restaurant orders working-tree changes. No push/deploy in this task without explicit instruction.
- Arabic and English resources for new product-owned copy. Do not invent working implementations for unsupported actions.
- Detached member: full supported own actions, no sibling scope, manager read-only. Explicit reattach is not introduced: restored management requires a future consent flow; no implicit restoration.
- Account pause/disable concerns family data access, not ownership of the customer's global identity or personal subscriptions. Expose this scope explicitly.
- Managed account provisioning initializes minimum own calendar/plan read grants only when missing/uninitialized; existing deliberate grants must not be silently overwritten. Validate write dependencies on read grants.
- Allow family onboarding in Draft/CheckoutPending/Active, exposing a valid customer context without service when no subscription is paid. Calendar service readiness remains enforced separately.
- An inactive/removed pending onboarding identity still must not accept an obsolete temporary password as a normal session. Permit OTP-verified customer recovery for inaccessible/historical setup and clear obsolete setup gates safely; viable active family first-login still cannot bypass onboarding.
- Linked user phone is the login source of truth. Reject unverified edits of the member phone to a different value; expose the actual linked phone.
- Permission updates use consistent nullable patch semantics with expectedVersion; omitted values preserve grants. Add dependency/unsupported metadata for controls.
- Issuing temporary credentials: accept an operation idempotency key. Repeated key must not reset again. Do not persist recoverable plaintext; expose truthful completed/result-not-recoverable state and permit an explicit new reset operation after a lost response.
- Historical detached links do not count as managed membership in another family. Do not drop old service lineage. Prevent multiple live managed links, while permitting independent historical detached contexts.
- Protect frozen identity/group history for detached service without granting former manager writes. Keep manager financial history limited to authentic payer evidence and approved read views.
- Pending freeze requests and consent changes need authorization rechecked under shared group locks. Detachment must explicitly handle outstanding manager freeze intents (reject until resolved rather than silently altering service).

## Ownership and tasks

### Task A: Account lifecycle and onboarding (worker auth)

Files: `FamilyMemberFirstLogin.cs`, `OtpAndPassword.cs`, `LoginRefreshLogout.cs`, relevant auth controllers/contracts, `CustomerSelfService.cs`, auth/session middleware helpers as needed, corresponding IdentityAccess tests/resources.

- [ ] Write regressions for F01 recovery after removal/cancelled membership without obsolete temporary session, F09 purpose-correct resend, F10 onboarding before payment, F11 access scope, F12 explicit credential ownership.
- [ ] Implement purpose-specific first-login resend with challenge rotation and authorization/rate-limit consistency.
- [ ] Permit safe OTP recovery of historical/inaccessible setup and reset state only after validated password replacement; retain active onboarding security.
- [ ] Normal password change/recovery remain identity ownership operations; expose scope and remove misleading manager password control for managed accounts if required by catalog.
- [ ] Keep normal customer login as sole entry; no member-login endpoint.

### Task B: Manager contract/state/provisioning (worker manager)

Files: all FamilyManager files except `FamilyActionPolicy.cs` (coordinate if needed), `FamilyDraftHandlers.cs`, FamilyManager controller, identity family phone checks, domain/persistence migration for credential operation if needed, corresponding FamilyManager/API tests.

- [ ] F02 canonical linked phone + reject unverified phone changes.
- [ ] F04/F05 actor-correct switcher and coherent lifecycle access/detach/setup fields across home/detail/list/me.
- [ ] F06 minimum initial read grants; do not clobber deliberate existing grants.
- [ ] F08 nullable versioned patch; dependencies and unsupported metadata; preserve 409 optimistic conflict semantics.
- [ ] F13 member-scoped discoverability: expose only self and permitted siblings with per-actor read capabilities, no manager roster payload leakage.
- [ ] F15 credential operation replay protection without storing plaintext and truthful lost-response recovery contract.
- [ ] Exclude detached historical links from new managed membership collisions and prevent shared-profile write bypasses.
- [ ] Reject detachment while outstanding manager freeze requests/financial operations remain unresolved; idempotent repeat unchanged.

### Task C: Subscription enforcement/projections (worker subscriptions)

Files: `SubscriptionFreezeService.cs`, `SubscriptionOperationsHandlers.cs`, `CustomerSubscriptionQueryService.cs`, `CustomerSubscriptionContracts.cs`, `CustomerSubscriptionManagementController.cs`, renewal/consent services as needed, corresponding CustomerSubscriptions/Freeze/API/Integration tests.

- [ ] F03 recheck customer freeze authority under family/subscription lock while preserving admin permissions.
- [ ] F07 consistent discovery for calendar-only/read-limited users without leaking plan/financial data; coordinate dependency rules with manager worker.
- [ ] F13 own-first current selection for nonmanager + member identity on cards, allowed sibling visibility only.
- [ ] F14 use consistent HTTP status and localized semantic ProblemDetails.
- [ ] Group lock and authorization recheck for auto-renew consent changes; retain NotConnected truthful billing state.

### Task D: Integration and release verification (root)

- [ ] Inspect each implementation against its finding; resolve shared contract conflicts and code review findings.
- [ ] Run fresh scoped unit/API tests, full relevant family regression suites, PostgreSQL migrations/integration/concurrency tests, API build, resource parity, diff checks.
- [ ] Resolve Docker/runtime setup using supported tooling without destructive changes; if unavailable, report exact unverified checks instead of promising 100% runtime validation.
- [ ] Update mobile handoff with endpoint/state/action/error examples and one-login routing; publish finding-by-finding completion evidence.
- [ ] No Flutter runtime claims without Flutter application source; no production migration/deployment claims without authorized deployment and evidence.
