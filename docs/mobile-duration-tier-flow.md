# فلو المدد والتصنيف للموبايل

دليل ربط شاشتي **المدة** و**التصنيف** في اشتراك العميل.

الشاشتان منفصلتان. لا تستخدم `GET /customer/subscription-flow/configuration`.

Base URL:

```text
https://maelmate.runasp.net/api/v1
```

كل الطلبات تحتاج Customer access token:

```http
Authorization: Bearer <customer-access-token>
Accept: application/json
Accept-Language: ar
```

لا ترسل `customerId` في المسار أو الـquery. الباك يأخذه من الـJWT.

---

## ترتيب الشاشات

| الترتيب | الشاشة | Endpoint |
|---|---|---|
| قبل هذه الشاشات | البرنامج | `GET /customer/subscription-flow/programs` |
| قبل هذه الشاشات | الباقة | `GET /customer/subscription-flow/programs/{programId}/bundles` |
| 1 | المدة + اختيار الجمعة | `GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=` |
| 2 | التصنيف | `GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?includeFriday=` |
| بعد الاختيار | المطاعم ثم الملخص ثم الدفع | نفس `programId` و`bundleId` و`durationDays` و`tier` و`includeFriday` |

احتفظ بهذه القيم من الشاشات السابقة:

- `programId`
- `bundleId`

بعد إنهاء الشاشتين أضف:

- `durationDays`
- `includeFriday`
- `tier`

---

## قواعد عامة

- استخدم IDs والقيم الراجعة من الباك. لا تثبت مددًا أو تصنيفات أو أسعارًا داخل التطبيق.
- `durationDays` هو أيام الباقة بدون الجمعة: عادة `6` أو `12` أو `26`.
- الموبايل لا يحسب أيام الجمعة ولا السعر. استخدم `fridayDays` و`totalServiceDays` من الاستجابة.
- اختيار الجمعة يضيف أيامًا ثابتة من الباك:
  - 6 → `fridayDays=1` و`totalServiceDays=7`
  - 12 → `fridayDays=2` و`totalServiceDays=14`
  - 26 → `fridayDays=4` و`totalServiceDays=30`
- شاشة المدة **لا تعرض سعرًا**.
- شاشة التصنيف تظهر **من غير `durationDays`**.
- زر الاشتراك/المتابعة **inactive** إلى أن يختار المستخدم تصنيفًا.
- زر الجمعة إذا كان غير متاح يظهر **inactive/disabled**، وليس رسالة تحذير.

---

## 1. شاشة المدة

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=false
```

عند تفعيل الجمعة أعد نفس الطلب:

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=true
```

أول دخول للشاشة: استدعِ `includeFriday=false`.

هذا الـendpoint **لا يرجع سعرًا**. لا يوجد `priceFrom` ولا `dailyPriceFrom` ولا `currencyCode`. لا ترسم سعرًا على كروت المدة.

### استجابة `200` كاملة

```json
[
  {
    "durationDays": 6,
    "nameAr": "6 أيام",
    "nameEn": "6 days",
    "selectionCount": 5,
    "isPopular": false,
    "badge": null,
    "supportsFriday": true,
    "includeFriday": false,
    "fridayDays": 0,
    "totalServiceDays": 6,
    "fridayOption": {
      "code": "INCLUDE_FRIDAY",
      "nameAr": "إضافة يوم الجمعة",
      "nameEn": "Include Friday",
      "defaultSelected": false,
      "isEnabled": true
    },
    "fridayAvailability": {
      "isAvailable": true,
      "restaurantCount": 3,
      "code": "FRIDAY_AVAILABLE",
      "messageAr": "يوم الجمعة متاح ضمن هذه الباقة",
      "messageEn": "Friday is available for this bundle",
      "displayType": "INFO"
    }
  },
  {
    "durationDays": 12,
    "nameAr": "12 يومًا",
    "nameEn": "12 days",
    "selectionCount": 10,
    "isPopular": false,
    "badge": null,
    "supportsFriday": true,
    "includeFriday": false,
    "fridayDays": 0,
    "totalServiceDays": 12,
    "fridayOption": {
      "code": "INCLUDE_FRIDAY",
      "nameAr": "إضافة يوم الجمعة",
      "nameEn": "Include Friday",
      "defaultSelected": false,
      "isEnabled": true
    },
    "fridayAvailability": {
      "isAvailable": true,
      "restaurantCount": 3,
      "code": "FRIDAY_AVAILABLE",
      "messageAr": "يوم الجمعة متاح ضمن هذه الباقة",
      "messageEn": "Friday is available for this bundle",
      "displayType": "INFO"
    }
  },
  {
    "durationDays": 26,
    "nameAr": "26 يومًا",
    "nameEn": "26 days",
    "selectionCount": 24,
    "isPopular": true,
    "badge": {
      "code": "MOST_POPULAR",
      "nameAr": "الأكثر اختيارًا",
      "nameEn": "Most popular"
    },
    "supportsFriday": true,
    "includeFriday": false,
    "fridayDays": 0,
    "totalServiceDays": 26,
    "fridayOption": {
      "code": "INCLUDE_FRIDAY",
      "nameAr": "إضافة يوم الجمعة",
      "nameEn": "Include Friday",
      "defaultSelected": false,
      "isEnabled": true
    },
    "fridayAvailability": {
      "isAvailable": true,
      "restaurantCount": 3,
      "code": "FRIDAY_AVAILABLE",
      "messageAr": "يوم الجمعة متاح ضمن هذه الباقة",
      "messageEn": "Friday is available for this bundle",
      "displayType": "INFO"
    }
  }
]
```

عند `includeFriday=true` نفس الشكل مع تغيير:

| المدة | `fridayDays` | `totalServiceDays` |
|---|---:|---:|
| 6 | 1 | 7 |
| 12 | 2 | 14 |
| 26 | 4 | 30 |

و`includeFriday` يصبح `true`.

### حقول العرض

| الحقل | الاستخدام |
|---|---|
| `durationDays` | قيمة الاختيار التي تُمرر للتصنيف وباقي الفلو |
| `nameAr` / `nameEn` | عنوان الكرت حسب لغة التطبيق |
| `isPopular` | إذا كانت `true` اعرض `badge` |
| `badge` | `null` إذا لم تكن المدة الأكثر اختيارًا |
| `totalServiceDays` | عدد أيام الخدمة الفعلي بعد الجمعة إن وُجدت |
| `fridayOption.isEnabled` | يفعّل أو يعطّل زر الجمعة |
| `fridayOption.defaultSelected` | دائمًا `false`؛ لا تفعّل الجمعة تلقائيًا |
| `includeFriday` | يعكس قيمة الـquery التي أرسلتها |

### زر الجمعة

- إذا `fridayOption.isEnabled=true`: الزر active. المستخدم يقدر يفعّله أو يطفيه.
- إذا `fridayOption.isEnabled=false`: الزر inactive/disabled. لا تظهر بانر تحذير ولا Alert من `fridayAvailability`.
- `displayType` في حالة التعطيل هو `"DISABLED"` وليس `"WARNING"`.
- لا ترسل `includeFriday=true` والزر مطفي.

شكل الجمعة غير المتاحة:

```json
"supportsFriday": false,
"fridayOption": {
  "code": "INCLUDE_FRIDAY",
  "nameAr": "إضافة يوم الجمعة",
  "nameEn": "Include Friday",
  "defaultSelected": false,
  "isEnabled": false
},
"fridayAvailability": {
  "isAvailable": false,
  "restaurantCount": 0,
  "code": "FRIDAY_UNAVAILABLE",
  "messageAr": "يوم الجمعة غير متاح لعدم وجود مطاعم تعمل الجمعة ضمن هذه الباقة",
  "messageEn": "Friday is unavailable because no restaurant in this bundle operates on Friday",
  "displayType": "DISABLED"
}
```

`messageAr` / `messageEn` اختياريان لـtooltip على الزر المعطّل فقط. لا تستخدمهما كتحذير ظاهر.

عند تغيير الزر: أعد `GET durations` بالقيمة الجديدة ثم حدّث `fridayDays` و`totalServiceDays`. لا يوجد سعر لتحديثه في هذه الشاشة.

---

## 2. شاشة التصنيف

التصنيفات تظهر **من غير عدد الأيام**. لا ترسل `durationDays` عند تحميل الشاشة.

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?includeFriday=false
```

مع الجمعة:

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?includeFriday=true
```

أرسل نفس `includeFriday` المختار في شاشة المدة. لا ترسل `durationDays` هنا.

قيم `tier` المرجعة: `Basic` أو `Platinum` أو `Elite`. استخدم النص الراجع كما هو في باقي الفلو.

زر الاشتراك/المتابعة يبقى **inactive** إلى أن يختار المستخدم تصنيفًا. اختيار المدة وحدها لا يفعّل الزر. بعد اختيار التصنيف يصبح الزر active، بشرط أن تكون المدة مختارة أيضًا قبل الانتقال للمطاعم.

### استجابة `200` عند تحميل الشاشة (بدون مدة)

```json
[
  {
    "tier": "Basic",
    "nameAr": "سيلكت",
    "nameEn": "Select",
    "descriptionAr": "مطاعم مختارة بجودة مميزة",
    "descriptionEn": "Selected restaurants with quality meals",
    "restaurantCount": 2,
    "restaurants": [
      {
        "restaurantId": "97db2bbb-b652-4aaa-8584-3414e6966792",
        "logoUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Fmain.webp"
      },
      {
        "restaurantId": "1fced92c-b900-472a-885e-c4199b1a446f",
        "logoUrl": null
      }
    ],
    "durationDays": null,
    "totalPrice": null,
    "dailyPrice": null,
    "priceDifference": null,
    "currencyCode": null,
    "selectionCount": 24,
    "isRecommended": true,
    "includeFriday": false,
    "fridayDays": null,
    "totalServiceDays": null,
    "fridayAvailability": {
      "isAvailable": true,
      "restaurantCount": 2,
      "code": "FRIDAY_AVAILABLE",
      "messageAr": "يوم الجمعة متاح ضمن هذه الباقة",
      "messageEn": "Friday is available for this bundle",
      "displayType": "INFO"
    }
  },
  {
    "tier": "Platinum",
    "nameAr": "بلاتينوم",
    "nameEn": "Platinum",
    "descriptionAr": "خيارات أوسع من المطاعم المميزة",
    "descriptionEn": "A wider choice of premium restaurants",
    "restaurantCount": 1,
    "restaurants": [
      {
        "restaurantId": "3aa11111-2222-3333-4444-555555555555",
        "logoUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Fplatinum.webp"
      }
    ],
    "durationDays": null,
    "totalPrice": null,
    "dailyPrice": null,
    "priceDifference": null,
    "currencyCode": null,
    "selectionCount": 8,
    "isRecommended": false,
    "includeFriday": false,
    "fridayDays": null,
    "totalServiceDays": null,
    "fridayAvailability": {
      "isAvailable": true,
      "restaurantCount": 1,
      "code": "FRIDAY_AVAILABLE",
      "messageAr": "يوم الجمعة متاح ضمن هذه الباقة",
      "messageEn": "Friday is available for this bundle",
      "displayType": "INFO"
    }
  },
  {
    "tier": "Elite",
    "nameAr": "إيليت",
    "nameEn": "Elite",
    "descriptionAr": "أعلى تصنيف للمطاعم وتجربة مميزة",
    "descriptionEn": "Top-tier restaurants and an elevated experience",
    "restaurantCount": 1,
    "restaurants": [
      {
        "restaurantId": "4bb22222-3333-4444-5555-666666666666",
        "logoUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Felite.webp"
      }
    ],
    "durationDays": null,
    "totalPrice": null,
    "dailyPrice": null,
    "priceDifference": null,
    "currencyCode": null,
    "selectionCount": 3,
    "isRecommended": false,
    "includeFriday": false,
    "fridayDays": null,
    "totalServiceDays": null,
    "fridayAvailability": {
      "isAvailable": true,
      "restaurantCount": 1,
      "code": "FRIDAY_AVAILABLE",
      "messageAr": "يوم الجمعة متاح ضمن هذه الباقة",
      "messageEn": "Friday is available for this bundle",
      "displayType": "INFO"
    }
  }
]
```

الأسماء والأوصاف أرقام/نصوص توضيحية. ارسم `nameAr` / `nameEn` الراجعة من الباك، ولا تثبت نص التصنيف في التطبيق.

### عرض السعر بعد اختيار التصنيف والمدة

بعد ما المستخدم يختار تصنيفًا **ومدة**، أعد الطلب مع `durationDays` عشان السعر:

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?durationDays=26&includeFriday=false
```

هنا `totalPrice` و`dailyPrice` و`priceDifference` و`currencyCode` ترجع أرقامًا.

قواعد الرسم:

1. تحميل الشاشة: كروت التصنيف ظاهرة، من غير سعر، وزر الاشتراك inactive.
2. بعد اختيار تصنيف: فعّل زر الاشتراك. الكروت غير المحددة تبقى بدون سعر.
3. بعد اختيار تصنيف + مدة: اعرض `totalPrice` و`currencyCode` للتصنيف المحدد فقط.
4. لا تعرض أسعار كل التصنيفات جنب بعض قبل الاختيار.
5. لا تنتقل للمطاعم أو الملخص أو الدفع وزر الاشتراك inactive، أو قبل اختيار المدة والتصنيف معًا.

إذا `logoUrl=null` اعرض Placeholder. `restaurants` أول 8 مطاعم فقط؛ العدد الكامل في `restaurantCount`.

عند `includeFriday=true` لا ترجع إلا التصنيفات التي فيها مطعم يعمل الجمعة.

إذا `isRecommended=true` يمكن عرض بادج التوصية. لا تعتبره اختيارًا تلقائيًا ولا تفعّل الزر بسببه.

---

## أخطاء هذه الشاشات

| HTTP | الكود | المعنى | تصرف الموبايل |
|---:|---|---|---|
| 401 | — | التوكن منتهي | Refresh ثم أعد الطلب مرة واحدة |
| 403 | — | التوكن ليس Customer | أعد تسجيل الدخول |
| 404 | `subscription_flow.bundle_not_found` | البرنامج أو الباقة غير موجودة/غير فعالة | ارجع لشاشة الباقات |
| 409 | `pricing_unavailable` | لا توجد مدد مسعّرة لهذه الباقة | رسالة إعادة المحاولة والرجوع |
| 409 | `subscription_flow.friday_unavailable` | أُرسل `includeFriday=true` والجمعة غير متاحة | لا ترسل الجمعة؛ اجعل الزر disabled |
| 400 | `subscription_flow.duration_not_available` | المدة المختارة غير متاحة لهذه الباقة | أعد تحميل المدد |

مثال `409` للجمعة:

```json
{
  "error": "subscription_flow.friday_unavailable",
  "fridayAvailability": {
    "isAvailable": false,
    "restaurantCount": 0,
    "code": "FRIDAY_UNAVAILABLE",
    "messageAr": "يوم الجمعة غير متاح لعدم وجود مطاعم تعمل الجمعة ضمن هذه الباقة",
    "messageEn": "Friday is unavailable because no restaurant in this bundle operates on Friday",
    "displayType": "DISABLED"
  }
}
```

---

## خطوات الربط

1. المستخدم يختار برنامجًا ثم باقة. احتفظ بـ`programId` و`bundleId`.
2. `GET .../durations?includeFriday=false`.
3. ارسم كروت المدة بدون سعر. المدة الأكثر اختيارًا من `isPopular` و`badge`.
4. زر الجمعة من `fridayOption.isEnabled`: active أو disabled. لا تحذير.
5. إذا فعّل الجمعة وكان الزر active: أعد `durations?includeFriday=true` وحدّث الأيام فقط.
6. `GET .../tiers?includeFriday={includeFriday}` **من غير** `durationDays`. ارسم كروت التصنيف فورًا.
7. زر الاشتراك/المتابعة inactive إلى أن يختار المستخدم تصنيفًا.
8. بعد اختيار تصنيف + مدة: أعد `GET .../tiers?durationDays={durationDays}&includeFriday={includeFriday}` واعرض `totalPrice` للتصنيف المحدد فقط.
9. للمتابعة مرر:

```text
programId
bundleId
durationDays
includeFriday
tier
```

10. لا تغيّر هذه القيم محليًا في شاشات المطاعم والملخص والدفع. نفس `includeFriday` يُرسل في:

```text
programId
bundleId
durationDays
includeFriday
tier
```

10. لا تغيّر هذه القيم محليًا في شاشات المطاعم والملخص والدفع. نفس `includeFriday` يُرسل في:

- `GET /customer/subscription-flow/restaurants`
- `GET /customer/subscription-flow/restaurants/{restaurantId}`
- `GET /customer/subscription-flow/summary`
- `GET /customer/checkout/restaurants`
- `POST /customer/subscriptions/checkout`

---

## ما لا يفعله الموبايل

- لا تستخدم `GET /customer/subscription-flow/configuration`.
- لا تحسب السعر أو أيام الجمعة بمعادلة محلية.
- لا تعرض سعرًا في شاشة المدة.
- لا ترسل `durationDays` عند أول تحميل للتصنيفات.
- لا تفعّل زر الاشتراك قبل اختيار تصنيف.
- لا تعرض أسعار التصنيفات قبل اختيار تصنيف ومدة.
- لا تفعّل الجمعة تلقائيًا؛ `defaultSelected` أصلًا `false`.
- لا ترسل `includeFriday=true` إذا كان الزر disabled.
- لا تعتبر قائمة مطاعم التصنيف قائمة الأهلية النهائية. الأهلية تُحسم لاحقًا في `GET /customer/subscription-flow/restaurants`.
