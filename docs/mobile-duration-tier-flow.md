# ربط شاشة المدة والتصنيف بالموبايل

Base URL: `https://maelmate.runasp.net/api/v1`

جميع الطلبات تحتاج Customer access token.

## 1. المدد المتاحة

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations
Authorization: Bearer <customer-token>
Accept-Language: ar
```

```json
[
  {
    "durationDays": 6,
    "nameAr": "6 أيام",
    "nameEn": "6 days",
    "priceFrom": 66.000,
    "dailyPriceFrom": 11.000,
    "currencyCode": "KWD",
    "selectionCount": 8,
    "isPopular": false,
    "badge": null
  },
  {
    "durationDays": 26,
    "nameAr": "26 يومًا",
    "nameEn": "26 days",
    "priceFrom": 234.000,
    "dailyPriceFrom": 9.000,
    "currencyCode": "KWD",
    "selectionCount": 24,
    "isPopular": true,
    "badge": {
      "code": "MOST_POPULAR",
      "nameAr": "الأكثر اختيارًا",
      "nameEn": "Most popular"
    }
  }
]
```

- `priceFrom` و`dailyPriceFrom` هما أقل سعر متاح بين التصنيفات لهذه المدة، للعرض الاسترشادي قبل اختيار التصنيف.
- `isPopular` يُحسب من الاشتراكات المدفوعة والمؤكدة فقط. إذا لا توجد بيانات، تكون كل القيم `false`.

## 2. التصنيفات للمدة المختارة

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?durationDays=26
Authorization: Bearer <customer-token>
Accept-Language: ar
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
    "totalPrice": 234.000,
    "dailyPrice": 9.000,
    "priceDifference": 0.000,
    "currencyCode": "KWD",
    "selectionCount": 24,
    "isRecommended": true
  },
  {
    "tier": "Platinum",
    "nameAr": "بلس",
    "nameEn": "Plus",
    "descriptionAr": "خيارات أوسع من المطاعم المميزة",
    "descriptionEn": "A wider choice of premium restaurants",
    "restaurantCount": 1,
    "restaurants": [],
    "durationDays": 26,
    "totalPrice": 286.000,
    "dailyPrice": 11.000,
    "priceDifference": 52.000,
    "currencyCode": "KWD",
    "selectionCount": 10,
    "isRecommended": false
  }
]
```

- `priceDifference` هو فرق السعر عن أقل تصنيف متاح لنفس المدة.
- `restaurants` تحتوي أول 8 مطاعم فقط، وكل عنصر يرجع `restaurantId` و`logoUrl` فقط.
- `restaurantCount` هو العدد الكامل للمطاعم المعتمدة والنشطة ذات أحدث تصنيف مطابق.
- `isRecommended` يكون للتصنيف الأكثر اختيارًا لهذه الباقة والمدة. إذا لا توجد اختيارات سابقة، يكون لأول تصنيف سعري متاح.

## ترتيب استدعاءات الموبايل

1. اختيار البرنامج.
2. اختيار الباقة.
3. استدعاء `durations` واختيار المدة.
4. استدعاء `tiers` باستخدام `durationDays` المختارة.
5. بعد اختيار التصنيف والعنوان والتاريخ، استدعاء `/customer/subscription-flow/restaurants` لفحص الأهلية النهائية.

المسار القديم `/customer/subscription-flow/configuration` باقٍ للتوافق، لكن الفلو الجديد يستخدم المسارين المنفصلين.
