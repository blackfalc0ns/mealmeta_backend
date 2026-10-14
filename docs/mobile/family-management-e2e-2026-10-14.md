# Family management backend HTTP E2E — 2026-10-14

The focused test is `MealMate.ApiTests.FamilyManager.FamilyManagementFlowPostgresHttpTests`. It uses `OrderLifecycleEndToEndFixture`: the actual API `Program`, dependency injection, JWT authentication, PostgreSQL migrations and persisted assertions. Test-only clock, payment gateway and push notifier registrations come from the shared fixture. The scenario seeds the fixture's real restaurant/menu catalog, then adds an isolated, already-funded family subscription with persisted batch, member and calendar snapshot records. The test does not perform checkout or charge a gateway.

Run it from the backend directory with:

```powershell
./scripts/run-family-management-e2e.ps1
```

The runner checks Docker before starting, filters to the focused test, writes a TRX report under the ignored `TestResults/family-management-e2e` directory, and fails when no tests ran or any test failed.

The HTTP scenario checks family home, member list/detail, basic profile edit with nutrition/activity preservation, preference save/read, member creation with persisted default profile fields, manager pause/unpause access, outsider denial with Arabic/English/Arabic localized details, calendar/day and restaurant/meal reads, subscription delivery-address retrieval, incomplete-day rejection, restaurant and meal selection, completion, confirmation and replay. While access is paused, the manager still selects the restaurant; the outsider is denied both calendar read and edit. The day contains one lunch slot, and database reads after replay assert the selected meal and restaurant each occur once, the day is `CustomerSelected`, and no restaurant order was created before the deadline.

This is a one-member, one-day funded slice for the family-management and calendar paths. It does not cover the payment purchase/renewal journey, member deletion, longer calendar cycles or Flutter presentation. The screen mapping is:

| Mobile screen ID | Backend behavior covered |
|---|---|
| 08.01 home | Family home and wallet-unavailable state |
| 08.02 members | Member list |
| 08.03 detail | Member detail and resolved subscription summary |
| 08.20 access | Pause/unpause; manager calendar action while paused |
| 08.21 permissions | Untouched; no permissions contract or policy was changed |
| M08.22 member edit/preferences | Basic profile edit, preference save/read and member addition |
| 08.04 calendar | Calendar read |
| 08.06 day | Day read and incomplete-day rejection |
| 08.07 restaurants | Restaurant list and selection |
| 08.09 meals | Available meal read and slot selection |
| 08.10 complete+confirm | Completion, confirmation, reload and idempotent replay |

Several mobile fields differ from the current backend contract and need explicit handling in the client: the access screen uses mobile/generated-password behavior rather than a manager email/password form; member edit sends `ageYears` rather than `dateOfBirth`; the wallet is unavailable; and an upcoming-only member subscription is reported through `upcomingSubscriptionId` rather than an active `memberSubscriptionId`. The backend response should not invent an active subscription for that state. These are contract notes, not Flutter verification.

The family calendar resolver uses the funded subscription's verification timestamp and service interval. The selected service date is at least five days ahead of the fixed test clock so it remains editable beyond the 72-hour lock. Delivery address is read through `/api/v1/customer/subscriptions/{id}/delivery-address`, independently of the day DTO.

The calendar's pre-fulfillment restriction and disabled wallet capability remain deliberate product behavior. Payment creation in the shared fixture uses a test gateway service, so this E2E does not establish that an external payment was charged. A Flutter project is not present in this backend checkout; no mobile runtime verification is claimed here.

## Live release acceptance — separately funded subscriptions

The live release check is separate from the isolated pending-payment fixture above. The primary reports two already funded subscriptions on the live server, each verified/completed (`Status = 4`), starting 2026-10-17 with six persisted service days. After this change is deployed, family home/member summary should resolve those subscriptions as actual active service, report `Completed` fulfillment, and show six remaining days from their real calendar rows. They must not appear as `PaidPreparing` or as awaiting payment. This is the expected post-deployment result; the local PostgreSQL fixture does not cover those live records, and a post-deployment live response has not been checked by this worktree.

## Release worktree verification — 2026-10-14

On the clean `be9afd6` release base, the scoped Release unit command passed 234/234 with no build warnings or errors. Its TRX is `TestResults/family-management-release/family-management-release-unit-20261014.trx`. The PostgreSQL HTTP runner passed 1/1; its TRX is `TestResults/family-management-e2e/family-management-e2e.trx`. `dotnet publish src/MealMate.Api/MealMate.Api.csproj --configuration Release --no-restore --output artifacts/release-family-management` completed successfully and produced the API deployment build at `artifacts/release-family-management`. The primary independently confirmed the scoped 234/234 result, HTTP 1/1 result, clean diff check, and API publish output. These checks validate the local release build; they do not verify the live records described above.

The future-only regression models the observed customer response where a paid subscription starts in the future, `memberSubscriptionId` is null, and `upcomingSubscriptionId` is populated. Home, list and detail now project the persisted upcoming subscription for lifecycle and summary data while retaining a null current subscription ID. Remaining days come from its actual future calendar rows. Detail does not offer renewal or expose today's nutrition for that future-only service. The regression also verifies paid service cannot display `ReadyToPay` or `SetupIncomplete`, even when a newer unpaid checkout batch exists for the member. The fix is local to this checkout and has not been deployed or applied to the live API.

Additional verification on 2026-10-14: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~Paid_future_only_member_uses_upcoming_service_lifecycle_across_management_reads' -v:q` passed 4/4 (Ready and InProgress setup crossed with pending and completed fulfillment). Reverting only the detail remaining-days projection to CurrentId-only made all 4 cases fail; restoring the UpcomingId fallback returned them to green. `./scripts/run-family-management-e2e.ps1` passed 1/1 against PostgreSQL, including assertions for all three management reads. The HTTP regression seeds a separately funded future-only member and leaves the existing editable-day journey intact. No live API request was changed by this local verification.

Verification on 2026-10-14: the focused unit suite `FamilyDraftExpiryTests` passed 5/5. The primary independently reran `./scripts/run-family-management-e2e.ps1` and passed 1/1 against PostgreSQL. The primary's scoped unit run for `FamilyManager|CustomerCalendar|FamilyCheckout|FamilyLifecycle|FamilyDelivery|FamilyDraftExpiry` passed 211/211; its TRX report is `family-management-primary-final-20261014.trx`. Separate PostgreSQL integration tests passed 3/3: funding notification replay, family renewal preserving historical funded service, and the manager removal/customer cancellation race crediting the payer once while preserving the locked day. Their TRX report is `tests/MealMate.IntegrationTests/TestResults/family-management-primary-finance-20261014.trx`. These are separate scenarios, not one HTTP sequence, and they use test gateway behavior without establishing an external charge. The focused persistence regression was also run with the old destructive assignments temporarily restored; it failed because the stored goal became empty, then passed after restoring the fix. The production change preserves nutrition goal/activity on update and initializes those fields only when adding a member. The HTTP E2E creates the funded subscription directly in the isolated database; neither these tests nor the unit suite verifies the full Flutter experience.

Final primary verification on 2026-10-14: an independent source-build run of `./scripts/run-family-management-e2e.ps1` passed 1/1; output is `artifacts/family-future-primary-http-20261014.log`. A no-build scoped unit run against the latest compiled artifact passed 215/215; TRX is `family-future-primary-built-artifact-20261014.trx`. Two full unit source-build attempts failed on concurrent, unrelated Advertising compiler errors (`CS0411` in `RestaurantAdvertisingCommands.cs:449` and `AdAuctionNextBidderOfferCommands.cs:35`). Therefore, no full workspace build is claimed. The primary confirmed compiled Application and test DLL timestamps were later than the changed handler source. Permissions were not changed, and the changed production files passed the suspicious-term scan.
