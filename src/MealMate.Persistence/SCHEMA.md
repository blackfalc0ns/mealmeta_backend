# MealMate Database Schema

Generated for Master Blueprint v3.1 + Pricing Amendment MM-PRC-AMD-01.

## Stack
- PostgreSQL (PostGIS image via `docker-compose.yml`)
- EF Core 9 + Npgsql + Identity
- Guid PKs, UTC timestamps, soft-delete on `AuditableEntity`, append-only on financial/snapshot tables (`BaseEntity` only)

## Migrations
1. `20260627230937_InitialIdentity` — ASP.NET Identity tables
2. `20260723170549_M00_FoundationThroughM12_DomainSchema` — full domain schema (Foundation + M01…M12)

Apply:
```bash
docker compose up -d
dotnet ef database update --project src/MealMate.Persistence --startup-project src/MealMate.Api
```

Dev connection (see `appsettings.Development.json`): `localhost:5433` (Docker) to avoid clashing with any local Postgres on `5432`.

Migrations:
1. `20260627230937_InitialIdentity` — ASP.NET Identity tables
2. `20260723170549_M00_FoundationThroughM12_DomainSchema` — full domain schema (Foundation + M01…M12)

| Module | Domain folder | Key tables |
|---|---|---|
| Foundation | `Foundation/` | Countries, Regions, RuleVersions, Documents, DocumentVerifications, IdempotencyRecords |
| M01 | `IdentityAccess/` | Permissions, RolePermissions, AccessScopes, UserScopes, *Profiles, *Registrations, AuditLogs |
| M02 | `ProgramsSubscriptions/` | Programs, Bundles, RestaurantBundlePricings (dual price), Classifications, Subscriptions, Snapshots, Promo/Referral |
| M03 | `CalendarMeals/` | SubscriptionDays, Selections, Allergies, Freezes, LimitSnapshots |
| M04 | `RestaurantOperations/` | Menus, Meals, Capacities, DailyOrders, Barcodes, Labels, Ratings |
| M05 | `DriverDelivery/` | Trips, Assignments, Scans, Tracking, Holds, DriverRatings |
| M06 | `ComplaintsSupport/` | Complaints, Evidence, Responsibility, Compensations, Tickets |
| M07 | `CentralAccounting/` | ChartOfAccounts, JournalEntries, Payables, Settlements, Gateway configs |
| M08 | `CustomerFinance/` | Cash/Promo wallets, WalletTransactions, Payments, Cancellations, Refunds |
| M09 | `RestaurantFinance/` | MealPayables (supply-based), Commissions, MonthlyInvoices, Payouts |
| M10 | `Influencers/` | Codes, CommissionCalculations, Withdrawals, PayoutHistory |
| M11 | `CollaborativeCampaigns/` | Campaigns, DiscountSplits (funding modes), Enrollments, AccountingEntries |
| M12 | `AdminDashboard/` | SystemSettings, ProfitabilityAlerts, AdminNotifications |

## Conventions
- **Money**: owned columns `*_Amount decimal(18,3)` + `*_CurrencyCode varchar(3)`
- **Percentage**: `decimal(9,6)` fraction (0.15 = 15%)
- **Dual pricing (AMD-01)**: `ReferencePrice26` vs `ContractedSupplyPrice26` on `RestaurantBundlePricings`
- **Campaign funding**: `CampaignFundingMode` = MealMateFull | RestaurantFull | Shared
- Configurations live under `Persistence/Configurations/<Module>/` and are auto-applied
