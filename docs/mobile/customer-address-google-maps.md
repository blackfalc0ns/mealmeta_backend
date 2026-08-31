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

### ترتيب فورم عنوان المنزل (بعد تأكيد الموقع)

| # | حقل الموبايل | API field | من الخريطة؟ |
|---|--------------|-----------|-------------|
| 1 | المحافظة | `suggestedGovernorateId` | نعم |
| 2 | المنطقة | `suggestedRegionId` | نعم |
| 3 | القطعة | `suggestedBlockId` | نعم (لو null سيبه للمستخدم) |
| 4 | الشارع | `street` | نعم |
| 5 | الجادة (اختياري) | **`avenue`** | نعم (منفصلة عن البناء) |
| 6 | رقم المنزل / البناية | `building` | **لا — يدوي** |
| 7 | الدور (اختياري) | `floor` | **لا — يدوي** |
| 8 | رقم الشقة (اختياري) | `unit` | **لا — يدوي** |

كمان اربط: `formattedAddress` → addressLine، و `latitude` / `longitude` للحفظ.

**الجادة ≠ البناء:** لو Google رجّع `شارع X جادة Y` كحقل واحد، الـ API بيفصلها → `street` + `avenue`. `building` / `floor` / `unit` تفضل `null` من الـ maps عشان المستخدم يكتبهم.

```json
{
  "formattedAddress": "17-27 شارع 2 جادة 2، السالمية، الكويت",
  "latitude": 29.3412132,
  "longitude": 48.082421,
  "governorate": "حولي",
  "area": "السالمية",
  "block": "قطعة 5",
  "street": "شارع 2",
  "avenue": "جادة 2",
  "building": null,
  "suggestedGovernorateId": "...",
  "suggestedGovernorateNameAr": "حولي",
  "suggestedGovernorateNameEn": "Hawalli",
  "suggestedRegionId": "...",
  "suggestedRegionNameAr": "السالمية",
  "suggestedRegionNameEn": "Salmiya",
  "suggestedBlockId": "...",
  "suggestedBlockNameAr": "قطعة 5",
  "suggestedBlockNameEn": "Block 5"
}
```

### Flutter بعد «تأكيد الموقع»
```dart
// pseudo — same order as the home-address form
final place = await mapsApi.reverseGeocode(lat, lng);
governorateId = place.suggestedGovernorateId;
areaId = place.suggestedRegionId;
blockId = place.suggestedBlockId;
streetCtrl.text = place.street ?? '';
avenueCtrl.text = place.avenue ?? '';   // optional — NOT building
// building / floor / unit stay empty for the user
buildingCtrl.clear();
floorCtrl.clear();
unitCtrl.clear();
addressLineCtrl.text = place.formattedAddress;
latitude = place.latitude;
longitude = place.longitude;
```

**اللغة:** endpoints الـ maps بترجع أسماء العنوان بالعربي حتى لو الـ app باعت `Language: en` (فورم الكويت عربي). الشارع/الجادة لو Google رجّعهم English بيتتحويلوا لـ `شارع` / `جادة`.

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
  "street": "شارع 2",
  "avenue": "جادة 2",
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
- `notes`, `governorate`, `area`, `block`, `street`, `avenue`, `building`, `unit`, `floor`, `blockRegionId`, `isDefault`, `notifyOnDelivery`

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
  "avenue": "...",
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

## 5) Flutter screen checklist (ترتيب الفورم)

After map confirm (`تأكيد الموقع`), bind in this order:

1. المحافظة → `suggestedGovernorateId`  
2. المنطقة → `suggestedRegionId`  
3. القطعة → `suggestedBlockId`  
4. الشارع → `street`  
5. الجادة → `avenue` (اختياري، منفصل عن البناء)  
6. رقم المنزل / البناية → `building` (**يدوي**)  
7. الدور → `floor` (**يدوي**)  
8. رقم الشقة → `unit` (**يدوي**)  
9. `latitude` / `longitude` + `formattedAddress` / `label` / `notes` / `isDefault` / `notifyOnDelivery`  
10. Save  

Do **not** hide empty fields — leave building/floor/unit visible empty for the user.

---

## 6) Flow

1. Places Autocomplete **or** pin **or** GPS  
2. `POST /maps/geocode` or `/maps/reverse-geocode`  
3. Fill every mapped controller from response (except building/floor/unit)  
4. User completes building / floor / apartment and edits any field  
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
