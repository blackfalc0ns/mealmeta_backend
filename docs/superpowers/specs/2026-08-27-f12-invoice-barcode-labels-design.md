# F12 — Invoice, Barcode & Labels (Admin Templates → −24h Generation)

**Date:** 2026-08-27  
**Status:** Approved for planning  
**Repos:** backend + admin-dashboard + restaurant-dashboard  
**Approach:** Snapshot persistence + browser print (no server PDF in v1)

## 1. Goal

At **−24h** before delivery, for each restaurant-confirmed subscription day (one box), the system generates:

1. **Box invoice** — full box invoice including a **single box barcode** used later by the driver at pickup and delivery.
2. **Box label** — sticker with full box data.
3. **Per-meal labels** — one sticker per meal with full meal data.

Admin designs and publishes **three template kinds**. Generation uses the **latest published** template of each kind. Restaurant prints in the browser from template layout + stored snapshots (same rendering approach as the existing admin designer).

## 2. Non-goals (v1)

- Server-side PDF file storage / PDF engine.
- Full driver scan workflow end-to-end (barcode value must exist and be unique; scan handlers can follow).
- Monthly finance / settlement invoices (separate product).
- Fine-grained permissions beyond existing `operations:view` (unless already required by admin ops routes).

## 3. Decisions locked with product

| Topic | Decision |
|-------|----------|
| Scope | Templates CRUD/publish **and** −24h generation |
| Documents | Box invoice + box label + meal labels |
| Barcode | **One barcode per box**; same value for print and driver scan |
| Template kinds | `invoice` · `box_label` · `meal_label` |
| Rendering | Persist snapshots; print PDF in browser |
| Logos | **MealMate + restaurant logo on all three** document types |
| Invoice privacy | Short delivery area (governorate/area/block); **no customer phone** |
| Label privacy | **No customer identity** on meal/box labels (align F12/F31) |
| Idempotency | Generate once per subscription day |
| Reissue | Update same rows; **keep same barcode** unless admin explicitly requests a new barcode |
| Missing published template | Job **logs error and skips** that day; no silent fallback template |

## 4. Architecture

```
Admin template designer
  → DocumentTemplates API (draft / publish / archive)
        ↓ published layouts (jsonb)
PrepDocumentsGenerator job (−24h window, ConfirmedByRestaurant)
  → MealBarcode (1 per SubscriptionDay)
  → MealInvoice (1 per SubscriptionDay / box)
  → MealLabel kind=box (1)
  → MealLabel kind=meal (N meals)
        ↓
Restaurant upcoming-24h + prep-documents API
  → browser print (template + snapshot + logos + barcode)
Driver (later)
  → scans MealBarcode.BarcodeValue
```

### Units

| Unit | Responsibility |
|------|----------------|
| `DocumentTemplateService` | Admin CRUD, publish, archive; load published by kind |
| `PrepDocumentsGenerator` | Idempotent −24h generation; assemble payloads; resolve logos |
| Restaurant prep read APIs | Expose generation flags + print payloads |
| Admin / restaurant UI | Persist templates; print from API data |

## 5. Data model

### 5.1 New: `DocumentTemplates`

| Column | Notes |
|--------|-------|
| Id | Guid |
| Kind | `invoice` \| `box_label` \| `meal_label` |
| NameAr / NameEn | |
| Language | `ar` \| `en` \| `both` (default `both`) |
| Status | `draft` \| `published` \| `archived` |
| PageWidthMm / PageHeightMm | |
| LayoutJson | elements array (reuse admin designer shape) |
| Version | increments on publish |
| PublishedAtUtc | null until published |
| Audit fields | standard |

Rules:

- At most **one published** template per `Kind` (publishing a new one archives or unpublishes the previous published of that kind).
- Required element types for all kinds: `mealmate_logo`, `restaurant_logo`.
- Invoice and box label must support `barcode` element bound to box barcode.
- Seed one published template per kind from current admin mock layouts (extended with logo elements).

### 5.2 Extend existing ops tables

**`MealBarcode`** (already exists)

- Keep `SubscriptionDayId`, `BarcodeValue`, `GeneratedAtUtc`.
- Ensure **unique** `BarcodeValue` and **unique** `SubscriptionDayId` (one barcode per box/day).
- Value format: stable scannable string (e.g. `MM-{yyyyMMdd}-{shortDayId}` or opaque token); must be unique and Code128-friendly.

**`MealInvoice`** (today: restaurant + service date batch)

- Add `SubscriptionDayId` (nullable for legacy rows; required for new generator rows).
- Add `TemplateId`, `TemplateVersion`.
- Add `BarcodeValue` denormalized copy for print convenience (optional but useful).
- `SnapshotJson` holds box invoice payload including logo URLs/keys resolved at generation time.
- `InvoiceNumber` unique per generation.
- Clarify: **v1 invoice is per box (subscription day)**, not daily restaurant batch. Batch aggregation is out of scope.

**`MealLabel`**

- Add `Kind`: `box` \| `meal`.
- Add `MealSelectionId` or `MealId` (nullable; required when `Kind = meal`).
- Add `TemplateId`, `TemplateVersion`.
- `LabelPayloadJson` holds sticker payload + resolved logo keys/URLs.

## 6. Payloads (snapshot content)

### Shared

- `mealMateLogoUrl` / storage key  
- `restaurantLogoUrl` / storage key  
- `restaurantName` (ar/en)  
- `serviceDate`  
- `orderCode` / subscription day id  
- `barcodeValue` (invoice + box label; optional on meal label for ops consistency — **print barcode primarily on invoice and box label**)

### Box invoice

- Line items: each meal name, slot, qty  
- Delivery short address: governorate / area / block only  
- Totals / box summary fields as template supports  
- **No phone number**

### Box label

- Full box prep data: meals list, slots, restaurant, service date, barcode  
- Logos required  
- **No customer name/phone**

### Meal label

- Meal name (ar/en), slot, nutrition, allergens, ingredients/notes as available  
- Logos required (small in layout)  
- **No customer identity**

## 7. APIs

### Admin — templates

Base: `/api/v1/admin/operations/document-templates` (already declared in admin endpoints)

| Method | Path | Behavior |
|--------|------|----------|
| GET | `/` | List (filter by kind/status) |
| POST | `/` | Create draft |
| GET | `/{id}` | Detail |
| PUT | `/{id}` | Update draft (or create new draft version from published — prefer edit draft only; published immutable except archive) |
| POST | `/{id}/publish` | Publish; unpublish previous of same kind |
| POST | `/{id}/archive` | Archive |
| POST | `/{id}/reissue-prep/{subscriptionDayId}` | Optional admin reissue for one day (keep barcode by default; `?newBarcode=true` to rotate) |

Auth: existing admin operations permission.

### Restaurant — consumption

| Method | Path | Behavior |
|--------|------|----------|
| GET | `/api/v1/restaurants/orders/upcoming-24h` | **Change:** set `InvoiceGenerated` / `BarcodeGenerated` / `LabelsGenerated` from DB rows, not index math |
| GET | `/api/v1/restaurants/orders/{id}/prep-documents` | Return invoice snapshot, box label, meal labels, barcode, and template layouts used (or current published if layout embedded in snapshot) |

Prefer **embedding template layout snapshot** inside each document snapshot at generation time so later template edits do not change already-printed legal copies.

### Notifications (v1 minimal)

- On successful generation: SignalR (or existing restaurant notifier) to pending/upcoming section — reuse pattern from −72h send if available.
- On failure / missing template: structured log + optional admin notification if infrastructure exists; otherwise log is enough for v1 with metric/count in job summary.

## 8. −24h job

- Runs periodically (same style as other subscription jobs).
- Selects `SubscriptionDay` where:
  - Status = `ConfirmedByRestaurant` (or later statuses that still need docs if not generated)
  - `LockAtUtc` / service timeline implies now ≥ serviceDate−24h and docs not yet generated
- For each day: transactional create barcode + invoice + labels if not present.
- If any required published template missing: skip day, log error with day id + missing kinds.
- If meal nutrition incomplete: still generate label with available fields; flag `incompleteNutrition` in payload for restaurant UI (align F12 exception).

## 9. Frontend

### Admin

- Wire `InvoiceLabelsStore` to HTTP templates API (remove in-memory as source of truth).
- Extend `DocumentTemplateKind` with `box_label` and `meal_label` (replace single `label` or map legacy `label` → `box_label` during migration of mock).
- Ensure designer supports mandatory MealMate + restaurant logo elements.
- Add sidebar nav entry under operations → `/admin/operations/invoices`.

### Restaurant

- Wire labels/print page to `prep-documents` API.
- Upcoming-24h badges follow real flags.

## 10. Error handling

| Case | Behavior |
|------|----------|
| No published template for a kind | Skip generation for that day; log |
| Day not confirmed | Do not generate |
| Already generated | No-op (idempotent) |
| Reissue | Overwrite snapshots; keep barcode unless `newBarcode=true` |
| Logo missing | Use null/placeholder URL in snapshot; still generate; UI shows empty logo slot |

## 11. Testing

- Publish template; list returns one published per kind.
- Publishing second invoice unpublishes/archives previous.
- Generator creates 1 barcode + 1 invoice + 1 box label + N meal labels.
- Second generator run does not duplicate.
- Missing published `meal_label` → skip + error log.
- Upcoming-24h flags true only when rows exist.
- Snapshots include both logo fields for all three docs.
- Invoice snapshot has no phone field.
- Unit tests for payload builders; integration test for migrate + generate against Postgres when Docker available.

## 12. Implementation order

1. Domain + migration: `DocumentTemplates` + Meal* extensions  
2. Admin template APIs + seed published defaults  
3. Wire admin UI store + kinds + nav  
4. `PrepDocumentsGenerator` job + payload builders  
5. Fix upcoming-24h flags + `prep-documents` API  
6. Wire restaurant labels print page  
7. Tests + docs (`workflows` already describe F12; add mobile/restaurant API note if needed)

## 13. Open follow-ups (explicitly later)

- Driver scan against `MealBarcode`
- Server PDF archive
- Admin confirmation-overdue → replacement window (separate from F12)
- Stricter document-template permissions
