# ربط شاشة اختيار الباقة بالموبايل

## Endpoint

```http
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles
Authorization: Bearer <customer-token>
Accept-Language: ar
```

يرجع الباقات الفعالة التابعة للبرنامج فقط. بيانات العرض ثنائية اللغة، ويحتفظ بالحقول القديمة للتوافق مع النسخ الحالية من التطبيق.

## نموذج الاستجابة

```json
[
  {
    "bundleId": "a6497534-efb2-4744-b190-9ac81a5b5102",
    "programId": "b59a2251-ba7f-4a78-a6bb-d43defa2e4d2",
    "code": "PKG-BL1S",
    "nameAr": "باقة 3 وجبات",
    "nameEn": "3 Meals Bundle",
    "description": "Breakfast, lunch and snack",
    "descriptionAr": "فطور وغداء وسناك",
    "descriptionEn": "Breakfast, lunch and snack",
    "imageUrl": null,
    "mealsPerDay": 3,
    "breakfastCount": 1,
    "lunchCount": 1,
    "dinnerCount": 0,
    "snackCount": 1,
    "slotRequirements": [
      { "slot": "breakfast", "count": 1 },
      { "slot": "lunch", "count": 1 },
      { "slot": "snack", "count": 1 }
    ],
    "slots": [
      { "code": "breakfast", "nameAr": "الإفطار", "nameEn": "Breakfast", "quantity": 1 },
      { "code": "lunch", "nameAr": "الغداء", "nameEn": "Lunch", "quantity": 1 },
      { "code": "snack", "nameAr": "سناك", "nameEn": "Snack", "quantity": 1 }
    ],
    "restaurantCount": 2,
    "restaurants": [
      {
        "restaurantId": "97db2bbb-b652-4aaa-8584-3414e6966792",
        "nameAr": "اسم المطعم",
        "nameEn": "Restaurant name",
        "imageUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Fmain.webp"
      },
      {
        "restaurantId": "1fced92c-b900-472a-885e-c4199b1a446f",
        "nameAr": "مطعم بدون صورة",
        "nameEn": "Restaurant without image",
        "imageUrl": null
      }
    ],
    "sortOrder": 1
  }
]
```

## المطلوب في الموبايل

- استخدم `nameAr` أو `nameEn` و`descriptionAr` أو `descriptionEn` حسب لغة التطبيق.
- استخدم `slots` لعرض أسماء الوجبات المترجمة والكميات. اترك `slotRequirements` للتوافق والمنطق الداخلي فقط.
- اعرض صور `restaurants` كتجميع صور داخل كارت الباقة، واعرض Placeholder عندما تكون `imageUrl = null`.
- `restaurantCount` هو العدد الكامل للمطاعم المعتمدة والنشطة التي فعّلت الباقة، بينما `restaurants` تعرض أول 8 مطاعم فقط لتقليل حجم الاستجابة.
- لا تعتبر هذه القائمة قائمة الأهلية النهائية. بعد اختيار المنطقة والتاريخ والمدة والتصنيف، استخدم `GET /customer/subscription-flow/restaurants`؛ فهو الذي يفحص المنيو والسعة والتسعير والتغطية والحساسية والتفضيلات.
- `imageUrl` الخاص بالمطعم يأتي من صورة الفرع الأساسية فقط. الباك لا يستخدم صورة وجبة كبديل.

## التوافق

الحقول القديمة لم تُحذف: `description`, `breakfastCount`, `lunchCount`, `dinnerCount`, `snackCount`, و`slotRequirements`. لذلك يمكن نشر تحديث الموبايل تدريجيًا دون كسر النسخة الحالية.
