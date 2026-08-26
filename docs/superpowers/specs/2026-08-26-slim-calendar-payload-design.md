# Design Spec: Slim Calendar Payload + Day Detail

**Date:** 2026-08-26  
**Status:** Draft for user review  
**Audience:** Backend + Flutter  
**Related:** `docs/mobile/individual-mobile-gaps/calendar/02-day-selection-api.md`, `CustomerCalendarController`, `CustomerCalendarEngine`

## 1. Problem

`GET /api/v1/customer/subscriptions/{subscriptionId}/calendar` returns every subscription day with full `selectedMeals` payloads (ingredients, descriptions, allergen lists, macros). For a ~26-day subscription this is large on the wire and expensive to deserialize on mobile. The server also parses full restaurant `MealsJson` snapshots per day just to build those selected-meal DTOs.

## 2. Goal

Keep the month calendar light for mobile and server, while preserving a full day payload when the customer opens a day or after a mutation.

**Success criteria**

- Default calendar response omits ingredients, descriptions, and allergen arrays on selected meals.
- Opening one day can load the full day DTO (current shape) without reloading the whole month.
- Mutation endpoints that already return a single day keep returning the **full** day DTO (no Flutter break for post-select refresh).
- Mobile gap docs document the new day-detail route and the slim calendar contract.
- Unit/API tests cover slim list vs full day.

## 3. Non-goals

- No `?from=&to=` windowing in this change (can be added later).
- No change to restaurant list, meal menu, slot select, complete, or auto-select business rules.
- No Flutter app code changes in this backend task (docs only for mobile).
- No DB schema migration.

## 4. Approaches considered

| Option | Idea | Trade-off |
|--------|------|-----------|
| A | `?date=` only | Light for one day; month grid needs many round-trips or loses overview |
| B (chosen) | Slim `GET /calendar` + full `GET /days/{date}` | One light month load; detail on demand |
| C | `?detail=full` on calendar | Easy flag; default still must be slim; easy to misuse and reload heavy month |

**Recommendation:** B.

## 5. API design

Base: `/api/v1/customer/subscriptions/{subscriptionId}`

### 5.1 `GET /calendar` (slim)

Returns `CustomerCalendarScreenDto` with days as **summary** day DTOs.

Each day keeps existing top-level fields:

- `id`, `serviceDate`, `restaurantId`, `restaurantNameAr`/`restaurantNameEn` (collapsed per i18n middleware), `restaurantLogoUrl`
- `status`, `uiStatus`, `isAutoSelected`, `canEdit`
- `selectionDeadlineUtc`, `lockAtUtc`, `lockRemainingSeconds`
- `filledCount`, `requiredCount`, `slotRequirements`
- `failureReasonCode`

`selectedMeals[]` becomes **summary meals** only:

| Field | Include |
|-------|---------|
| `mealId` | yes |
| `nameAr` / `nameEn` (+ collapsed `name`) | yes |
| `slot`, `slotNameAr`, `slotNameEn` | yes |
| `sortOrder` | yes |
| `imageUrl` | yes |
| `isSelected` | yes (always `true` for calendar selections) |
| `calories`, `proteinG`, `carbsG`, `fatG` | **no** |
| `descriptionAr` / `descriptionEn` | **no** |
| `presentAllergenCodes`, `mayContainAllergenCodes`, `dietaryPreferenceCodes` | **no** |
| `ingredients` | **no** |
| `blockReason` | **no** |

Rationale for keeping name/image/slot: month/day chips can show what was picked without a second request; cost is small vs ingredients.

### 5.2 `GET /days/{date}` (full day detail) — **new**

- `{date}` = `YYYY-MM-DD`
- Auth: same as calendar (customer owner / family calendar access gate)
- 404 if day not on subscription calendar (`Calendar.DayUnavailable`)
- Response: existing **full** `CustomerCalendarDayDto` (same shape as today after `POST .../meals/slot`)

### 5.3 Mutations (unchanged response type)

These continue to return **full** `CustomerCalendarDayDto`:

- `POST /days/{date}/restaurant`
- `POST /days/{date}/meals/slot`
- `POST /days/{date}/complete`
- `POST /days/{date}/auto-select`

Menu endpoints unchanged:

- `GET /days/{date}/restaurants`
- `GET /days/{date}/restaurants/{restaurantId}/meals`
- legacy `GET /days/{date}/meals/available`

### 5.4 Mobile flow (updated)

```text
1) GET .../calendar                    → month grid (slim selectedMeals)
2) Open day                            → GET .../days/{date} if full selected-meal detail needed
3) Pick restaurant / meals             → existing restaurants + meals + slot endpoints
4) After slot/complete                 → use returned full day DTO (or re-GET days/{date})
5) Back to month                       → optionally patch local day from mutation; else refresh slim calendar
```

## 6. Backend shape

### 6.1 DTOs (`CustomerCalendarContracts.cs`)

- Add `CustomerCalendarMealSummaryDto` with the slim meal fields only.
- Add `CustomerCalendarDaySummaryDto` mirroring `CustomerCalendarDayDto` but `SelectedMeals` is `IReadOnlyList<CustomerCalendarMealSummaryDto>`.
- Change `CustomerCalendarScreenDto.Days` to `IReadOnlyList<CustomerCalendarDaySummaryDto>`.
- Keep `CustomerCalendarDayDto` + `CustomerCalendarMealDto` for full day and mutations.
- Add `GetCustomerCalendarDayQuery(Guid SubscriptionId, DateOnly ServiceDate) : IQuery<CustomerCalendarDayDto>`.

### 6.2 Engine / handlers / controller

- `GetCalendarAsync`: map days with summary meal mapper (no ingredients/description/allergens/macros).
- `GetDayAsync(subscriptionId, date)`: reuse existing `MapDayAsync` full mapper for one day.
- Register query handler; add `[HttpGet("days/{date}")]` on `CustomerCalendarController` **before** more specific `days/{date}/...` routes is fine in ASP.NET (literal segments win); place it with the other day GETs.
- **Server cost (this slice):** when building calendar summaries, map only selected meal rows to summary fields after locating each selected meal in the day’s meal list — do not attach full ingredient graphs. Optional follow-up (out of scope unless cheap): avoid parsing unused restaurant catalogs during calendar-only GET; not required for first ship if response size is fixed.

### 6.3 i18n

No special case: existing bilingual collapse middleware applies to `nameAr`/`nameEn` and restaurant names the same way.

## 7. Docs to update

- `docs/mobile/individual-mobile-gaps/calendar/02-day-selection-api.md` — add day detail row; note slim calendar.
- `docs/mobile/individual-calendar/01-individual-calendar-api.md` (if it documents calendar payload fields) — align.
- This spec remains the design source of truth for the change.

## 8. Testing

- Unit: calendar screen DTO / mapping asserts selected meals lack ingredients (and related heavy fields); day-by-date query returns ingredients when present in snapshot.
- Existing mutation tests keep expecting full meal DTO where they already assert meal detail.
- Prefer extending `CustomerCalendarHandlerTests` / `MobileCalendarContractsTests` rather than new project.

## 9. Compatibility / risk

- **Breaking for Flutter** if the app assumes `selectedMeals[].ingredients` on the month calendar response. Document clearly; day detail + menu endpoints still provide full data. Mutations still return full day.
- Prefer additive day route over changing mutation contracts.

## 10. Implementation order

1. DTOs + query contract  
2. Engine slim map + `GetDayAsync`  
3. Handler + controller route  
4. Tests  
5. Mobile gap / calendar API docs  

---

## Spec self-review

- No TBD/TODO placeholders left for required behavior.
- Slim vs full boundaries are explicit (calendar list vs day GET vs mutations).
- Scope is one backend feature; no Flutter code in this plan.
- Ambiguity resolved: month grid **keeps** meal name/image/slot summaries; strips nutrition/allergens/ingredients/description only.
