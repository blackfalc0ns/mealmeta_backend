# F01 foundation and governance implementation report

**Date:** 2026-10-21  
**Status:** Implemented with a deliberately narrow supported contract; metadata configuration and downstream finance consumers remain phase prerequisites.

## Delivered behavior

- The existing `RuleVersion` is the governed source. New governed drafts require an active legal entity, a defined scope, valid UTC microsecond-aligned half-open dates, bounded identity/reason fields, and a supported typed payload.
- Supported payloads are `CANCELLATION` schema 1 (`minimumExecutedDays`, `minimumUnexecutedExclusive`, both required nonnegative integers) and the legacy `cancel.fee` schema 1 contract. No business rates are invented. Other rule contracts return a localized unsupported-contract error and await their owning phase.
- Draft creator identity is assigned server-side. Only the creator can submit; a different checker must approve or reject. Approval and rejection require the submitted governance fingerprint and row version. The fingerprint is canonical across PostgreSQL jsonb key ordering/number representation and covers scope, entity, window, schema, creator, owner, previous version, reason, and payload.
- Financial scope locks use the existing PostgreSQL advisory transaction lock manager. Approval, audit, and outbox commit atomically. Denied self-approval/stale/overlap/state decisions are recorded after the rejected business transaction rolls back.
- Approved governed content is protected against field mutation, lifecycle downgrade, hard delete and soft delete. Financial rule audit rows are append-only. Legacy `LegalEntityId == Guid.Empty` records remain usable for non-governed resolution.
- Saved paid snapshots can resolve their `Superseded` rule version. Money allocation uses exact decimal-to-BigInteger weights and largest remainders.
- API routes use separate `finance.governance.view`, `.manage`, and `.approve` permissions. Lists/detail/metadata reads are bounded and no-tracking. Simulation validates a candidate and overlap without writes.
- External reporting readiness fails closed until F09 supplies a verified taxonomy-package registry. An arbitrary package id cannot enable external filing. Entity/profile data are read from persistence; this phase creates no production seeds.
- The Angular `/admin/finance/governance` screen uses the real API, typed cancellation fields, scoped review details, expected hash/version decisions, bilingual reactive copy, and permission-gated commands.

## Validation

Commands executed in this phase used an isolated .NET artifacts directory (`F:\MealMate\outputs\finance-phase01-dotnet`) to avoid sharing `bin/obj` outputs with another active executor.

| Check | Result |
|---|---|
| `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~Finance" --artifacts-path F:\MealMate\outputs\finance-phase01-dotnet --disable-build-servers -m:1 --no-restore` | Passed: 53, failed: 0, skipped: 0. |
| `dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter "FullyQualifiedName~RuleApprovalApiTests" --artifacts-path F:\MealMate\outputs\finance-phase01-dotnet --disable-build-servers -m:1` | Passed: 5, failed: 0. HTTP workflow tests use the real service and isolated PostgreSQL, including same-actor denial, independent checker approval, stale fingerprint/version conflicts, authorization, and serialized string enums. |
| `dotnet test tests/MealMate.IntegrationTests/MealMate.IntegrationTests.csproj --filter "FullyQualifiedName~RuleApprovalPostgresTests" --artifacts-path F:\MealMate\outputs\finance-phase01-dotnet --disable-build-servers -m:1 --no-restore` | Passed: 5, failed: 0. Covered jsonb round-trip fingerprint stability, competing approvals/audit/outbox, deterministic advisory-lock waiting, additive migration Down/Up, and read-only simulation. |
| `npm run typecheck` | Passed after the final Angular changes. |
| `npm run build` | Final production build passed (exit code 0); existing budget/CommonJS warnings remain. |
| `node scripts/i18n-check.cjs --self-test` | Passed. The scoped scanner's two translated status-label false positives are documented below; no full-repository gate is claimed. |

The PostgreSQL helper was directed to the temporary local test server and created only isolated `mealmate_test_<GUID>` databases. No credentials are stored here. No live database migration was run.

The primary Codex agent independently reran the Finance unit suite (53/53), PostgreSQL integration suite (5/5), and PostgreSQL-backed HTTP API suite (5/5); each run had zero failures and zero skips.

The localization scanner reported two status-label false positives because it sees the template's translated `t().status` header and label as a raw status display; actual row states use the reactive bilingual status dictionary. Other scanner advisories are reviewed as typed copy, permission codes, or API/domain identifiers. No scanner baseline was changed.

The independent browser review exercised the screen with HTTP intercept fixtures only; it verified Arabic/English switching, localized errors that remain open across a locale change, readiness string enums and scoped identity rendering, stale-readiness response discard, a validation alert, and a 390px mobile viewport with no horizontal overflow. The API and PostgreSQL tests above exercise the persisted governance workflow with the real application service.

The production-bundle browser regression also verified that a cleared required number field remains empty and blocks submission. The temporary PostgreSQL test server was stopped after verification, and no isolated test databases remain.

## Remaining boundary

Legal entities and reporting profiles require real authorized setup before use. External XBRL/taxonomy readiness remains blocked until the F09 verified registry exists. This phase does not migrate subscription pricing consumers, add penalty rates, produce filings, or implement later-phase financial policies.

There is no workflow in this phase to close an open-ended approved rule or automatically activate its successor. `EffectiveToUtc` is part of the pinned approved content, and `Superseded` versions continue to participate in overlap checks; configure successor windows as planned, non-overlapping periods until a dedicated replacement/activation workflow is delivered.
