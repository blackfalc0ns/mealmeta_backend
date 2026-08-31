# Mobile: Customer address map autofill

## Endpoints

- `POST /api/v1/maps/geocode` body: `{ "address": "...", "countryCode": "KW" }`
- `POST /api/v1/maps/reverse-geocode` body: `{ "latitude": 29.34, "longitude": 48.08 }`
- `GET /api/v1/catalog/locations?countryCode=KW`
- `GET|POST|PUT|DELETE /api/v1/customers/me/addresses`

Auth: maps endpoints are anonymous; address CRUD requires customer JWT.

## Map response → form fields

| Response field | Form field |
|---|---|
| `formattedAddress` | `addressLine` |
| `latitude` / `longitude` | `latitude` / `longitude` (required) |
| `governorate` | `governorate` |
| `area` | `area` |
| `block` | `block` |
| `street` | `street` |
| `building` | `building` |
| `suggestedRegionId` | `regionId` (allow manual override) |

Kuwait bounds required on save: lat `28.5–30.2`, lng `46.5–48.5`.

## Suggested Flutter flow

1. Places Autocomplete / pin / GPS  
2. Call geocode or reverse-geocode  
3. Fill controllers from response  
4. User confirms / edits  
5. `POST /api/v1/customers/me/addresses` with filled body  

## Google keys

- Mobile SDK key: Maps + Places (package / bundle restrictions)  
- Server key already used by backend geocode (do not embed server key in the app)
