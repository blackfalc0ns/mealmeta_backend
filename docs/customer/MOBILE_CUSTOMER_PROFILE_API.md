# دليل الموبايل: حساب العميل والبروفايل

Base URL محليًا: `http://localhost:5073`  
Production: استخدم رابط الـ API المخصص للبيئة.

كل endpoints في هذا الدليل، ما عدا التسجيل وتسجيل الدخول، تحتاج التوكن:

```http
Authorization: Bearer <accessToken>
Content-Type: application/json
```

## 1. البروفايل الأساسي

### جلب بيانات المستخدم الحالي

```http
GET /api/v1/customers/me
```

مثال Response:

```json
{
  "userId": "b149c799-0f6d-4a38-b36f-19e4a1c0df95",
  "fullName": "أحمد محمد",
  "email": null,
  "phoneNumber": "+96590000000",
  "preferredLanguage": "ar",
  "defaultRegionId": null,
  "deliveryAddressLine": null,
  "deliveryLatitude": null,
  "deliveryLongitude": null,
  "dateOfBirth": null,
  "onboardingCompleted": true,
  "profileImageStorageKey": "uploads/customers/profile/avatar.png",
  "accountStatus": "Active"
}
```

### تعديل البروفايل

```http
PUT /api/v1/customers/me
```

```json
{
  "fullName": "أحمد محمد",
  "preferredLanguage": "ar",
  "defaultRegionId": null,
  "deliveryAddressLine": "العنوان النصي المختصر",
  "deliveryLatitude": 29.3759,
  "deliveryLongitude": 47.9774,
  "dateOfBirth": "1996-04-12"
}
```

أرسل `null` لأي قيمة اختيارية لا تريد حفظها.

## 2. صورة البروفايل

رفع الصورة يتم على خطوتين. لا ترسل الصورة مباشرة إلى endpoint البروفايل.

### الخطوة 1: رفع الملف

```http
POST /api/v1/uploads
Content-Type: multipart/form-data
Authorization: Bearer <accessToken>
```

Fields:

| Field | Value |
| --- | --- |
| `file` | ملف الصورة (`jpg`, `png`, `webp`...) |
| `scope` | `customers` |
| `folder` | `profile` |

مثال Response:

```json
{
  "storageKey": "uploads/customers/profile/6f22b5-avatar.png",
  "fileName": "avatar.png",
  "contentType": "image/png",
  "sizeBytes": 18420,
  "readUrl": "/api/v1/uploads/content?key=uploads%2Fcustomers%2Fprofile%2F6f22b5-avatar.png",
  "folder": "profile",
  "scope": "customers"
}
```

### الخطوة 2: ربط الصورة بالبروفايل

```http
POST /api/v1/customers/me/profile-image
```

```json
{
  "storageKey": "uploads/customers/profile/6f22b5-avatar.png"
}
```

استخدم `readUrl` العائد من الرفع لعرض الصورة. عند استبدال الصورة يحذف السيرفر الصورة السابقة تلقائيًا.

### حذف الصورة

```http
DELETE /api/v1/customers/me/profile-image
```

Response: `204 No Content`.

## 3. العناوين

العناوين منفصلة عن البروفايل لأن العميل يمكن أن يملك أكثر من عنوان. استخدم `isDefault: true` لتعيين عنوانه الأساسي.

### جلب كل العناوين

```http
GET /api/v1/customers/me/addresses
```

### إضافة عنوان

```http
POST /api/v1/customers/me/addresses
```

```json
{
  "label": "المنزل",
  "addressLine": "قطعة 4، شارع 10، منزل 25",
  "regionId": "area-guid-from-location-catalog",
  "blockRegionId": "block-guid-optional",
  "governorate": "محافظة الأحمدي",
  "area": "الفحيحيل",
  "block": "4",
  "street": "10",
  "building": "25",
  "unit": "منزل",
  "floor": "الدور الأول",
  "latitude": 29.0802,
  "longitude": 48.1307,
  "notes": "خلف المسجد",
  "isDefault": true,
  "notifyOnDelivery": true
}
```

`regionId` لازم يكون **المنطقة (Area)** من `GET /api/v1/locations?countryCode=KW`، مش المحافظة.

- لو بعتّ GUID محافظة + `area` بالاسم، السيرفر يحوّله للمنطقة.
- لو بعتّ GUID قطعة (Block) في `regionId`، السيرفر ياخد المنطقة الأب ويحفظ القطعة في `blockRegionId`.
- `address.area_invalid` = الـ GUID مش موجود أو مش منطقة/قطعة/محافظة صالحة.
- `address.area_required` = بعتّ محافظة من غير منطقة.

### تعديل أو حذف عنوان

```http
PUT /api/v1/customers/me/addresses/{addressId}
DELETE /api/v1/customers/me/addresses/{addressId}
```

استخدم نفس body الإضافة مع `PUT`. حذف العنوان يرجع `204 No Content`.

## 4. تغيير كلمة السر

```http
PUT /api/v1/customers/me/password
```

```json
{
  "currentPassword": "OldPassword123",
  "newPassword": "NewPassword123"
}
```

كلمة السر الجديدة يجب أن تكون 8 أحرف على الأقل وتحتوي حروفًا وأرقامًا. بعد النجاح تُلغى جلسات المستخدم السابقة، لذلك سجّل دخوله من جديد.

## 5. التفضيلات الغذائية

```http
GET /api/v1/customers/me/food-preferences
PUT /api/v1/customers/me/food-preferences
```

مثال حفظ:

```json
{
  "dietaryPattern": "نباتي",
  "allergies": [
    {
      "code": "nuts",
      "nameAr": "مكسرات",
      "nameEn": "Nuts",
      "severity": "high"
    }
  ],
  "dislikedFoods": [
    {
      "code": "mushroom",
      "nameAr": "فطر",
      "nameEn": "Mushroom",
      "severity": null
    }
  ],
  "notes": "بدون صلصات حارة"
}
```

## أخطاء شائعة

| Status | المعنى في الموبايل |
| --- | --- |
| `400` | بيانات ناقصة أو غير صالحة؛ اعرض `detail` من Problem Details. |
| `401` | التوكن غير صالح أو انتهت الجلسة؛ جدّد الجلسة أو أعد تسجيل الدخول. |
| `403` | الحساب ليس حساب عميل أو ليس مسموحًا له بالعملية. |
| `404` | العنوان أو الملف المطلوب غير موجود. |

