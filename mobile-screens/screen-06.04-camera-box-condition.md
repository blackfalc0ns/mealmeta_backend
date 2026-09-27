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
  "storageKey": "uploads/drivers/delivery/pickup-condition/7ea85f64-5717-4562-b3fc-2c963f66af10/2fa85f64-5717-4562-b3fc-2c963f66af11/photo.jpg",
  "contentType": "image/jpeg",
  "sizeBytes": 245812,
  "uploadedAtUtc": "2026-09-27T08:02:00Z",
  "scanStatus": "PhotoUploaded",
  "nextAction": "ConfirmPickup"
}
```

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
```json
{
  "tripId": "7ea85f64-5717-4562-b3fc-2c963f66af10",
  "boxId": "2fa85f64-5717-4562-b3fc-2c963f66af11",
  "boxCode": "480K-1234",
  "status": "PickedUp",
  "pickedUpAtUtc": "2026-09-27T08:03:00Z",
  "assignedBoxesCount": 8,
  "validatedBoxesCount": 8,
  "receivedBoxesCount": 8,
  "remainingBoxesCount": 0,
  "allBoxesReceived": true,
  "nextAction": "ShowPickupSummary"
}
```

- **التوجيه حسب نتيجة `nextAction`:**
  * إذا كانت `allBoxesReceived == false` (`nextAction: "ShowBoxSuccess"`): يتم توجيه السائق إلى كارت النجاح `06.15` للعودة ومسح البوكس التالي.
  * إذا كانت `allBoxesReceived == true` (`nextAction: "ShowPickupSummary"`): يتم توجيه السائق مباشرة إلى شاشة ملخص اكتمال الاستلام `06.05`.

