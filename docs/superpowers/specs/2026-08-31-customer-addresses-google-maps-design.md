# Customer Addresses + Google Maps Design

**Date:** 2026-08-31  
**Status:** Approved for implementation  
**Scope:** Backend maps enrichment + customer address coordinate validation; mobile API contract

## Decisions

- UX: Places search + map pin + live GPS; MealMate catalog `regionId` still used for delivery eligibility.
- Coordinates: required on create/update; must be inside Kuwait bounds (lat 28.5–30.2, lng 46.5–48.5).
- Resolution: mobile resolves place first via Maps APIs, then POSTs/PUTs address with filled fields.
- Map result must auto-fill form fields: `addressLine`, `governorate`, `area`, `block`, `street`, `building`, lat/lng, and suggested `regionId`.

## Backend

### Maps APIs (existing routes)

`POST /api/v1/maps/geocode`  
`POST /api/v1/maps/reverse-geocode`

Response expands to:

```json
{
  "formattedAddress": "string",
  "latitude": 0,
  "longitude": 0,
  "governorate": "string|null",
  "area": "string|null",
  "block": "string|null",
  "street": "string|null",
  "building": "string|null",
  "suggestedRegionId": "guid|null",
  "suggestedRegionNameAr": "string|null",
  "suggestedRegionNameEn": "string|null"
}
```

- Parse Google `address_components` into structured fields.
- Suggest nearest active `RegionType.Area` with centroid within ~20 km (prefer name match when possible).
- Remain `[AllowAnonymous]` for onboarding/customer flows.

### Customer addresses

`POST/PUT /api/v1/customers/me/addresses`

- Require `latitude` and `longitude`.
- Reject when outside Kuwait bounds (`validation.required_field` / `validation.invalid_format`).
- Do not change 72h default/active delivery rules.
- Server does not geocode on save; validates client-provided coords.

## Mobile contract

1. Configure Google Maps/Places SDK keys (Android/iOS restrictions).
2. On place select / pin / GPS → call geocode or reverse-geocode.
3. Bind response into form fields; set `regionId` from `suggestedRegionId` when present (allow manual override via `GET /api/v1/catalog/locations?countryCode=KW`).
4. Save via existing address endpoints with required lat/lng.
5. Show `isDeliveryActive`, `deliveryEffectiveFromUtc`, `deliveryChangeNotice` after save.

## Out of scope

- Flutter app implementation in this repo
- Server-side geocode during address create
- Changing checkout rules beyond requiring valid coords on saved addresses
