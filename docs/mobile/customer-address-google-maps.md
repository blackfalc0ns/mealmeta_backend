# Mobile: Customer address map autofill

Complete contract for Flutter. Show **all** map + address fields in the UI after a place is selected (search / pin / GPS). Empty Google fields stay editable blanks.

---

## 1) APIs

| Method | Path | Auth |
|--------|------|------|
| `POST` | `/api/v1/maps/geocode` | Anonymous |
| `POST` | `/api/v1/maps/reverse-geocode` | Anonymous |
| `GET` | `/api/v1/catalog/locations?countryCode=KW` | Usually anonymous/public catalog |
| `GET` | `/api/v1/customers/me/addresses` | Customer JWT |
| `POST` | `/api/v1/customers/me/addresses` | Customer JWT |
| `PUT` | `/api/v1/customers/me/addresses/{addressId}` | Customer JWT |
| `DELETE` | `/api/v1/customers/me/addresses/{addressId}` | Customer JWT |

---

## 2) Geocode / reverse-geocode

### Request — reverse-geocode (بعد تأكيد الموقع على الخريطة)
```json
{
  "latitude": 29.339405795601476,
  "longitude": 48.08187089860439
}
```

### Response — اربطها على فورم عنوان المنزل كالتالي

| API field | حقل الموبايل |
|-----------|---------------|
| `suggestedGovernorateId` | dropdown المحافظة |
| `suggestedRegionId` | dropdown المنطقة |
| `suggestedBlockId` | dropdown القطعة (لو null سيبه للمستخدم) |
| `street` | الشارع |
| `building` | اختياري — ممكن للجادة أو المبنى |
| `formattedAddress` | addressLine / عرضوان الكامل |
| `latitude` / `longitude` | lat/lng (مطلوبين عند الحفظ) |
| `governorate` / `area` / `block` | نصوص للعرض فقط إن احتجت |

**الجادة:** Google غالبًا مش بيرجعها منفصلة — سيّبها اختيارية يدوي أو حط جزء من `street`/`building`.

```json
{
  "formattedAddress": "السالمية، الكويت",
  "latitude": 29.3390635,
  "longitude": 48.0817633,
  "governorate": "حولي",
  "area": "السالمية",
  "block": "10",
  "street": "شارع سالم المبارك",
  "building": null,
  "suggestedGovernorateId": "...",
  "suggestedGovernorateNameAr": "حولي",
  "suggestedGovernorateNameEn": "Hawalli",
  "suggestedRegionId": "...",
  "suggestedRegionNameAr": "السالمية",
  "suggestedRegionNameEn": "Salmiya",
  "suggestedBlockId": null,
  "suggestedBlockNameAr": null,
  "suggestedBlockNameEn": null
}
```

### Flutter بعد «تأكيد الموقع»
```dart
// pseudo
final place = await mapsApi.reverseGeocode(lat, lng);
governorateId = place.suggestedGovernorateId; // set dropdown
areaId = place.suggestedRegionId;             // set dropdown
blockId = place.suggestedBlockId;             // set dropdown if not null
streetCtrl.text = place.street ?? '';
avenueCtrl.text = place.building ?? '';       // optional
addressLineCtrl.text = place.formattedAddress;
latitude = place.latitude;
longitude = place.longitude;
```

لازم تستخدم الـ **IDs** مش الأسماء عشان الـ dropdowns تشتغل.

---

## 3) Create / update address body

`POST /api/v1/customers/me/addresses`  
`PUT /api/v1/customers/me/addresses/{addressId}`

```json
{
  "label": "المنزل",
  "addressLine": "السالمية، الكويت",
  "regionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "latitude": 29.340746,
  "longitude": 48.083898,
  "notes": "جنب السوبرماركت",
  "isDefault": true,
  "notifyOnDelivery": true,
  "governorate": "حولي",
  "area": "السالمية",
  "block": "10",
  "street": "شارع سالم المبارك",
  "building": "12",
  "unit": "5",
  "floor": "2",
  "blockRegionId": null
}
```

### Required on save
- `label`
- `addressLine`
- `latitude` + `longitude` inside Kuwait: lat `28.5–30.2`, lng `46.5–48.5`
- Prefer `regionId` from catalog (needed for checkout readiness)

### Optional
- `notes`, `governorate`, `area`, `block`, `street`, `building`, `unit`, `floor`, `blockRegionId`, `isDefault`, `notifyOnDelivery`

---

## 4) List / single address response fields (show all)

```json
{
  "id": "...",
  "label": "المنزل",
  "addressLine": "...",
  "regionId": "...",
  "latitude": 29.340746,
  "longitude": 48.083898,
  "notes": "...",
  "isDefault": true,
  "notifyOnDelivery": true,
  "governorate": "حولي",
  "area": "السالمية",
  "block": "10",
  "street": "...",
  "building": "...",
  "unit": "...",
  "floor": "...",
  "blockRegionId": null,
  "isDeliveryActive": true,
  "deliveryEffectiveFromUtc": null,
  "deliveryChangeNotice": {
    "ar": "...",
    "en": "..."
  }
}
```

Show delivery status to the user when present (`isDeliveryActive`, `deliveryEffectiveFromUtc`, `deliveryChangeNotice`).

---

## 5) Flutter screen checklist (كل دول تظهر)

After map pick, the form must render **all** of these (prefilled where possible):

1. Map (search + pin + my location)  
2. `label`  
3. `addressLine`  
4. `regionId` (catalog; default = `suggestedRegionId`)  
5. `blockRegionId` (optional)  
6. `governorate`  
7. `area`  
8. `block`  
9. `street`  
10. `building`  
11. `unit`  
12. `floor`  
13. `latitude` / `longitude` (can be read-only display)  
14. `notes`  
15. `isDefault`  
16. `notifyOnDelivery`  
17. Save  

Do **not** hide empty Google fields — leave them visible empty for the user to complete.

---

## 6) Flow

1. Places Autocomplete **or** pin **or** GPS  
2. `POST /maps/geocode` or `/maps/reverse-geocode`  
3. Fill every mapped controller from response  
4. User edits any field  
5. `POST/PUT .../customers/me/addresses`  
6. Refresh list and show 72h delivery notice if returned  

---

## 7) Google keys

| Key | Where |
|-----|--------|
| Mobile (new) | Flutter only — Maps SDK Android/iOS + Places; restrict by package/SHA-1 / bundle id |
| Server | Backend `GoogleMaps:ApiKey` / `GOOGLE_MAPS_SERVER_API_KEY` — Geocoding only |
| Browser | Admin/Restaurant dashboards — not used by Flutter |

Never put the server key inside the mobile app.

### Production troubleshooting (`maelmate.runasp.net`)

If `POST /api/v1/maps/reverse-geocode` returns **400**:

| `code` | Meaning | Fix |
|--------|---------|-----|
| `maps.api_key_missing` | Server key not configured on host | Set env `GOOGLE_MAPS_SERVER_API_KEY` (or `GoogleMaps__ApiKey`) on runasp and restart |
| `maps.geocode_failed` | HTTP call to Google failed | Check outbound HTTPS from host / billing |
| `maps.geocode_unresolved` | Google returned non-OK (e.g. `REQUEST_DENIED`) | Enable **Geocoding API** on the server key; IP restrictions must allow the host |
| `validation.invalid_format` (old builds) | Same issues before specific codes shipped | Redeploy latest API + set server key |

Local coords that work (for smoke test):
`{"latitude":29.339405795601476,"longitude":48.08187089860439}`
