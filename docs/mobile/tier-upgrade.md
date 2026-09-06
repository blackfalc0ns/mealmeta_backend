# ترقية تصنيف المطاعم (موبايل Flutter)

عقد الـ API لشاشات **ترقية التصنيف** (Select → Plus → Max / Basic → Platinum → Elite)، مع نقطة النهاية المنفصلة لملخص الترقية، مقارنة المطاعم، قاعدة الـ 72 ساعة، وتكرار المطاعم، وإلغاء رسوم بوابة الدفع.

- **Auth:** `Authorization: Bearer <customer_jwt>`
- **Role:** عميل فقط → غير ذلك `403`
- **JSON:** `camelCase`
- **Enums:** string (`"Basic"`, `"Platinum"`, `"Elite"`, `"Submitted"`, `"Approved"`, `"Rejected"`)
- **العملة:** `KWD`
- **الدفع:** **MyFatoorah** (بدون أي رسوم بوابة دفع إضافية).

---

## 1. فلو الشاشات الجديد (Two-Step Upgrade Flow)

```
[1. شاشة خيارات الترقية] ────► GET /api/v1/customer/subscriptions/{id}/upgrade-options
          │
          ▼ اختيار طبقة أعلى (مثلاً Platinum)
[2. شاشة ملخص الترقية]   ────► GET /api/v1/customer/subscriptions/{id}/upgrade-summary?targetTier=Platinum
          │
          ├─► عرض مقارنة مطاعم التصنيف الحالي والتصنيف الجديد والمطاعم المكتسبة الإضافية
          ├─► عرض موعد التفعيل بعد 72 ساعة (effectiveFromDate) وتنبيه الإغلاق التشغيلي
          ├─► عرض تكرار المطاعم المسموح في التصنيف الجديد (restaurantRepetition)
          ├─► عرض السعر الصافي بدون رسوم بوابة دفع (PriceDifference)
          └─► عرض طرق الدفع المتاحة (Knet, Apple Pay, Visa/Master)
          │
          ▼ ضغط "تأكيد ودفع الترقية"
[3. بدء الدفع]           ────► POST /api/v1/customer/subscriptions/{id}/upgrade-requests
          │
          ▼ فتح بوابة MyFatoorah والتحقق من النتيجة
[4. فحص الحالة بعد الدفع] ────► GET /api/v1/customer/subscriptions/{id}/upgrade-requests/{upgradeId}
```

---

## 2. نقاط النهاية (Endpoints)

### أ) خيارات الترقية (Tiers Cards Only)
`GET /api/v1/customer/subscriptions/{id}/upgrade-options`

- **الوظيفة:** ترجع فقط بطاقات وخيارات التصنيفات المتاحة للترقية (بدون ملخص الدفع وبدون طرق الدفع لتصنيف افتراضي).
- **الاستجابة (`200 OK`):**
```json
{
  "canUpgrade": true,
  "unavailableReason": null,
  "currentTier": "Basic",
  "remainingServiceDays": 12,
  "totalServiceDays": 26,
  "upgradeEndpoint": "/api/v1/customer/subscriptions/{id}/upgrade-requests",
  "options": [
    {
      "targetTier": "Basic",
      "tierNameAr": "سيلكت",
      "tierNameEn": "Select",
      "logoUrl": "https://...",
      "isCurrent": true,
      "selectable": false,
      "accessibleRestaurantCount": 21,
      "currentRemainingCredit": 0,
      "targetRemainingPrice": 0,
      "gatewayRecovery": 0,
      "priceDifference": 0,
      "currencyCode": "KWD"
    },
    {
      "targetTier": "Platinum",
      "tierNameAr": "بلاتينيوم",
      "tierNameEn": "Platinum",
      "logoUrl": "https://...",
      "isCurrent": false,
      "selectable": true,
      "accessibleRestaurantCount": 39,
      "currentRemainingCredit": 70.5,
      "targetRemainingPrice": 86.5,
      "gatewayRecovery": 0,
      "priceDifference": 16.0,
      "currencyCode": "KWD"
    },
    {
      "targetTier": "Elite",
      "tierNameAr": "إيليت",
      "tierNameEn": "Elite",
      "logoUrl": "https://...",
      "isCurrent": false,
      "selectable": true,
      "accessibleRestaurantCount": 60,
      "currentRemainingCredit": 70.5,
      "targetRemainingPrice": 102.25,
      "gatewayRecovery": 0,
      "priceDifference": 31.75,
      "currencyCode": "KWD"
    }
  ]
}
```

---

### ب) ملخص الترقية المنفصل ومقارنة المطاعم (Upgrade Summary Endpoint)
`GET /api/v1/customer/subscriptions/{id}/upgrade-summary?targetTier={targetTier}`

- **مثال:** `GET /api/v1/customer/subscriptions/c9ddb6db-7390-4ba3-8ab9-9b0d25cf6160/upgrade-summary?targetTier=Platinum`
- **الاستجابة (`200 OK`):**
```json
{
  "subscriptionId": "c9ddb6db-7390-4ba3-8ab9-9b0d25cf6160",
  "currentTier": {
    "tier": "Basic",
    "tierNameAr": "أساسي",
    "tierNameEn": "Basic",
    "logoUrl": "https://...",
    "accessibleRestaurantCount": 21
  },
  "targetTier": {
    "tier": "Platinum",
    "tierNameAr": "بلاتينيوم",
    "tierNameEn": "Platinum",
    "logoUrl": "https://...",
    "accessibleRestaurantCount": 39
  },
  "currentTierRestaurants": [
    {
      "id": "11111111-1111-1111-1111-111111111111",
      "nameAr": "دايت هاوس",
      "nameEn": "Diet House",
      "logoUrl": "/api/v1/uploads/content?key=...",
      "rating": 4.8,
      "tier": "Basic"
    }
  ],
  "targetTierRestaurants": [
    {
      "id": "11111111-1111-1111-1111-111111111111",
      "nameAr": "دايت هاوس",
      "nameEn": "Diet House",
      "logoUrl": "/api/v1/uploads/content?key=...",
      "rating": 4.8,
      "tier": "Basic"
    },
    {
      "id": "22222222-2222-2222-2222-222222222222",
      "nameAr": "هيلثي ستيشن",
      "nameEn": "Healthy Station",
      "logoUrl": "/api/v1/uploads/content?key=...",
      "rating": 4.9,
      "tier": "Platinum"
    }
  ],
  "newlyAddedRestaurants": [
    {
      "id": "22222222-2222-2222-2222-222222222222",
      "nameAr": "هيلثي ستيشن",
      "nameEn": "Healthy Station",
      "logoUrl": "/api/v1/uploads/content?key=...",
      "rating": 4.9,
      "tier": "Platinum"
    }
  ],
  "effectiveFromDate": "2026-09-10",
  "effectiveNoticeAr": "يبدأ تفعيل التصنيف الجديد على جدول وجباتك اعتباراً من 2026-09-10 (بعد مهلة الـ 72 ساعة المقررة لتجهيز الطلبات الحالية).",
  "effectiveNoticeEn": "The new tier will take effect on your meal schedule starting from 2026-09-10 (after the 72-hour operational preparation cutoff).",
  "restaurantRepetition": {
    "limit": 3,
    "restaurantCount": 39,
    "durationDays": 12
  },
  "currentRemainingCredit": 70.5,
  "targetRemainingPrice": 86.5,
  "priceDifference": 16.0,
  "currencyCode": "KWD",
  "remainingServiceDays": 12,
  "totalServiceDays": 26,
  "paymentMethods": [
    {
      "paymentMethodId": 1,
      "code": "kn",
      "nameAr": "كي نت",
      "nameEn": "KNET",
      "integrationType": "MYFATOORAH_HOSTED",
      "serviceCharge": 0.0,
      "totalAmount": 16.0,
      "currencyCode": "KWD",
      "isAvailable": true,
      "unavailableReason": null,
      "imageUrl": "https://demo.myfatoorah.com/imgs/payment-methods/kn.png"
    },
    {
      "paymentMethodId": 11,
      "code": "ap",
      "nameAr": "أبل الدفع",
      "nameEn": "Apple Pay",
      "integrationType": "MYFATOORAH_HOSTED",
      "serviceCharge": 0.0,
      "totalAmount": 16.0,
      "currencyCode": "KWD",
      "isAvailable": true,
      "unavailableReason": null,
      "imageUrl": "https://demo.myfatoorah.com/imgs/payment-methods/ap.png"
    },
    {
      "paymentMethodId": 2,
      "code": "vm",
      "nameAr": "فيزا / ماستر",
      "nameEn": "VISA / MASTER",
      "integrationType": "MYFATOORAH_HOSTED",
      "serviceCharge": 0.0,
      "totalAmount": 16.0,
      "currencyCode": "KWD",
      "isAvailable": true,
      "unavailableReason": null,
      "imageUrl": "https://demo.myfatoorah.com/imgs/payment-methods/vm.png"
    }
  ],
  "upgradeEndpoint": "/api/v1/customer/subscriptions/c9ddb6db-7390-4ba3-8ab9-9b0d25cf6160/upgrade-requests"
}
```

#### ملاحظات هامة على شاشة ملخص الترقية:
1. **إلغاء رسوم بوابة الدفع:** المبلغ المطلوب دفعه `priceDifference` هو صافي فرق السعر فقط ($86.5 - 70.5 = 16.0$). لا توجد أي رسوم بوابة دفع مضافة (`serviceCharge = 0`).
2. **المطاعم الجديدة (`newlyAddedRestaurants`):** قائمة المطاعم الحصرية الإضافية التي سيفتحها العميل عند الترقية؛ يُنصح بعرضها في قسم بارز ("مطاعم جديدة تنضم لباقاتك").
3. **موعد التفعيل بعد 72 ساعة (`effectiveFromDate`):** الأيام التي تقع ضمن مهلة الـ 72 ساعة محجوزة ومغلقة في المطبخ للتحضير؛ التصنيف الجديد يظهر في التقويم للأيام التي تبدأ من تاريخ `effectiveFromDate`.
4. **تكرار المطاعم (`restaurantRepetition`):** يوضح للعميل الحد الأقصى للأيام المسموح بتكرار نفس المطعم فيها خلال الفترة المتبقية.

---

### ج) تأكيد وبدء الدفع
`POST /api/v1/customer/subscriptions/{id}/upgrade-requests`

```json
{
  "targetTier": "Platinum",
  "callbackUrl": "https://your-app/payment/callback",
  "errorUrl": "https://your-app/payment/error",
  "paymentMethodId": 1
}
```

- **الاستجابة (`201 Created`):**
```json
{
  "upgradeId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "subscriptionId": "c9ddb6db-7390-4ba3-8ab9-9b0d25cf6160",
  "fromTier": "Basic",
  "toTier": "Platinum",
  "amount": 16.0,
  "currencyCode": "KWD",
  "paymentUrl": "https://myfatoorah.../Invoice?paymentId=...",
  "isPaid": false,
  "status": "Submitted",
  "remainingServiceDays": 12
}
```

---

### د) التحقق من حالة الترقية بعد الدفع
`GET /api/v1/customer/subscriptions/{id}/upgrade-requests/{upgradeId}`

```json
{
  "upgradeId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "subscriptionId": "c9ddb6db-7390-4ba3-8ab9-9b0d25cf6160",
  "fromTier": "Basic",
  "toTier": "Platinum",
  "amount": 16.0,
  "currencyCode": "KWD",
  "status": "Approved",
  "isPaid": true,
  "isFailed": false,
  "effectiveAtUtc": "2026-09-06T18:00:00Z",
  "paymentUrl": null
}
```

عندما يكون `isPaid == true`:
- تم تطبيق الترقية فوراً وبشكل تلقائي في النظام بدون أي انتظار أو موافقة أدمن.
- يتم تحديث واجهة الموبايل وإعادة تحميل تفاصيل الاشتراك (`GET /api/v1/customer/subscriptions/current`).
