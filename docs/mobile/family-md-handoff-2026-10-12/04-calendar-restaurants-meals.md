# التقويم والمطاعم والوجبات

## ما تغير وطريقة الربط

استعمل /api/v1/customer/subscriptions/{subscriptionId}/calendar ثم days/{date}. date هو تاريخ خدمة الكويت YYYY-MM-DD، لا تحول DateOnly إلى UTC فيتغير اليوم. selectionDeadlineUtc/lockAtUtc timestamps UTC. استعمل canEdit وeditBlockReason وصلاحية الفعل معًا؛ الصلاحية وحدها لا تفتح يومًا محميًا.

isFulfillmentReady=false قد يعني الدفع مثبت لكن أيام الخدمة لم تُجهز بعد؛ لا تصنع أيامًا محليًا. uiStatus قيمه empty/drafting/awaiting_restaurant/auto_selected/locked/frozen/cancelled/replacement_required. editBlockReason يشمل selection_deadline_passed/day_locked/day_frozen/day_cancelled؛ fulfillment_not_ready حالة تجهيز الشاشة وليست قيمة حظر يوم.

GET days/{date}/restaurants يدعم query/page/pageSize. GET days/{date}/restaurants/{restaurantId}/meals يدعم slot/page/pageSize. استخدم isEnabled/disabledReason وحصة المطعم، ولا تعرض وجبة blocked كاختيار صالح. POST restaurant: {"restaurantId":"<UUID>"}. POST meals/slot: {"mealId":"<UUID>","slot":"<slot من السيرفر>"}. POST complete يقبل idempotencyKey في body أو Idempotency-Key header. POST confirm وauto-select بلا body. عمليات الكتابة ترجع CustomerCalendarDayDto؛ استبدل اليوم بالاستجابة ثم حدث الملخص.

GET days/{date}/meals/available مسار legacy؛ الربط الجديد يستعمل restaurantId الصريح. selectedMeals وslotRequirements تحددان ملء الوجبات؛ لا تفترض وجبة واحدة لكل slot. احتفظ nameAr/nameEn وحقول التغذية الأصلية، واعرض اللغة الحالية دون تعديل بيانات الدومين.

هذه **أشكال JSON توضيحية** مأخوذة من DTOs، وليست بيانات أعمال أو requests جاهزة للإرسال. استبدل الرموز بقيم السيرفر. الصفر وfalse يوضحان نوع القيمة فقط وليسا defaults أو قرارات صلاحية. الحقول nullable تظهر null هنا؛ راجع نوعها في الجدول لمعرفة القيمة عند وجودها. عنصر array يشرح نوع العنصر؛ الاستجابة الحقيقية قد ترجع []. IDs لا تُولّد بدل IDs السيرفر. Defaults موضحة منفصلة في الجداول. enums تُرسل بأسمائها الأصلية، والحقول camelCase. بعض حقول الاستجابة قد تُحذف عند null وفق خيارات serializer؛ العميل يتحمل null والغياب.

### CompleteDayBoxRequest

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "idempotencyKey": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `idempotencyKey` | `string?` | `null` |


### CustomerCalendarDayDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "id": "<UUID من السيرفر>",
  "serviceDate": "<YYYY-MM-DD>",
  "restaurantId": null,
  "restaurantNameAr": null,
  "restaurantNameEn": null,
  "restaurantLogoUrl": null,
  "status": "<Open | AutoSelected | CustomerSelected | Locked72h | SentToRestaurant | ConfirmedByRestaurant | ReplacementRequired | ReplacementWindowOpen | Preparing | OutForDelivery | Delivered | Frozen | Cancelled | Failed>",
  "uiStatus": "<string>",
  "isAutoSelected": false,
  "canEdit": false,
  "selectionDeadlineUtc": null,
  "lockAtUtc": null,
  "lockRemainingSeconds": null,
  "filledCount": 0,
  "requiredCount": 0,
  "slotRequirements": [
    {
      "slot": "<string>",
      "slotNameAr": "<string>",
      "slotNameEn": "<string>",
      "count": 0
    }
  ],
  "failureReasonCode": null,
  "nutritionTotals": {
    "calories": 0,
    "proteinG": 0,
    "carbsG": 0,
    "fatG": 0,
    "proteinPercentage": 0,
    "carbsPercentage": 0,
    "fatPercentage": 0,
    "caloriesPercentage": 0
  },
  "selectedMeals": [
    {
      "mealId": "<UUID من السيرفر>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "slot": "<string>",
      "slotNameAr": "<string>",
      "slotNameEn": "<string>",
      "sortOrder": 0,
      "imageUrl": null,
      "calories": null,
      "proteinG": null,
      "carbsG": null,
      "fatG": null,
      "descriptionAr": null,
      "descriptionEn": null,
      "presentAllergenCodes": [
        "<string>"
      ],
      "mayContainAllergenCodes": [
        "<string>"
      ],
      "dietaryPreferenceCodes": [
        "<string>"
      ],
      "ingredients": [
        {
          "id": "<UUID من السيرفر>",
          "nameAr": "<string>",
          "nameEn": "<string>",
          "originCountryCode": null,
          "supplierName": null,
          "isLocal": false,
          "isHalalCertified": false,
          "allergenCodes": [
            "<string>"
          ],
          "isCore": false,
          "iconUrl": null
        }
      ],
      "blockReason": null,
      "isSelected": false,
      "proteinPercentage": null,
      "carbsPercentage": null,
      "fatPercentage": null,
      "caloriesPercentage": null,
      "allergens": null,
      "allergensAr": null,
      "allergensEn": null,
      "allergenDetails": null
    }
  ],
  "restaurantRating": null,
  "restaurantRatingCount": 0,
  "deliveryWindowStart": null,
  "deliveryWindowEnd": null,
  "deliveryTimeText": null,
  "tier": null,
  "tierNameAr": null,
  "tierNameEn": null,
  "tierName": null,
  "tierLogoUrl": null,
  "programNameAr": null,
  "programNameEn": null,
  "programName": null,
  "proteinPercentage": null,
  "carbsPercentage": null,
  "fatPercentage": null,
  "caloriesPercentage": null,
  "editBlockReason": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `id` | `Guid` | — |
| `serviceDate` | `DateOnly` | — |
| `restaurantId` | `Guid?` | — |
| `restaurantNameAr` | `string?` | — |
| `restaurantNameEn` | `string?` | — |
| `restaurantLogoUrl` | `string?` | — |
| `status` | `SubscriptionDayStatus` | — |
| `uiStatus` | `string` | — |
| `isAutoSelected` | `bool` | — |
| `canEdit` | `bool` | — |
| `selectionDeadlineUtc` | `DateTime?` | — |
| `lockAtUtc` | `DateTime?` | — |
| `lockRemainingSeconds` | `int?` | — |
| `filledCount` | `int` | — |
| `requiredCount` | `int` | — |
| `slotRequirements` | `IReadOnlyList<CustomerCalendarSlotDto>` | — |
| `failureReasonCode` | `string?` | — |
| `nutritionTotals` | `CustomerDayNutritionTotalsDto` | — |
| `selectedMeals` | `IReadOnlyList<CustomerCalendarMealDto>` | — |
| `restaurantRating` | `decimal?` | `null` |
| `restaurantRatingCount` | `int` | `0` |
| `deliveryWindowStart` | `TimeOnly?` | `null` |
| `deliveryWindowEnd` | `TimeOnly?` | `null` |
| `deliveryTimeText` | `string?` | `null` |
| `tier` | `RestaurantTier?` | `null` |
| `tierNameAr` | `string?` | `null` |
| `tierNameEn` | `string?` | `null` |
| `tierName` | `string?` | `null` |
| `tierLogoUrl` | `string?` | `null` |
| `programNameAr` | `string?` | `null` |
| `programNameEn` | `string?` | `null` |
| `programName` | `string?` | `null` |
| `proteinPercentage` | `decimal?` | `null` |
| `carbsPercentage` | `decimal?` | `null` |
| `fatPercentage` | `decimal?` | `null` |
| `caloriesPercentage` | `decimal?` | `null` |
| `editBlockReason` | `string?` | `null` |


### CustomerCalendarDaySummaryDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "id": "<UUID من السيرفر>",
  "serviceDate": "<YYYY-MM-DD>",
  "restaurantId": null,
  "restaurantNameAr": null,
  "restaurantNameEn": null,
  "restaurantLogoUrl": null,
  "status": "<Open | AutoSelected | CustomerSelected | Locked72h | SentToRestaurant | ConfirmedByRestaurant | ReplacementRequired | ReplacementWindowOpen | Preparing | OutForDelivery | Delivered | Frozen | Cancelled | Failed>",
  "uiStatus": "<string>",
  "isAutoSelected": false,
  "canEdit": false,
  "selectionDeadlineUtc": null,
  "lockAtUtc": null,
  "lockRemainingSeconds": null,
  "filledCount": 0,
  "requiredCount": 0,
  "slotRequirements": [
    {
      "slot": "<string>",
      "slotNameAr": "<string>",
      "slotNameEn": "<string>",
      "count": 0
    }
  ],
  "failureReasonCode": null,
  "selectedMeals": [
    {
      "mealId": "<UUID من السيرفر>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "slot": "<string>",
      "slotNameAr": "<string>",
      "slotNameEn": "<string>",
      "sortOrder": 0,
      "imageUrl": null,
      "isSelected": false
    }
  ],
  "restaurantRating": null,
  "restaurantRatingCount": 0,
  "deliveryWindowStart": null,
  "deliveryWindowEnd": null,
  "deliveryTimeText": null,
  "tier": null,
  "tierNameAr": null,
  "tierNameEn": null,
  "tierName": null,
  "tierLogoUrl": null,
  "programNameAr": null,
  "programNameEn": null,
  "programName": null,
  "editBlockReason": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `id` | `Guid` | — |
| `serviceDate` | `DateOnly` | — |
| `restaurantId` | `Guid?` | — |
| `restaurantNameAr` | `string?` | — |
| `restaurantNameEn` | `string?` | — |
| `restaurantLogoUrl` | `string?` | — |
| `status` | `SubscriptionDayStatus` | — |
| `uiStatus` | `string` | — |
| `isAutoSelected` | `bool` | — |
| `canEdit` | `bool` | — |
| `selectionDeadlineUtc` | `DateTime?` | — |
| `lockAtUtc` | `DateTime?` | — |
| `lockRemainingSeconds` | `int?` | — |
| `filledCount` | `int` | — |
| `requiredCount` | `int` | — |
| `slotRequirements` | `IReadOnlyList<CustomerCalendarSlotDto>` | — |
| `failureReasonCode` | `string?` | — |
| `selectedMeals` | `IReadOnlyList<CustomerCalendarMealSummaryDto>` | — |
| `restaurantRating` | `decimal?` | `null` |
| `restaurantRatingCount` | `int` | `0` |
| `deliveryWindowStart` | `TimeOnly?` | `null` |
| `deliveryWindowEnd` | `TimeOnly?` | `null` |
| `deliveryTimeText` | `string?` | `null` |
| `tier` | `RestaurantTier?` | `null` |
| `tierNameAr` | `string?` | `null` |
| `tierNameEn` | `string?` | `null` |
| `tierName` | `string?` | `null` |
| `tierLogoUrl` | `string?` | `null` |
| `programNameAr` | `string?` | `null` |
| `programNameEn` | `string?` | `null` |
| `programName` | `string?` | `null` |
| `editBlockReason` | `string?` | `null` |


### CustomerCalendarMealAllergenDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "code": "<string>",
  "nameAr": "<string>",
  "nameEn": "<string>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `code` | `string` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |


### CustomerCalendarMealDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "mealId": "<UUID من السيرفر>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "slot": "<string>",
  "slotNameAr": "<string>",
  "slotNameEn": "<string>",
  "sortOrder": 0,
  "imageUrl": null,
  "calories": null,
  "proteinG": null,
  "carbsG": null,
  "fatG": null,
  "descriptionAr": null,
  "descriptionEn": null,
  "presentAllergenCodes": [
    "<string>"
  ],
  "mayContainAllergenCodes": [
    "<string>"
  ],
  "dietaryPreferenceCodes": [
    "<string>"
  ],
  "ingredients": [
    {
      "id": "<UUID من السيرفر>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "originCountryCode": null,
      "supplierName": null,
      "isLocal": false,
      "isHalalCertified": false,
      "allergenCodes": [
        "<string>"
      ],
      "isCore": false,
      "iconUrl": null
    }
  ],
  "blockReason": null,
  "isSelected": false,
  "proteinPercentage": null,
  "carbsPercentage": null,
  "fatPercentage": null,
  "caloriesPercentage": null,
  "allergens": null,
  "allergensAr": null,
  "allergensEn": null,
  "allergenDetails": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `mealId` | `Guid` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `slot` | `string` | — |
| `slotNameAr` | `string` | — |
| `slotNameEn` | `string` | — |
| `sortOrder` | `int` | — |
| `imageUrl` | `string?` | — |
| `calories` | `decimal?` | — |
| `proteinG` | `decimal?` | — |
| `carbsG` | `decimal?` | — |
| `fatG` | `decimal?` | — |
| `descriptionAr` | `string?` | — |
| `descriptionEn` | `string?` | — |
| `presentAllergenCodes` | `IReadOnlyList<string>` | — |
| `mayContainAllergenCodes` | `IReadOnlyList<string>` | — |
| `dietaryPreferenceCodes` | `IReadOnlyList<string>` | — |
| `ingredients` | `IReadOnlyList<CustomerMealIngredientDto>` | — |
| `blockReason` | `string?` | — |
| `isSelected` | `bool` | `false` |
| `proteinPercentage` | `decimal?` | `null` |
| `carbsPercentage` | `decimal?` | `null` |
| `fatPercentage` | `decimal?` | `null` |
| `caloriesPercentage` | `decimal?` | `null` |
| `allergens` | `IReadOnlyList<string>?` | `null` |
| `allergensAr` | `IReadOnlyList<string>?` | `null` |
| `allergensEn` | `IReadOnlyList<string>?` | `null` |
| `allergenDetails` | `IReadOnlyList<CustomerCalendarMealAllergenDto>?` | `null` |


### CustomerCalendarMealSummaryDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "mealId": "<UUID من السيرفر>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "slot": "<string>",
  "slotNameAr": "<string>",
  "slotNameEn": "<string>",
  "sortOrder": 0,
  "imageUrl": null,
  "isSelected": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `mealId` | `Guid` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `slot` | `string` | — |
| `slotNameAr` | `string` | — |
| `slotNameEn` | `string` | — |
| `sortOrder` | `int` | — |
| `imageUrl` | `string?` | — |
| `isSelected` | `bool` | `false` |


### CustomerCalendarScreenDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "subscriptionId": "<UUID من السيرفر>",
  "programId": null,
  "bundleId": null,
  "requiredCount": 0,
  "slotRequirements": [
    {
      "slot": "<string>",
      "slotNameAr": "<string>",
      "slotNameEn": "<string>",
      "count": 0
    }
  ],
  "isFulfillmentReady": false,
  "days": [
    {
      "id": "<UUID من السيرفر>",
      "serviceDate": "<YYYY-MM-DD>",
      "restaurantId": null,
      "restaurantNameAr": null,
      "restaurantNameEn": null,
      "restaurantLogoUrl": null,
      "status": "<Open | AutoSelected | CustomerSelected | Locked72h | SentToRestaurant | ConfirmedByRestaurant | ReplacementRequired | ReplacementWindowOpen | Preparing | OutForDelivery | Delivered | Frozen | Cancelled | Failed>",
      "uiStatus": "<string>",
      "isAutoSelected": false,
      "canEdit": false,
      "selectionDeadlineUtc": null,
      "lockAtUtc": null,
      "lockRemainingSeconds": null,
      "filledCount": 0,
      "requiredCount": 0,
      "slotRequirements": [
        {
          "slot": "<string>",
          "slotNameAr": "<string>",
          "slotNameEn": "<string>",
          "count": 0
        }
      ],
      "failureReasonCode": null,
      "selectedMeals": [
        {
          "mealId": "<UUID من السيرفر>",
          "nameAr": "<string>",
          "nameEn": "<string>",
          "slot": "<string>",
          "slotNameAr": "<string>",
          "slotNameEn": "<string>",
          "sortOrder": 0,
          "imageUrl": null,
          "isSelected": false
        }
      ],
      "restaurantRating": null,
      "restaurantRatingCount": 0,
      "deliveryWindowStart": null,
      "deliveryWindowEnd": null,
      "deliveryTimeText": null,
      "tier": null,
      "tierNameAr": null,
      "tierNameEn": null,
      "tierName": null,
      "tierLogoUrl": null,
      "programNameAr": null,
      "programNameEn": null,
      "programName": null,
      "editBlockReason": null
    }
  ],
  "hasUnreadNotifications": false,
  "unreadNotificationsCount": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscriptionId` | `Guid` | — |
| `programId` | `Guid?` | — |
| `bundleId` | `Guid?` | — |
| `requiredCount` | `int` | — |
| `slotRequirements` | `IReadOnlyList<CustomerCalendarSlotDto>` | — |
| `isFulfillmentReady` | `bool` | — |
| `days` | `IReadOnlyList<CustomerCalendarDaySummaryDto>` | — |
| `hasUnreadNotifications` | `bool` | `false` |
| `unreadNotificationsCount` | `int` | `0` |


### CustomerCalendarSlotDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "slot": "<string>",
  "slotNameAr": "<string>",
  "slotNameEn": "<string>",
  "count": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `slot` | `string` | — |
| `slotNameAr` | `string` | — |
| `slotNameEn` | `string` | — |
| `count` | `int` | — |


### CustomerCalendarSlotMealsDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "slot": "<string>",
  "slotNameAr": "<string>",
  "slotNameEn": "<string>",
  "count": 0,
  "page": 0,
  "pageSize": 0,
  "totalCount": 0,
  "totalPages": 0,
  "hasNextPage": false,
  "hasPreviousPage": false,
  "meals": [
    {
      "mealId": "<UUID من السيرفر>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "slot": "<string>",
      "slotNameAr": "<string>",
      "slotNameEn": "<string>",
      "sortOrder": 0,
      "imageUrl": null,
      "calories": null,
      "proteinG": null,
      "carbsG": null,
      "fatG": null,
      "descriptionAr": null,
      "descriptionEn": null,
      "presentAllergenCodes": [
        "<string>"
      ],
      "mayContainAllergenCodes": [
        "<string>"
      ],
      "dietaryPreferenceCodes": [
        "<string>"
      ],
      "ingredients": [
        {
          "id": "<UUID من السيرفر>",
          "nameAr": "<string>",
          "nameEn": "<string>",
          "originCountryCode": null,
          "supplierName": null,
          "isLocal": false,
          "isHalalCertified": false,
          "allergenCodes": [
            "<string>"
          ],
          "isCore": false,
          "iconUrl": null
        }
      ],
      "blockReason": null,
      "isSelected": false,
      "proteinPercentage": null,
      "carbsPercentage": null,
      "fatPercentage": null,
      "caloriesPercentage": null,
      "allergens": null,
      "allergensAr": null,
      "allergensEn": null,
      "allergenDetails": null
    }
  ]
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `slot` | `string` | — |
| `slotNameAr` | `string` | — |
| `slotNameEn` | `string` | — |
| `count` | `int` | — |
| `page` | `int` | — |
| `pageSize` | `int` | — |
| `totalCount` | `int` | — |
| `totalPages` | `int` | — |
| `hasNextPage` | `bool` | — |
| `hasPreviousPage` | `bool` | — |
| `meals` | `IReadOnlyList<CustomerCalendarMealDto>` | — |


### CustomerDayNutritionTotalsDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "calories": 0,
  "proteinG": 0,
  "carbsG": 0,
  "fatG": 0,
  "proteinPercentage": 0,
  "carbsPercentage": 0,
  "fatPercentage": 0,
  "caloriesPercentage": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `calories` | `decimal` | — |
| `proteinG` | `decimal` | — |
| `carbsG` | `decimal` | — |
| `fatG` | `decimal` | — |
| `proteinPercentage` | `decimal` | `0m` |
| `carbsPercentage` | `decimal` | `0m` |
| `fatPercentage` | `decimal` | `0m` |
| `caloriesPercentage` | `decimal` | `0m` |


### CustomerDayRestaurantDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "restaurantId": "<UUID من السيرفر>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "logoUrl": null,
  "tier": "<Basic | Platinum | Elite>",
  "usedCount": 0,
  "limit": 0,
  "remainingCount": 0,
  "isBusy": false,
  "isQuotaExhausted": false,
  "isEnabled": false,
  "disabledReason": null,
  "safeMealCount": 0,
  "rating": null,
  "ratingCount": 0,
  "deliveryWindowStart": null,
  "deliveryWindowEnd": null,
  "deliveryTimeText": null,
  "tierNameAr": null,
  "tierNameEn": null,
  "tierName": null,
  "tierLogoUrl": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `restaurantId` | `Guid` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `logoUrl` | `string?` | — |
| `tier` | `RestaurantTier` | — |
| `usedCount` | `int` | — |
| `limit` | `int` | — |
| `remainingCount` | `int` | — |
| `isBusy` | `bool` | — |
| `isQuotaExhausted` | `bool` | — |
| `isEnabled` | `bool` | — |
| `disabledReason` | `string?` | — |
| `safeMealCount` | `int` | — |
| `rating` | `decimal?` | `null` |
| `ratingCount` | `int` | `0` |
| `deliveryWindowStart` | `TimeOnly?` | `null` |
| `deliveryWindowEnd` | `TimeOnly?` | `null` |
| `deliveryTimeText` | `string?` | `null` |
| `tierNameAr` | `string?` | `null` |
| `tierNameEn` | `string?` | `null` |
| `tierName` | `string?` | `null` |
| `tierLogoUrl` | `string?` | `null` |


### CustomerDayRestaurantMealsDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "restaurantId": "<UUID من السيرفر>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "logoUrl": null,
  "serviceDate": "<YYYY-MM-DD>",
  "requiredCount": 0,
  "filledCount": 0,
  "slots": [
    {
      "slot": "<string>",
      "slotNameAr": "<string>",
      "slotNameEn": "<string>",
      "count": 0,
      "page": 0,
      "pageSize": 0,
      "totalCount": 0,
      "totalPages": 0,
      "hasNextPage": false,
      "hasPreviousPage": false,
      "meals": [
        {
          "mealId": "<UUID من السيرفر>",
          "nameAr": "<string>",
          "nameEn": "<string>",
          "slot": "<string>",
          "slotNameAr": "<string>",
          "slotNameEn": "<string>",
          "sortOrder": 0,
          "imageUrl": null,
          "calories": null,
          "proteinG": null,
          "carbsG": null,
          "fatG": null,
          "descriptionAr": null,
          "descriptionEn": null,
          "presentAllergenCodes": [
            "<string>"
          ],
          "mayContainAllergenCodes": [
            "<string>"
          ],
          "dietaryPreferenceCodes": [
            "<string>"
          ],
          "ingredients": [
            {
              "id": "<UUID من السيرفر>",
              "nameAr": "<string>",
              "nameEn": "<string>",
              "originCountryCode": null,
              "supplierName": null,
              "isLocal": false,
              "isHalalCertified": false,
              "allergenCodes": [
                "<string>"
              ],
              "isCore": false,
              "iconUrl": null
            }
          ],
          "blockReason": null,
          "isSelected": false,
          "proteinPercentage": null,
          "carbsPercentage": null,
          "fatPercentage": null,
          "caloriesPercentage": null,
          "allergens": null,
          "allergensAr": null,
          "allergensEn": null,
          "allergenDetails": null
        }
      ]
    }
  ],
  "coreIngredients": [
    {
      "id": "<UUID من السيرفر>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "iconUrl": null,
      "isLocal": false,
      "originCountryCode": null,
      "originCountryNameAr": null,
      "originCountryNameEn": null,
      "flagEmoji": null
    }
  ],
  "selectedMealIds": [
    "<UUID من السيرفر>"
  ],
  "rating": null,
  "ratingCount": 0,
  "deliveryWindowStart": null,
  "deliveryWindowEnd": null,
  "deliveryTimeText": null,
  "tier": null,
  "tierNameAr": null,
  "tierNameEn": null,
  "tierName": null,
  "tierLogoUrl": null,
  "programNameAr": null,
  "programNameEn": null,
  "programName": null,
  "nutritionTotals": null,
  "caloriesPercentage": null,
  "proteinPercentage": null,
  "carbsPercentage": null,
  "fatPercentage": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `restaurantId` | `Guid` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `logoUrl` | `string?` | — |
| `serviceDate` | `DateOnly` | — |
| `requiredCount` | `int` | — |
| `filledCount` | `int` | — |
| `slots` | `IReadOnlyList<CustomerCalendarSlotMealsDto>` | — |
| `coreIngredients` | `IReadOnlyList<CustomerRestaurantCoreIngredientDto>` | — |
| `selectedMealIds` | `IReadOnlyList<Guid>` | — |
| `rating` | `decimal?` | `null` |
| `ratingCount` | `int` | `0` |
| `deliveryWindowStart` | `TimeOnly?` | `null` |
| `deliveryWindowEnd` | `TimeOnly?` | `null` |
| `deliveryTimeText` | `string?` | `null` |
| `tier` | `RestaurantTier?` | `null` |
| `tierNameAr` | `string?` | `null` |
| `tierNameEn` | `string?` | `null` |
| `tierName` | `string?` | `null` |
| `tierLogoUrl` | `string?` | `null` |
| `programNameAr` | `string?` | `null` |
| `programNameEn` | `string?` | `null` |
| `programName` | `string?` | `null` |
| `nutritionTotals` | `CustomerDayNutritionTotalsDto?` | `null` |
| `caloriesPercentage` | `decimal?` | `null` |
| `proteinPercentage` | `decimal?` | `null` |
| `carbsPercentage` | `decimal?` | `null` |
| `fatPercentage` | `decimal?` | `null` |


### CustomerDayRestaurantsDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "serviceDate": "<YYYY-MM-DD>",
  "limit": 0,
  "adminCeiling": 0,
  "restaurants": [
    {
      "restaurantId": "<UUID من السيرفر>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "logoUrl": null,
      "tier": "<Basic | Platinum | Elite>",
      "usedCount": 0,
      "limit": 0,
      "remainingCount": 0,
      "isBusy": false,
      "isQuotaExhausted": false,
      "isEnabled": false,
      "disabledReason": null,
      "safeMealCount": 0,
      "rating": null,
      "ratingCount": 0,
      "deliveryWindowStart": null,
      "deliveryWindowEnd": null,
      "deliveryTimeText": null,
      "tierNameAr": null,
      "tierNameEn": null,
      "tierName": null,
      "tierLogoUrl": null
    }
  ],
  "page": 0,
  "pageSize": 0,
  "totalCount": 0,
  "totalPages": 0,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `serviceDate` | `DateOnly` | — |
| `limit` | `int` | — |
| `adminCeiling` | `int` | — |
| `restaurants` | `IReadOnlyList<CustomerDayRestaurantDto>` | — |
| `page` | `int` | `1` |
| `pageSize` | `int` | `0` |
| `totalCount` | `int` | `0` |
| `totalPages` | `int` | `0` |
| `hasNextPage` | `bool` | `false` |
| `hasPreviousPage` | `bool` | `false` |


### CustomerMealIngredientDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "id": "<UUID من السيرفر>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "originCountryCode": null,
  "supplierName": null,
  "isLocal": false,
  "isHalalCertified": false,
  "allergenCodes": [
    "<string>"
  ],
  "isCore": false,
  "iconUrl": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `id` | `Guid` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `originCountryCode` | `string?` | — |
| `supplierName` | `string?` | — |
| `isLocal` | `bool` | — |
| `isHalalCertified` | `bool` | — |
| `allergenCodes` | `IReadOnlyList<string>` | — |
| `isCore` | `bool` | `false` |
| `iconUrl` | `string?` | `null` |


### CustomerRestaurantCoreIngredientDto

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "id": "<UUID من السيرفر>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "iconUrl": null,
  "isLocal": false,
  "originCountryCode": null,
  "originCountryNameAr": null,
  "originCountryNameEn": null,
  "flagEmoji": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `id` | `Guid` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `iconUrl` | `string?` | — |
| `isLocal` | `bool` | `false` |
| `originCountryCode` | `string?` | `null` |
| `originCountryNameAr` | `string?` | `null` |
| `originCountryNameEn` | `string?` | `null` |
| `flagEmoji` | `string?` | `null` |


### SelectDayRestaurantRequest

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "restaurantId": "<UUID من السيرفر>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `restaurantId` | `Guid` | — |


### SelectDaySlotMealRequest

المصدر: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs`.

```json
{
  "mealId": "<UUID من السيرفر>",
  "slot": "<string>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `mealId` | `Guid` | — |
| `slot` | `string` | — |

