# ربط شاشة المدة والتصنيف بالموبايل

Base URL: `https://maelmate.runasp.net/api/v1`

جميع الطلبات تحتاج Customer access token.

## قاعدة الجمعة

- `durationDays` هو عدد أيام الباقة العادية بدون الجمعة.
- اختيار الجمعة يضيف عددًا ثابتًا من أيام الجمعة: 6 أيام تصبح 7، و12 تصبح 14، و26 تصبح 30.
- الموبايل لا يحسب السعر أو عدد الجمع بنفسه؛ يستخدم `fridayDays` و`totalServiceDays` من الباك.
- إتاحة الجمعة تعتمد على وجود مطعم معتمد ونشط داخل الباقة يعمل يوم الجمعة.

## 1. المدد المتاحة

بدون الجمعة:

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=false
Authorization: Bearer <customer-token>
```

مع الجمعة:

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=true
Authorization: Bearer <customer-token>
```

نموذج عنصر مدة 26 يومًا عند اختيار الجمعة:

```json
{
  "durationDays": 26,
  "nameAr": "26 يومًا",
  "nameEn": "26 days",
  "priceFrom": 270.000,
  "dailyPriceFrom": 9.000,
  "currencyCode": "KWD",
  "selectionCount": 24,
  "isPopular": true,
  "badge": {
    "code": "MOST_POPULAR",
    "nameAr": "الأكثر اختيارًا",
    "nameEn": "Most popular"
  },
  "supportsFriday": true,
  "includeFriday": true,
  "fridayDays": 4,
  "totalServiceDays": 30,
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
```

إذا لم يوجد مطعم يعمل الجمعة ترجع `supportsFriday=false` و`fridayOption.isEnabled=false`، ويكون التحذير:

```json
{
  "isAvailable": false,
  "restaurantCount": 0,
  "code": "FRIDAY_UNAVAILABLE",
  "messageAr": "يوم الجمعة غير متاح لعدم وجود مطاعم تعمل الجمعة ضمن هذه الباقة",
  "messageEn": "Friday is unavailable because no restaurant in this bundle operates on Friday",
  "displayType": "WARNING"
}
```

لا يرسل الموبايل `includeFriday=true` عندما تكون `fridayOption.isEnabled=false`. لو أرسله، يرجع الباك `409` بالكود `subscription_flow.friday_unavailable`.

## 2. التصنيفات

يجب إرسال نفس اختيار الجمعة:

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?durationDays=26&includeFriday=true
Authorization: Bearer <customer-token>
```

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
      }
    ],
    "durationDays": 26,
    "totalPrice": 270.000,
    "dailyPrice": 9.000,
    "priceDifference": 0.000,
    "currencyCode": "KWD",
    "selectionCount": 24,
    "isRecommended": true,
    "includeFriday": true,
    "fridayDays": 4,
    "totalServiceDays": 30,
    "fridayAvailability": {
      "isAvailable": true,
      "restaurantCount": 2,
      "code": "FRIDAY_AVAILABLE",
      "messageAr": "يوم الجمعة متاح ضمن هذه الباقة",
      "messageEn": "Friday is available for this bundle",
      "displayType": "INFO"
    }
  }
]
```

عند `includeFriday=true` لا ترجع إلا التصنيفات التي يوجد بها مطعم يعمل الجمعة. السعر النهائي وفرق السعر محسوبان بعد إضافة الجمعة.

## 3. بقية الفلو

يجب تمرير نفس `includeFriday` في كل الاستدعاءات التالية:

- `GET /customer/subscription-flow/restaurants`
- `GET /customer/subscription-flow/restaurants/{restaurantId}`
- `GET /customer/subscription-flow/summary`
- `GET /customer/checkout/restaurants`
- `POST /customer/subscriptions/checkout`

كل استجابة ترجع `includeFriday` و`fridayDays` و`totalServiceDays`. قائمة المطاعم والأهلية والمنيو والسعة والملخص والـCheckout تستخدم أيام الخدمة نفسها، لذلك لا يختلف السعر أو عدد الأيام بين شاشة وأخرى.

## ترتيب الربط

1. اختيار البرنامج والباقة.
2. استدعاء `durations?includeFriday=false`.
3. عرض رسالة `fridayAvailability` وتفعيل أو تعطيل اختيار الجمعة باستخدام `fridayOption.isEnabled`.
4. عند تغيير اختيار الجمعة، إعادة استدعاء `durations` بالقيمة الجديدة لتحديث السعر والأيام.
5. استدعاء `tiers` بنفس `includeFriday` والمدة المختارة.
6. تمرير نفس القيمة إلى المطاعم والملخص والـCheckout.
