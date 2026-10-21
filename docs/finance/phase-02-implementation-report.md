# F02 ledger and trial balance implementation report

## Delivered behavior

F02 adds a governed central-ledger foundation around the existing journal and chart-of-accounts entities. Manual adjustments and opening balances are persisted draft source documents with reasons, evidence references, exact KWD lines, maker-checker approval, content fingerprints, and optimistic row versions. A trusted posting factory reads only an approved source and creates the journal; there is no raw-lines posting endpoint. Reversal drafts refer to a posted original and target date, then the server copies its exact lines with debit and credit swapped.

Entity-scoped accounts and periods are configurable through API endpoints; no account codes, rates, or legal entities are seeded. Period close uses an optimistic version and shared database lock with posting. Closed periods are terminal for this release. The real trial-balance query includes posted governed journals only, reports opening/movement/closing and full-set totals, and includes close version plus a fingerprint over the complete ordered result. The UI verifies that fingerprint across pages before enabling export, so a changing open-period balance cannot silently mix snapshots. Legacy rows without legal-entity classification remain available for history but are excluded from governed totals.

The admin interface is discoverable at `/admin/finance/ledger`; it is guarded by ledger-view permission and has bilingual setup, period, source approval, journal detail/reversal, and trial-balance views. UI controls mirror separate manage, approve, and period-manage permissions while the API and application services enforce the permissions and tenant boundary.

## Accounting and integration boundaries

This release supplies an accountant-entered Adjustment/OpeningBalance workflow and a full-reversal workflow. It does not claim operational posting integration for payments, cancellation, subscription, or other product aggregates. It does not require a fabricated finance governance policy version for manual entries. A closed period cannot be reopened. Production account seeds and legal-entity provisioning remain environment setup.

The mapping experiment is a private XML format marked `TEST_ONLY` with local validation; it does not alter reporting readiness and is not an XBRL instance or filing. XBRL 2.1 section 4 requires a schema reference/DTS, context, unit, and XML Schema validity for a conforming instance. This release has no verified taxonomy/DTS and makes no external filing claim: [XBRL 2.1 Recommendation, section 4](https://www.xbrl.org/Specification/XBRL-2.1/REC-2003-12-31/XBRL-2.1-REC-2003-12-31%2Bcorrected-errata-2013-02-20.html).

The added migration is additive and follows the existing F01 migration. Its `Down` path refuses to remove governed ledger data. Integration verification runs against disposable PostgreSQL test databases; no live database migration was run.

The browser accepts source-line amounts only when their integer fils value is within JavaScript's safe-integer range (up to KWD 9,007,199,254,740.991). The server's `numeric(18,3)` storage range is wider; this UI does not promise exact entry or display across that entire range because JSON numeric values are parsed as JavaScript numbers.

## Verification

Focused verification completed successfully:

- `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter 'FullyQualifiedName~Finance' --artifacts-path F:\MealMate\outputs\finance-phase02-dotnet --disable-build-servers -m:1 --verbosity quiet -clp:ErrorsOnly` — 61 passed, 0 skipped.
- `dotnet test tests/MealMate.IntegrationTests/MealMate.IntegrationTests.csproj --filter 'FullyQualifiedName~Finance' --artifacts-path F:\MealMate\outputs\finance-phase02-dotnet --disable-build-servers -m:1 --verbosity quiet -clp:ErrorsOnly` — 8 passed, 0 skipped. Tests used the isolated PostgreSQL fixture and disposable databases.
- `dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter 'FullyQualifiedName~Finance' --artifacts-path F:\MealMate\outputs\finance-phase02-dotnet --disable-build-servers -m:1 --verbosity quiet -clp:ErrorsOnly` — 8 passed, 0 skipped, including real TestServer workflow and authorization checks against isolated PostgreSQL.
- `npm run typecheck` — passed.
- `npm run build` — passed (exit 0); existing budget and CommonJS warnings remain.
- `node scripts/i18n-check.cjs --self-test` — passed. Scoped catalog parity review found no missing locale keys; reviewed scanner false positives include translated `status` lookups and dictionary-key selections, plus the technical UUID placeholder and machine-format ISO date input. This is not a claim that the full repository has no i18n findings.
- `node outputs/finance-phase02-browser-review.cjs` — passed against the production bundle: loaded 501 rows over two pages; CSV contained 504 lines, three-decimal values, snapshot fingerprint and formula escaping; source review showed persisted lines; open dialog/error copy switched EN→AR; 390px viewport had no horizontal overflow; changed snapshots blocked export; load errors changed locale reactively; browser page errors: 0. Screenshot: `F:\MealMate\outputs\finance-phase02-dialog-ar.png`.
- Scoped production-path mock scan — 0 matches.

The XBRL boundary and caller inventory are documented in `docs/finance/discovery/F02-evidence.md`. Existing repository-wide warnings or unrelated concurrent edits are not represented as phase-specific passes.
