# Restaurant Settings (Full Stack) — Design Spec

**Date:** 2026-08-22  
**Status:** Approved  
**Scope:** Backend + restaurant-dashboard frontend

## Goal

Professional restaurant settings page wired end-to-end with **real API data** (no mocks). Approved restaurants can propose profile changes; **no change applies until admin approves**.

## Architecture

- **Live data:** current `RestaurantRegistration` + `RestaurantProfile` (what the system uses today).
- **Draft data:** `RestaurantSettingsChangeRequest` with JSON payload — editable by restaurant, never applied directly.
- **Workflow:** Draft → Submitted → (Admin) Approved | NeedsChanges.
- On **Approve:** merge draft into registration + profile + service regions.
- On **NeedsChanges:** restaurant edits draft again and resubmits.

## API

| Method | Path | Role |
|--------|------|------|
| GET | `/api/v1/restaurants/settings` | Restaurant — live + draft + changeRequest + documents + notifications |
| PUT | `/api/v1/restaurants/settings/draft` | Restaurant — save draft payload |
| POST | `/api/v1/restaurants/settings/draft/submit` | Restaurant — submit for admin review |
| POST | `/api/v1/admin/restaurants/{id}/settings-changes/approve` | Admin — apply draft |
| POST | `/api/v1/admin/restaurants/{id}/settings-changes/request-changes` | Admin — reject with reason |

## Out of scope (later)

- Notification preference toggles (no API yet)
- Operations toggles (auto-busy, quiet hours)
- Password change from settings page

## Frontend

Route: `/restaurant/settings` (existing). Sections: account, business, location, bank, service regions, documents, review. Forms bind to **draft**; live shown as reference. Actions: Save draft, Submit for review.

## Constraints

- Customer subscription commission is admin-only — never editable here.
- All registration field validation reuses `RestaurantRegistrationRules` + existing validators.
- Only one open change request per restaurant (Draft | Submitted | UnderReview | NeedsChanges).
