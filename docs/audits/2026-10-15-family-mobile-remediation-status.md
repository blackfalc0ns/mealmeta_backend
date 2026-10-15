# Family mobile remediation — verification status

The implementation follows the user's approved decisions: one customer login; login provisioning retains family management; explicit detachment preserves service and grants supported own actions automatically; former manager retains approved roster/service reads only.

This document accompanies the original audit. Implemented does not mean deployed or PostgreSQL verified.

| Finding | Implemented remediation |
| --- | --- |
| F01 | Viable onboarding remains purpose-gated; historical inaccessible setup can recover through verified OTP without leaving obsolete setup flags blocking sessions. Recovery rechecks under group/identity locks and a transaction. |
| F02 | Linked identity phone is canonical; ordinary member edits cannot change the login phone without identity verification. |
| F03 | Freeze and renewal-consent writes recheck policy after shared transaction locks; detachment blocks submitted freeze intents. |
| F04 | Switcher identifies group/member/service and derives read/edit flags from policy across eligible contexts. |
| F05 | Home, detail, lifecycle and session contexts carry setup/detachment state. |
| F06 | New/uninitialized permissions get minimum own calendar/plan reading; deliberately saved grants remain intact. |
| F07 | Calendar/service permissions derive minimum read prerequisites; invoice permission stays independent of plan visibility. |
| F08 | Nullable permission patches preserve omissions, require version, and reject unsupported grants. |
| F09 | Purpose-specific first-login OTP resend rotates the challenge with rate limiting. |
| F10 | First login supports live Draft/CheckoutPending/Active family onboarding. |
| F11 | Pause/disable limits family data while session contexts report state; credentials remain customer-owned. |
| F12 | Customer password changes are identity-owned; pending onboarding still cannot skip verified setup. |
| F13 | Own service wins current selection; sibling grants only expose authorized reads and identifiable switcher rows. |
| F14 | Renewal responses preserve 403/Conflict/NotFound and localized ProblemDetails. |
| F15 | Credential operation keys prevent repeated generation; completed replay reports no recoverable plaintext and requires deliberate reset. |

Additional review corrections: generated temporary passwords guarantee a letter and a number using cryptographic randomness; detached former managers cannot read private allergies/dislikes/dietary endpoints or the complete saved address book. Both regressions failed before their fixes and passed afterward. Detached owners retain authorized service despite historical parent cancellation/deletion. Implicit reattachment remains unavailable.

## Evidence so far

- Relevant unit tests: 319 passed, none failed or skipped.
- Relevant API tests: 185 passed, none failed or skipped.
- PostgreSQL runtime verification after moving Docker storage to F: succeeded: 14 first-login/password-gate tests, 24 family lifecycle/settlement tests, one same-operation-key credential concurrency test, and one freeze-versus-detachment lock-race test passed.
- Final API build succeeded with zero warnings/errors after limiting compiler parallelism; an earlier unrestricted compiler attempt exhausted local memory.
- EF reports no pending model changes. An idempotent SQL script containing detachment, setup backfill and credential-operation migrations was generated offline at `artifacts/family-mobile-remediation.sql`; it has not been applied to a database.
- Integration-test project compiled; the new same-key concurrent issuance regression has not yet executed on PostgreSQL.
- Eight affected family error keys are present and nonempty in both Arabic and English resources. Mobile language switching has not been exercised because Flutter source is not in this workspace.
- `git diff --check` passed.
- Final independent review found no concrete production lock bypass in freeze or renewal-consent writes. Consent uses manager/own RequestRenewal policy and records the actor; it does not enable recurring charging.
- Prior full unit run: 2446 passed and one unrelated restaurant upcoming-24h regression failed. The restaurant-order working-tree changes belong to separate ongoing work and were preserved.

## Remaining acceptance gates

PostgreSQL runtime verification is now complete for the listed family flows. Docker storage was moved to F: and Docker remained available for isolated Testcontainers databases. The earlier startup failure was environmental and occurred before tests; no factory reset was used. Fixtures create and clean up only isolated `mealmate_test_*` databases and never migrate the configured application database.

The Flutter implementation and deployed server/database must be verified separately. No deployment, commit or push is claimed here. Full operational readiness is pending the listed gates.

The mobile integration contract is [family-management-ready-contract.md](../mobile/family-management-ready-contract.md).
