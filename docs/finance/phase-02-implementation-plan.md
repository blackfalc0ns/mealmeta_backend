# F02 ledger and trial balance implementation plan

**Baseline:** frontend/root HEAD `396ff104b79f7488fa7f6e286de9d1d76aa200ae`; backend HEAD `f06a03ee2795c38d08f5e9fd66da8e6530ae12ec` at start of this work. Preserve the existing worktree, including F01, cancellation/family, and concurrent capacity changes. No checkout/reset/clean/commit/push or live migration.

## ADR-FIN-0002: approved source documents are the only posting input

Manual adjustments and opening balances begin as persisted draft source documents containing entity, business date, currency, reason, evidence reference, and proposed lines. The creator submits an immutable content fingerprint; a different checker approves with the expected fingerprint and row version. Approval, audit, and source outbox are atomic. A trusted server factory then reads only the approved document and derives lines internally. `PostOnceAsync` accepts a document ID and server-controlled event identity; there is no public raw-lines posting endpoint. Full reversals are source documents that reference one original journal and target date; the factory derives exact swapped original lines. This phase does not fabricate payment, cancellation, or subscription posting integrations, nor require a guessed governance RuleVersion for a manual adjustment.

## Sequential implementation slices

1. **Domain and persistence foundation:** add accounting periods, economic posting identities, source documents/links, reversal records, ledger enums and invariants. Extend the existing COA/journal entities compatibly; preserve unclassified legacy rows for read-only history and exclude them from governed totals. Add composite entity constraints, indexes, append-only guards, precise amount checks, and additive migrations.
2. **Posting and approval:** persist source draft/submit/approve with maker-checker and canonical fingerprint. Build trusted line factories, then atomically process Inbox + journal + source link + posting identity + audit + financial outbox. Implement stable economic identity and conflict-on-changed-content semantics. Period locks precede posting-identity locks.
3. **Reversal and periods:** approve a reversal source through a separate checker and derive its exact lines from the immutable original. Add Open/PendingClose/Closed period lifecycle with optimistic CloseVersion, shared posting/close lock, reasoned PendingClose cancellation, and no reopen workflow.
4. **Queries and API:** real posted-only trial balance/detail queries and permission/entity-scoped API. Never accept caller-provided journal lines or PeriodId to select a posting period. Add the TEST_ONLY internal trial-balance mapping/validator PoC without changing external readiness.
5. **Tests, UI, evidence:** RED-first unit/real-PostgreSQL/API coverage, then the bilingual ledger page backed only by real API responses, and the evidence/report documents.

## Authorized paths

- Domain: `src/MealMate.Domain/CentralAccounting/{JournalEntry.cs,JournalEntryLine.cs,ChartOfAccount.cs}`; new `AccountingPeriod.cs`, `PostingIdentity.cs`, `JournalReversal.cs`, `JournalSourceDocument.cs`, `JournalSourceLink.cs`, `LedgerEnums.cs`, `JournalInvariant.cs`; additive finance fields only in `src/MealMate.Domain/Common/InboxProcessedEvent.cs`.
- Application: new `src/MealMate.Application/Features/Finance/Ledger/{LedgerContracts.cs,LedgerAccess.cs,JournalSourceService.cs,PostingSourceFactory.cs,PostingService.cs,JournalReversalService.cs,PeriodLifecycleService.cs,TrialBalanceQuery.cs,LedgerQueries.cs,LedgerSourceOutboxHandler.cs,InternalTrialBalancePoc.cs}`; existing `src/MealMate.Application/{DependencyInjection.cs,Common/Abstractions/IAppDbContext.cs,Common/Abstractions/IApplicationLockManager.cs,Common/Security/PermissionCodes.cs}`.
- Persistence: `src/MealMate.Persistence/{AppDbContext.cs,Common/PostgresApplicationLockManager.cs,Configurations/CentralAccounting/CentralAccountingConfigurations.cs,Configurations/Common/InboxProcessedEventConfiguration.cs,Migrations/AppDbContextModelSnapshot.cs}`; new `Configurations/CentralAccounting/FinanceLedgerConfiguration.cs`, additive `Migrations/*Phase02LedgerFoundation*` (designer if generated); optional new `src/MealMate.Persistence/FinanceLedgerPersistenceGuard.cs`.
- API: new `src/MealMate.Api/Controllers/V1/FinanceLedgerController.cs`; existing `src/MealMate.Api/Resources/Resources.Messages.{resx,ar.resx}`.
- Tests: only `tests/MealMate.UnitTests/Finance/Ledger/**`, `tests/MealMate.IntegrationTests/Finance/Ledger/**`, `tests/MealMate.ApiTests/Finance/Ledger/**`.
- Docs: `docs/finance/phase-02-implementation-plan.md`, `phase-02-implementation-report.md`, `discovery/F02-evidence.md`.
- Frontend, after backend review: only `F:/MealMate/src/app/features/admin/finance/{data/finance-ledger-api.service.ts,models/finance-ledger.model.ts,pages/finance-ledger-page/finance-ledger-page.component.{ts,html,css}}`, `src/app/core/i18n/translations/finance-ledger.i18n.ts`, and the existing `src/app/{features/admin/admin-stub.routes.ts,core/auth/admin-permissions.ts,core/navigation/admin-nav.config.ts}`.

## Acceptance checklist

- [ ] Entity-scoped COA with same-entity parents, partial uniqueness preserving legacy global codes, no seeds/guessed rates, effective/postable/active checks, and control-account party requirements.
- [ ] Exact positive KWD 3-decimal lines, at least two lines, one debit or credit each, no mixed currencies, balanced in fils, numeric bounds, and no bypass via owned Money or direct SQL. Governed Posted journals and lines are append-only. Document and test DB balance enforcement separately from row checks.
- [ ] Immutable approved source content and maker-checker; trusted persisted source only; economic identity independent of EventId and handler version; payload conflict on changed content; one transaction for inbox, journal, link, identity, audit and outbox; no fake-success poison handling.
- [ ] Period then posting-identity locking, nonoverlapping periods, Open/PendingClose/Closed with CloseVersion, shared close/post lock, period selection by business date, and consistent close watermark. No operational aggregate locks in the consumer.
- [ ] Full immutable-original reversal only, unique under retries/concurrency, separately approved, exact swapped values, and target open period when original is closed.
- [ ] Trial balance from posted governed journals only, entity/currency scoped, correct opening/movement/closing and globally balanced totals, with drafts/legacy unclassified rows excluded and no fabricated records.
- [ ] Application/service/API authorization requires authenticated ADMIN identity plus entity scope (or SuperAdmin); restaurant users denied even with a forged permission. Real HTTP tests exercise service and isolated PostgreSQL.
- [ ] TEST_ONLY internal mapping/validation PoC never promotes external readiness or claims a filing.
- [ ] RED-first unit, PostgreSQL race/migration/immutability, and actual API tests. Safe migration Down refuses to delete posted data. Rerun F01 Finance tests and relevant regressions; use only isolated PostgreSQL fixtures.
- [ ] Bilingual real-API UI, permission-aware controls, semantic reactive errors, Kuwait dates, KWD precision, empty/loading/error states, and export from the same full result/CloseVersion.

## Known boundaries

Do not alter or regenerate existing migrations, especially mixed F01/cancellation migrations. Do not modify unrelated capacity, cancellation, family, legacy reports, manifests, or the shared test fixture. Do not run a database build/migration/test until the parent releases the currently running baseline suite and privately provisions the isolated PostgreSQL connection. A close/reopen workflow, operational-source integrations, external taxonomy/filing, and production account seeds remain out of scope.

The early XBRL work is only an internal `TEST_ONLY` mapping/validation experiment. It is not an XBRL instance or filing unless it includes a verified taxonomy/DTS and `schemaRef`, contexts, units, and XML Schema validation required by the [XBRL 2.1 Recommendation, section 4](https://www.xbrl.org/Specification/XBRL-2.1/REC-2003-12-31/XBRL-2.1-REC-2003-12-31%2Bcorrected-errata-2013-02-20.html). F09 owns the verified taxonomy registry; this phase must not alter external readiness.
