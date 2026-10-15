# Mobile advertising preview seed

Verified on 2026-10-14 against the user-selected `mealmate_db` development server: 4 preview offers, 1 linked campaign, 1 active sponsored placement at `offers.hero`, and 0 payment records for the preview auction. The uploaded JPEG returned HTTP 200. A second successful seed run preserved these counts without duplicate fixtures. The API build and dry-run succeeded; the mobile screen itself was not inspected. No database migrations were applied by this command.

Mock Data Check: synthetic visual fixtures were explicitly requested by the user and are opt-in development seed data only. No runtime fake fallback responses, provider payment records, or financial charges were introduced; the API reads persisted fixtures and the real storage asset.

`--seed-mobile-advertising-preview` creates or refreshes only tagged offers, one linked shared-savings campaign, and one sponsored placement marked with `MM-MOBILE-AD-PREVIEW`. It is a visual development fixture. Its awarded auction state does not prove payment and does not create bids, payment attempts, provider transactions, charges, or financial ledger entries. Do not use the seeded placement to validate payment or settlement flows. Reruns refresh only tagged preview rows and preserve offer usage counts and redemptions.

The command runs only with `ASPNETCORE_ENVIRONMENT=Development` and requires `--confirm-database <name>` to match the configured database name. The target check happens before the app's migration block; this command never applies migrations or starts broad seeding. Keep the selected database an isolated development/test database.

PowerShell example (replace the database name with the already approved target):

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/src/MealMate.Api -- --seed-mobile-advertising-preview --confirm-database '<configured-database>' --asset-file '<repo-owned-public-jpeg>' --placement-key offers.hero --customer-id <existing-customer-user-id>
```

Append `--dry-run` to validate prerequisites and print fixture counts without saving changes. The database confirmation is required for dry runs too.

The customer selector is optional when the placement market is unrestricted. For a region-scoped placement, provide an existing customer user ID whose default address has a matching `RegionId`. Supply a repo-owned JPEG between 1 KB and 25 MB. The seeder requires an existing active program and bundle and an operational restaurant. It creates a tagged preview placement and an unapproved development policy only when the requested placement key is unused; it never changes an existing ad space or policy. An overlapping active placement on a reused key stops the command before writes. Missing prerequisites stop before writes. It creates no customers, restaurants, programs, or bundles.

On first run, the JPEG is uploaded through the configured `IFileStorage` provider and recorded as owned by an existing SuperAdmin. Reruns reuse that preview placement's stored asset key while it remains available. Offer banner URLs use a 30-day storage read URL and are refreshed each time the command runs. The sponsored placement stores the verified storage key so the mobile ads query obtains its normal current read URL. Public HTTP(S) storage URLs are required; local filesystem paths are rejected. The mobile client must request the same `--placement-key` value. The mobile API still requires its normal authenticated customer context and default address to return ads.
