# Screen 06.04: كاميرا توثيق حالة البوكس (Camera Box Condition Documentation)

## 🎯 الغرض من الشاشة ونظرة عامة (Overview & Purpose)
تُمثّل هذه الشاشة خطوة التوثيق الفوتوغرافي الإلزامية لحالة تغليف كل صندوق على حدة قبل مغادرة المطبخ. تضمن الشاشة حماية السائق وتثبت سلامة العبوة والوجبات الداخلية في سجلات النظام (`pickup_scans`).

---

## 🎨 التحليل البصري والتخطيطي (UI/UX Breakdown)

```
┌────────────────────────────────────────────────────────┐
│ 9:41                   تأكيد استلام الطلب      📶 🔋  │
├────────────────────────────────────────────────────────┤
│ 📸 تصوير البوكس (480K-1234)                           │
│ التقط صورة واضحة لتوثيق حالة البوكس قبل الاستلام        │
│                                                        │
│ ┌────────────────────────────────────────────────────┐ │
│ │                                                    │ │
│ │             ┌──────────────────────┐               │ │
│ │             │      MealMate        │               │ │
│ │             │   [ بوكس الوجبة ]    │               │ │
│ │             └──────────────────────┘               │ │
│ │                                                    │ │
│ │                 [ 📷 زر التقاط ]                   │ │
│ └────────────────────────────────────────────────────┘ │
│                                                        │
│ 💡 نصيحة: تأكد من وضوح شريط الإغلاق وسلامة التغليف     │
│                                                        │
│                     [ تأكيد الاستلام ]                 │
│                                                        │
├────────────────────────────────────────────────────────┤
│   [الرئيسية]   [ 🟣 الطلبات ]   [الخريطة]   [الدعم]   [الحساب] │
└────────────────────────────────────────────────────────┘
```

---

## 🔌 تكامل الخادم (API Integration)

تتم عملية التوثيق والتأكيد عبر خطوتين أمنيتين متتاليتين تضمنان عدم التلاعب بالصور والربط الصارم برمز التحقق:

### الخطوة 1: رفع صورة حالة التغليف (Upload Condition Photo)
- **الرابط:** `POST /api/v1/driver/pickup/boxes/{boxId}/condition-photo`
- **نوع المحتوى:** `multipart/form-data`
- **الصلاحية:** `Bearer Token` (`Driver`)
- **الحقول:**
  - `file`: ملف الصورة (الأنواع المسموحة: JPEG, PNG, WebP — الحجم الأقصى: 10 MiB مع التحقق الصارم من التوقيع الرقمي للملف `magic bytes`).
  - `validationToken`: رمز التحقق الصادر من الشاشة `06.03`.
- **الاستجابة الناجحة (`HTTP 200 OK`):**
```json
{
  "boxId": "2fa85f64-5717-4562-b3fc-2c963f66af11",
  "conditionPhotoStorageKey": "uploads/drivers/delivery/pickup-condition/7ea85f64-5717-4562-b3fc-2c963f66af10/2fa85f64-5717-4562-b3fc-2c963f66af11/photo.jpg",
  "storageKey": "uploads/drivers/delivery/pickup-condition/7ea85f64-5717-4562-b3fc-2c963f66af10/2fa85f64-5717-4562-b3fc-2c963f66af11/photo.jpg",
  "contentType": "image/jpeg",
  "sizeBytes": 245812,
  "uploadedAtUtc": "2026-09-27T08:02:00Z",
  "scanStatus": "PhotoUploaded",
  "nextAction": "ConfirmPickup"
}
```

#### أكواد أخطاء رفع الصورة:
| كود الخطأ | الحالة | السبب |
| :--- | :---: | :--- |
| `PHOTO_REQUIRED` | 400 | ملف الصورة مفقود أو فارغ |
| `PHOTO_INVALID_TYPE` | 400 | الامتداد غير مدعوم (المسموح JPEG, PNG, WebP) |
| `PHOTO_INVALID_SIGNATURE` | 400 | محتوى الملف لا يطابق الصيغة (فحص Magic Bytes) |
| `PHOTO_TOO_LARGE` | 400 | حجم الملف يتجاوز الحد المسموح (10 ميجابايت) |
| `VALIDATION_TOKEN_INVALID` | 400 | رمز التحقق غير صالح لهذا البوكس |
| `VALIDATION_TOKEN_EXPIRED` | 400 | انتهت مهلة الـ 5 دقائق، يجب إعادة مسح الباركود |
| `VALIDATION_TOKEN_CONSUMED` | 400 | تم استخدام رمز التحقق بالفعل مسبقاً |

---

### الخطوة 2: تأكيد استلام البوكس (Confirm Pickup)
- **الرابط:** `POST /api/v1/driver/pickup/boxes/{boxId}/confirm`
- **الصلاحية:** `Bearer Token` (`Driver`)
- **الهيدر الإلزامي لمنع التكرار:** `Idempotency-Key: <UUID>`
- **Body:**
```json
{
  "validationToken": "c3f87a9b0e1d4c2a...",
  "conditionPhotoStorageKey": "uploads/drivers/delivery/pickup-condition/7ea85f64-5717-4562-b3fc-2c963f66af10/2fa85f64-5717-4562-b3fc-2c963f66af11/photo.jpg",
  "latitude": 29.3375,
  "longitude": 48.0280
}
```

- **الاستجابة الناجحة (`HTTP 200 OK`):**
*(يُرجع السيرفر كلاً من التسميات الحديثة والقديمة لضمان التوافق التام)*
```json
{
  "tripId": "7ea85f64-5717-4562-b3fc-2c963f66af10",
  "boxId": "2fa85f64-5717-4562-b3fc-2c963f66af11",
  "boxCode": "480K-1234",
  "status": "PickedUp",
  "pickedUpAtUtc": "2026-09-27T08:03:00Z",
  "totalBoxesCount": 8,
  "assignedBoxesCount": 8,
  "validatedBoxesCount": 8,
  "pickedUpBoxesCount": 8,
  "receivedBoxesCount": 8,
  "remainingBoxesCount": 0,
  "allBoxesPickedUp": true,
  "allBoxesReceived": true,
  "nextAction": "ShowPickupSummary"
}
```

#### أكواد أخطاء تأكيد الاستلام:
| كود الخطأ | الحالة | السبب |
| :--- | :---: | :--- |
| `IDEMPOTENCY_KEY_REQUIRED` | 400 | الهيدر `Idempotency-Key` مفقود |
| `IDEMPOTENCY_KEY_REUSED` | 409 | إعادة استخدام نفس الـ Key مع معطيات مختلفة |
| `LOCATION_REQUIRED` | 400 | إحداثيات GPS مفقودة أو مساوية لصفر |
| `VALIDATION_TOKEN_INVALID` | 400 | رمز التحقق غير صالح |
| `VALIDATION_TOKEN_EXPIRED` | 400 | انتهت مهلة رمز التحقق |
| `VALIDATION_TOKEN_CONSUMED` | 400 | تم استهلاك رمز التحقق مسبقاً |
| `PHOTO_NOT_UPLOADED` | 400 | لا توجد صورة مؤكدة مطابقة للـ storageKey مع التوكن |
| `BOX_ALREADY_PICKED_UP` | 409 | البوكس تم استلامه وتأكيده بالفعل |

- **منطق التوجيه (Routing Logic):**
  * إذا كانت `allBoxesPickedUp == false` (أو `remainingBoxesCount > 0`): `nextAction: "ShowBoxSuccess"` ➔ الانتقال لكارت النجاح والعودة لمسح البوكس التالي.
  * إذا كانت `allBoxesPickedUp == true` (أو `remainingBoxesCount == 0`): `nextAction: "ShowPickupSummary"` ➔ الانتقال لشاشة ملخص اكتمال الاستلام `06.05`.

