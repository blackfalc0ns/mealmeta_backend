# Restaurant Prep Documents API (F12)

Generated at **−24h** for restaurant-confirmed subscription days (one box per day).

## Endpoints

### List prep queue
`GET /api/v1/restaurants/orders/upcoming-24h`

Flags are **DB-backed**:
- `invoiceGenerated` — `MealInvoices` row for the day
- `barcodeGenerated` — `MealBarcodes` row for the day
- `labelsGenerated` — box label present **and** meal label count matches meal selections

### Print payload
`GET /api/v1/restaurants/orders/{subscriptionDayId}/prep-documents`

Returns:
- `barcodeValue` — single box barcode (driver scan value)
- `invoice` — snapshot + embedded layout
- `boxLabel` — snapshot + embedded layout
- `mealLabels[]` — one per meal

Each part:
| Field | Meaning |
|-------|---------|
| `kind` | `invoice` \| `box_label` \| `meal_label` |
| `snapshotJson` | Full payload including logos, meals, barcode, `embeddedLayoutJson` |
| `layoutJson` | Convenience extract of embedded layout |

## Admin templates

`/api/v1/admin/operations/document-templates`

Kinds: `Invoice`, `BoxLabel`, `MealLabel` — one **published** template per kind.

### Admin reissue
`POST /api/v1/admin/operations/prep-documents/{subscriptionDayId}/reissue?newBarcode=false`

## Generation job

`PrepDocumentsGenerationJob` every 5 minutes. Window: `SelectionDeadlineUtc + 48h` (or `ServiceDate` UTC midnight − 24h). Skips if any published template is missing.

## Privacy

- Invoice: short address (governorate/area/block) only — no phone
- Labels: no customer identity
- Logos: MealMate + restaurant on all three document types
