# F02 ledger and trial balance evidence

## Existing source inventory

- The existing finance/governance API and rules resolve reporting requirements; they are not posting sources and are not consulted as an invented policy for manual adjustments.
- Existing central accounting `JournalEntry`, `JournalEntryLine`, and `ChartOfAccount` remain the journal/COA foundations. Legacy null-entity accounting rows remain readable and are excluded from governed entity totals.
- The only new posting inputs in this phase are persisted `Adjustment`, `OpeningBalance`, and `Reversal` source documents. The first two require maker-checker approval. Reversal input contains only the original journal reference, target business date, reason, and evidence; the server derives exact swapped lines.
- The posting API receives a source-document ID. It never accepts a caller-provided journal, lines, event ID, period ID, or authority to fabricate an operational event. Payments, cancellations, subscriptions, and other operational owners remain future posting-source integrations.
- Finance outbox handlers are registered for the existing lifecycle and generic dispatch paths. Ledger-specific poison/malformed messages throw so the dispatcher retains its retry/failure behavior; unrelated notification event types are ignored by these handlers.

## Safety and report boundary

- A canonical source-content fingerprint and row version are pinned on submit and approval. A different administrator must approve. Posting reloads the approved document after period and economic-identity locks, derives lines from that document, and atomically persists inbox delivery, journal, source link, economic identity, audit, and outbox records.
- Economic identity is independent of delivery event ID. Same-effect deliveries return the persisted journal; different content for the same identity conflicts. A complete reversal is unique and retains the original posted journal unchanged.
- The period lifecycle is Open → PendingClose → Open (reasoned cancellation) or Closed. Closed periods are terminal in this phase. Period/date fields are immutable; close and posting use the same period advisory lock and CloseVersion.
- PostgreSQL triggers/constraints enforce governed row/line immutability, tenant-consistent references, and deferred journal balance/currency/entity invariants for direct SQL paths. EF guards cover tracked header, lines, owned Money, source, period, account, and audit state changes. SQL `CHECK` constraints validate row values only; final multi-line balance is enforced by a deferred constraint trigger at transaction end.
- Trial balance is one aggregate query over posted governed rows, carries the period close version and a SHA-256 fingerprint of the full ordered result, checks opening + movement = closing per account, and reports totals over all rows. The UI compares the fingerprint and all snapshot metadata across pages before assembling or exporting; a changed snapshot is rejected. CSV export uses the same complete result and includes close version and fingerprint.
- Journal/source detail includes the original-journal ID for reversals, submitted maker/checker identities, evidence reference, and the reviewed line content; the maker and reviewer can see the saved scope and content before approval.
- The internal mapping experiment is explicitly `TEST_ONLY`, has its own private XML namespace/shape and local validator, and does not call or change external readiness. It is not an XBRL instance or filing. XBRL 2.1 section 4 requires an applicable DTS and `schemaRef`, contexts, units, and XML Schema validity for an XBRL instance; no verified taxonomy/DTS is introduced here: [XBRL 2.1 Recommendation, section 4](https://www.xbrl.org/Specification/XBRL-2.1/REC-2003-12-31/XBRL-2.1-REC-2003-12-31%2Bcorrected-errata-2013-02-20.html).

## Verification evidence

Integration tests use the existing `PostgresTestDatabase` helper and create disposable `mealmate_test_*` databases only. The tests apply the complete migration chain and exercise SQL behavior; no live application database was migrated. The isolated database credential is supplied through the process environment and is intentionally omitted from this record.

Covered PostgreSQL scenarios include schema migration and safe Down/Up, legacy null-entity rows, direct SQL balanced posting and invalid posted-line changes, cross-entity FK rejection, EF owned-money mutation rejection, ten concurrent first deliveries with varied event IDs, an observed advisory-lock wait across pending close, rollback after injected outbox failure, exact full reversal, trial-balance opening/movement/closing, and replay after period close.

The API TestServer exercises the real services and isolated PostgreSQL: unauthenticated denial, view-only denial for commands, maker submission and self-approval denial, stale fingerprint/version conflicts, independent checker approval, persisted source processing and journal detail, cross-tenant journal denial, and restaurant-role denial despite supplied ledger permission claims.

Focused final verification completed with the commands below (the PostgreSQL fixture received its connection through the process environment; credentials are not recorded here):

- `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter 'FullyQualifiedName~Finance' --artifacts-path F:\MealMate\outputs\finance-phase02-dotnet --disable-build-servers -m:1 --verbosity quiet -clp:ErrorsOnly` — 61 passed, 0 skipped.
- `dotnet test tests/MealMate.IntegrationTests/MealMate.IntegrationTests.csproj --filter 'FullyQualifiedName~Finance' --artifacts-path F:\MealMate\outputs\finance-phase02-dotnet --disable-build-servers -m:1 --verbosity quiet -clp:ErrorsOnly` — 8 passed, 0 skipped.
- `dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter 'FullyQualifiedName~Finance' --artifacts-path F:\MealMate\outputs\finance-phase02-dotnet --disable-build-servers -m:1 --verbosity quiet -clp:ErrorsOnly` — 8 passed, 0 skipped.
- `npm run typecheck` and `npm run build` — passed; production build exited 0 with existing budget/CommonJS warnings.
- `node scripts/i18n-check.cjs --self-test` — passed. Scoped parity was clean. The reviewed scanner findings were false positives for already translated status/dictionary selections, the technical UUID placeholder, and machine ISO date formatting; no full-repository i18n cleanliness claim is made.
- `node outputs/finance-phase02-browser-review.cjs` — production bundle browser checks passed: full 501-row/two-page export, 504-line CSV with snapshot fingerprint and formula escaping, persisted source-line review, reactive EN/AR dialog and error copy, snapshot-change export block, zero 390px overflow, zero page errors. Screenshot: `F:\MealMate\outputs\finance-phase02-dialog-ar.png`.
- Scoped production-path mock scan — 0 matches.

The source-entry UI is bounded to values whose integer fils fit JavaScript's safe-integer range (KWD 9,007,199,254,740.991). Server `numeric(18,3)` supports larger values, but JSON numeric parsing in a JavaScript client cannot guarantee exact fils above that bound; the UI is not claimed to cover the full server numeric range. These checks do not include a live application database migration or an external XBRL filing.
