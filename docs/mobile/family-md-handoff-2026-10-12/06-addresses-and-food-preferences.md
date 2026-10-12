# العناوين والتفضيلات والحساسية

## طريقة الربط

عناوين الفرد قبل الشراء تحت family-subscription-flow/members/{memberId}/addresses، مع preview-coverage وset-default. تعديل عنوان profile أو default ليس تعديل عنوان أيام الاشتراك المدفوع؛ ذلك عبر delivery-address في ملف 05.

كتالوج allergies/disliked-ingredients/dietary وfood-preferences/catalog هو مصدر الأكواد. PUT member allergies يمكن إرسال selectedCodes، وdisliked-ingredients ingredientCodes، وdietary dietaryPreferenceCodes. [] تعني عدم اختيار بعد إكمال الشاشة، وهي مختلفة عن عدم إكمال الشاشة. completed وhasNoAllergies/hasNoDietaryRestriction مرجع العرض، لا تخمّنها من قائمة فارغة فقط.

هذه **أشكال JSON توضيحية** مأخوذة من DTOs، وليست بيانات أعمال أو requests جاهزة للإرسال. استبدل الرموز بقيم السيرفر. الصفر وfalse يوضحان نوع القيمة فقط وليسا defaults أو قرارات صلاحية. الحقول nullable تظهر null هنا؛ راجع نوعها في الجدول لمعرفة القيمة عند وجودها. عنصر array يشرح نوع العنصر؛ الاستجابة الحقيقية قد ترجع []. IDs لا تُولّد بدل IDs السيرفر. Defaults موضحة منفصلة في الجداول. enums تُرسل بأسمائها الأصلية، والحقول camelCase. بعض حقول الاستجابة قد تُحذف عند null وفق خيارات serializer؛ العميل يتحمل null والغياب.

### AddressCoveragePreviewDto

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerAddresses.cs`.

```json
{
  "canSetAsDefault": false,
  "isCurrentSubscriptionRestaurantCovered": false,
  "uncoveredRestaurantNameAr": null,
  "uncoveredRestaurantNameEn": null,
  "conflictMessage": null,
  "resolvedRegionId": null,
  "resolvedRegionNameAr": null,
  "resolvedRegionNameEn": null,
  "governorate": null,
  "area": null,
  "block": null,
  "latitude": null,
  "longitude": null,
  "restaurantCoverageNotice": null,
  "servingRestaurants": [
    {
      "id": "<UUID من السيرفر>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "logoUrl": null,
      "rating": null
    }
  ],
  "hasActiveSubscription": false,
  "pagination": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `canSetAsDefault` | `bool` | — |
| `isCurrentSubscriptionRestaurantCovered` | `bool` | — |
| `uncoveredRestaurantNameAr` | `string?` | — |
| `uncoveredRestaurantNameEn` | `string?` | — |
| `conflictMessage` | `string?` | — |
| `resolvedRegionId` | `Guid?` | — |
| `resolvedRegionNameAr` | `string?` | — |
| `resolvedRegionNameEn` | `string?` | — |
| `governorate` | `string?` | — |
| `area` | `string?` | — |
| `block` | `string?` | — |
| `latitude` | `decimal?` | — |
| `longitude` | `decimal?` | — |
| `restaurantCoverageNotice` | `LocalizedTextDto?` | — |
| `servingRestaurants` | `IReadOnlyList<AddressServingRestaurantDto>` | — |
| `hasActiveSubscription` | `bool` | `false` |
| `pagination` | `CustomerPaginationMetaDto?` | `null` |


### AddressServingRestaurantDto

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerAddresses.cs`.

```json
{
  "id": "<UUID من السيرفر>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "logoUrl": null,
  "rating": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `id` | `Guid` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `logoUrl` | `string?` | — |
| `rating` | `decimal?` | — |


### AdminCustomerAddressDto

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/GetAdminCustomerDetailQuery.cs`.

```json
{
  "id": "<UUID من السيرفر>",
  "label": "<string>",
  "addressLine": "<string>",
  "governorate": null,
  "area": null,
  "block": null,
  "street": null,
  "avenue": null,
  "building": null,
  "unit": null,
  "floor": null,
  "latitude": null,
  "longitude": null,
  "notes": null,
  "isDefault": false,
  "notifyOnDelivery": false,
  "isActiveDelivery": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `id` | `Guid` | — |
| `label` | `string` | — |
| `addressLine` | `string` | — |
| `governorate` | `string?` | — |
| `area` | `string?` | — |
| `block` | `string?` | — |
| `street` | `string?` | — |
| `avenue` | `string?` | — |
| `building` | `string?` | — |
| `unit` | `string?` | — |
| `floor` | `string?` | — |
| `latitude` | `decimal?` | — |
| `longitude` | `decimal?` | — |
| `notes` | `string?` | — |
| `isDefault` | `bool` | — |
| `notifyOnDelivery` | `bool` | — |
| `isActiveDelivery` | `bool` | — |


### CustomerAddressDto

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerAddresses.cs`.

```json
{
  "id": "<UUID من السيرفر>",
  "label": "<string>",
  "addressLine": "<string>",
  "regionId": null,
  "latitude": null,
  "longitude": null,
  "notes": null,
  "isDefault": false,
  "notifyOnDelivery": false,
  "governorate": null,
  "area": null,
  "block": null,
  "street": null,
  "avenue": null,
  "building": null,
  "unit": null,
  "floor": null,
  "blockRegionId": null,
  "isDeliveryActive": false,
  "deliveryEffectiveFromUtc": null,
  "deliveryChangeNotice": null,
  "restaurantCoverageNotice": null,
  "servingRestaurants": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `id` | `Guid` | — |
| `label` | `string` | — |
| `addressLine` | `string` | — |
| `regionId` | `Guid?` | — |
| `latitude` | `decimal?` | — |
| `longitude` | `decimal?` | — |
| `notes` | `string?` | — |
| `isDefault` | `bool` | — |
| `notifyOnDelivery` | `bool` | — |
| `governorate` | `string?` | — |
| `area` | `string?` | — |
| `block` | `string?` | — |
| `street` | `string?` | — |
| `avenue` | `string?` | — |
| `building` | `string?` | — |
| `unit` | `string?` | — |
| `floor` | `string?` | — |
| `blockRegionId` | `Guid?` | `null` |
| `isDeliveryActive` | `bool` | `false` |
| `deliveryEffectiveFromUtc` | `DateTime?` | `null` |
| `deliveryChangeNotice` | `LocalizedTextDto?` | `null` |
| `restaurantCoverageNotice` | `LocalizedTextDto?` | `null` |
| `servingRestaurants` | `IReadOnlyList<AddressServingRestaurantDto>?` | `null` |


### CustomerAddressRequest

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerAddresses.cs`.

```json
{
  "label": "<string>",
  "addressLine": "<string>",
  "regionId": null,
  "latitude": null,
  "longitude": null,
  "notes": null,
  "isDefault": false,
  "notifyOnDelivery": false,
  "governorate": null,
  "area": null,
  "block": null,
  "street": null,
  "avenue": null,
  "building": null,
  "unit": null,
  "floor": null,
  "blockRegionId": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `label` | `string` | — |
| `addressLine` | `string` | — |
| `regionId` | `Guid?` | — |
| `latitude` | `decimal?` | — |
| `longitude` | `decimal?` | — |
| `notes` | `string?` | — |
| `isDefault` | `bool` | — |
| `notifyOnDelivery` | `bool` | `true` |
| `governorate` | `string?` | `null` |
| `area` | `string?` | `null` |
| `block` | `string?` | `null` |
| `street` | `string?` | `null` |
| `avenue` | `string?` | `null` |
| `building` | `string?` | `null` |
| `unit` | `string?` | `null` |
| `floor` | `string?` | `null` |
| `blockRegionId` | `Guid?` | `null` |


### CustomerDietaryPreferencesScreenDto

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerFoodPreferences.cs`.

```json
{
  "dietaryPreferences": [
    {
      "code": "<string>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "isSelected": false,
      "isNone": false
    }
  ],
  "selectedCount": 0,
  "page": 0,
  "pageSize": 0,
  "totalCount": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `dietaryPreferences` | `IReadOnlyList<DietaryPreferenceOptionDto>` | — |
| `selectedCount` | `int` | — |
| `page` | `int` | — |
| `pageSize` | `int` | — |
| `totalCount` | `int` | — |


### CustomerFoodPreferenceCatalogDto

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerFoodPreferences.cs`.

```json
{
  "ingredients": [
    {
      "code": "<string>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "category": null,
      "allergenCodes": [
        "<string>"
      ],
      "isSelected": false,
      "isNone": false
    }
  ],
  "allergens": [
    {
      "code": "<string>",
      "nameAr": null,
      "nameEn": null,
      "isSelected": false,
      "severity": null,
      "isNone": false
    }
  ],
  "selectedAllergenCount": 0,
  "dietaryPreferences": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `ingredients` | `IReadOnlyList<FoodPreferenceIngredientOptionDto>` | — |
| `allergens` | `IReadOnlyList<FoodPreferenceAllergenOptionDto>` | — |
| `selectedAllergenCount` | `int` | `0` |
| `dietaryPreferences` | `IReadOnlyList<DietaryPreferenceOptionDto>?` | `null` |


### CustomerFoodPreferencesDto

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerFoodPreferences.cs`.

```json
{
  "dietaryPattern": null,
  "allergies": [
    {
      "code": "<string>",
      "nameAr": null,
      "nameEn": null,
      "severity": null
    }
  ],
  "dislikedFoods": [
    {
      "code": "<string>",
      "nameAr": null,
      "nameEn": null,
      "severity": null
    }
  ],
  "notes": null,
  "dietaryPreferenceCodes": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `dietaryPattern` | `string?` | — |
| `allergies` | `IReadOnlyList<FoodPreferenceItemDto>` | — |
| `dislikedFoods` | `IReadOnlyList<FoodPreferenceItemDto>` | — |
| `notes` | `string?` | — |
| `dietaryPreferenceCodes` | `IReadOnlyList<string>?` | `null` |


### DietaryPreferenceOptionDto

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerFoodPreferences.cs`.

```json
{
  "code": "<string>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "isSelected": false,
  "isNone": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `code` | `string` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `isSelected` | `bool` | `false` |
| `isNone` | `bool` | `false` |


### FoodPreferenceAllergenOptionDto

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerFoodPreferences.cs`.

```json
{
  "code": "<string>",
  "nameAr": null,
  "nameEn": null,
  "isSelected": false,
  "severity": null,
  "isNone": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `code` | `string` | — |
| `nameAr` | `string?` | — |
| `nameEn` | `string?` | — |
| `isSelected` | `bool` | `false` |
| `severity` | `string?` | `null` |
| `isNone` | `bool` | `false` |


### FoodPreferenceIngredientOptionDto

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerFoodPreferences.cs`.

```json
{
  "code": "<string>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "category": null,
  "allergenCodes": [
    "<string>"
  ],
  "isSelected": false,
  "isNone": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `code` | `string` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `category` | `string?` | — |
| `allergenCodes` | `IReadOnlyList<string>` | — |
| `isSelected` | `bool` | `false` |
| `isNone` | `bool` | `false` |


### FoodPreferenceItemDto

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerFoodPreferences.cs`.

```json
{
  "code": "<string>",
  "nameAr": null,
  "nameEn": null,
  "severity": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `code` | `string` | — |
| `nameAr` | `string?` | — |
| `nameEn` | `string?` | — |
| `severity` | `string?` | — |


### PreviewAddressCoverageRequest

المصدر: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerAddresses.cs`.

```json
{
  "addressId": null,
  "regionId": null,
  "blockRegionId": null,
  "latitude": null,
  "longitude": null,
  "governorate": null,
  "area": null,
  "block": null,
  "isDefault": false,
  "page": null,
  "pageSize": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `addressId` | `Guid?` | `null` |
| `regionId` | `Guid?` | `null` |
| `blockRegionId` | `Guid?` | `null` |
| `latitude` | `decimal?` | `null` |
| `longitude` | `decimal?` | `null` |
| `governorate` | `string?` | `null` |
| `area` | `string?` | `null` |
| `block` | `string?` | `null` |
| `isDefault` | `bool` | `true` |
| `page` | `int?` | `null` |
| `pageSize` | `int?` | `null` |

