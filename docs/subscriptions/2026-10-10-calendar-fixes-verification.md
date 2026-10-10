# Calendar and meal selection fixes — 2026-10-10

Scope: the four confirmed issues in `2026-10-10-calendar-meal-selection-audit.md`, on the existing `dev` branch. Unrelated AdminCoverage, voice and migration work was preserved.

Implemented:

- Restaurant menus and availability use the requested service date only. Missing dated snapshots disable the restaurant and return `calendar.box_not_ready`; selection returns a known error rather than throwing.
- Automatic recommendation uses enabled candidates. The current restaurant cannot override busy/quota restrictions when an eligible alternative exists. Saved boxes also pass current eligibility before the automatic shortcut. Existing no-alternative policy relaxation does not bypass coverage, allergy or capacity checks.
- Customer completion/confirmation checks current eligibility; automatic cutoff can replace a saved restaurant that lost coverage, using the existing transactional capacity transfer. The dispatch service returns a Result and checks actual delivery region coverage after the address snapshot, before status, order or notification writes. Failed dispatch is propagated to the caller.
- Replacement days persist `FailureReasonCode`. Calendar detail and summary return the actual code; the lock job and restaurant failure path preserve it in escalation, and outbox payloads include `ReasonCode`. Inbox/push copy has Arabic/English reasons for capacity, coverage, missing menu, allergy-safe box, busy restaurant and repetition limit. FCM data includes the stored reason code.
- Independent review identified disabled automatic generation masking invalid saved-box failures. A failing regression reproduced it; the fix preserves the disabled generation policy and escalates actual saved coverage/menu failures.
- Mobile handoff now documents date-specific availability, actual replacement reasons, push metadata and additional acceptance scenarios.

Schema: `20261010173000_AddSubscriptionDayFailureReasonCode` adds a nullable varchar(120) column and updates EF snapshot/target model. Legacy days without a stored reason retain the previous `calendar.no_safe_box` API fallback. No cause was fabricated or backfilled. Existing startup migration execution is enabled by default outside Testing (`Database:ApplyMigrationsOnStartup`); deployment must allow the migration to complete before serving the new build. Production DB changes were not applied manually in this task.

Fresh verification after final code changes:

| Check | Result |
|---|---|
| Full UnitTests | 2048 passed, 0 failed/skipped |
| Scoped ApiTests: CustomerCalendar, CustomerCheckout, OrderLifecycle | 244 passed, 0 failed/skipped |
| PostgreSQL Capacity integration suite | 16 passed, 0 failed/skipped |
| i18n audit self-test | Passed |
| i18n diagnostic scan | 1161 files; 56 parity objects; 0 parity mismatches; 0 parser diagnostics |

The PostgreSQL suite applies migrations and exercises competing capacity transfers and freeze replacement; four added cases reload stored replacement reasons through real PostgreSQL and check detail/summary consistency, including nullable legacy compatibility. The API scope includes the previously failing GoldenPath lifecycle and four Arabic/English failure-resource cases. Unit regressions were observed failing against the old behavior before fixes.

Commands, from the backend directory:

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --artifacts-path F:/MealMate/outputs/calendar-fixes-tests --nologo --verbosity quiet
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --artifacts-path F:/MealMate/outputs/calendar-fixes-api --nologo --filter 'FullyQualifiedName~CustomerCalendar|FullyQualifiedName~OrderLifecycle|FullyQualifiedName~CustomerCheckout' --verbosity quiet
dotnet test tests/MealMate.IntegrationTests/MealMate.IntegrationTests.csproj --artifacts-path F:/MealMate/outputs/calendar-fixes-integration --nologo --filter 'FullyQualifiedName~Capacity' --verbosity quiet
```

From the MealMate root: `node scripts/i18n-audit.cjs --self-test` and `node scripts/i18n-audit.cjs`. The diagnostic inventory contains existing legacy classifications; zero parity mismatches is not proof that all UI copy is translated. Existing unrelated compiler warnings remain.

Limits: no Flutter changes, live language switching, real payment, hosted scheduler/outbox/FCM, full API suite or hosted deployment validation. Backend test success does not certify the entire live mobile flow 100%.

Mock Data Check:
- No mock business data introduced.
- No fake fallback responses introduced.
- No hardcoded production business values introduced.
- All runtime data comes from real application sources.

Synthetic fixture data remains confined to test projects.
