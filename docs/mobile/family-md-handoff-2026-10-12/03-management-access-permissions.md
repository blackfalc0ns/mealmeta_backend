# إدارة العيلة والحساب والصلاحيات

## ما تغير وطريقة الربط

GET home/members/members/{memberId} على /api/v1/customer/family يعرض lifecycle منفصلًا: setupStatus، checkoutSelected، paymentStatus، fulfillmentStatus، serviceStatus، accessStatus، displayStatus. لا تختصر هذه المحاور في Active واحد. memberSubscriptionId الحالي مختلف عن upcomingSubscriptionId؛ linkedCustomerUserId nullable ومختلف عن memberId.

GET/PUT access يتحكم في الحساب المستقل والإيقاف والإزالة فقط. isPaused لا يجمّد الخدمة، وremove لا يثبت الإلغاء أو الاسترداد. لا تسجل generatedPassword أو invite token في logs/analytics.

GET permissions ثم PUT permissions مع expectedVersion=version الحالي. عند family.permissions_changed أعد القراءة واطلب إعادة تطبيق اختيار المستخدم، دون overwrite صامت. المدير يحتفظ بصلاحيات إدارة العمليات المدعومة، وصلاحيات الفرد يحددها المدير. المصدر يسمح أيضًا ببعض عمليات الفرد عند منح المفتاح المناسب؛ لا تفترض حظر كل كتابة للفرد أو السماح بها، بل اعتمد capabilities ورفض السيرفر.

viewCalendar/viewProgram/viewInvoices تخص قراءة الفرد لنفسه. viewFamilyCalendars/viewFamilyPlans/viewFamilyInvoices تمنح نطاق العيلة لكل مورد مستقل. قراءة التقويم لا تمنح تعديله. GET capabilities يرجع array من action/allowed/reasonCode/requiredPermission؛ action وrequiredPermission قد يستعملان أسماء PascalCase الأصلية. لا تحوّلهما إلى identifiers جديدة. قدرات editMealsAndDays/changeProgram/changeDeliveryTime/unitExchange غير متاحة حتى للمدير في هذه النسخة.

هذه **أشكال JSON توضيحية** مأخوذة من DTOs، وليست بيانات أعمال أو requests جاهزة للإرسال. استبدل الرموز بقيم السيرفر. الصفر وfalse يوضحان نوع القيمة فقط وليسا defaults أو قرارات صلاحية. الحقول nullable تظهر null هنا؛ راجع نوعها في الجدول لمعرفة القيمة عند وجودها. عنصر array يشرح نوع العنصر؛ الاستجابة الحقيقية قد ترجع []. IDs لا تُولّد بدل IDs السيرفر. Defaults موضحة منفصلة في الجداول. enums تُرسل بأسمائها الأصلية، والحقول camelCase. بعض حقول الاستجابة قد تُحذف عند null وفق خيارات serializer؛ العميل يتحمل null والغياب.

### AcceptFamilyInviteCommand

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "token": "<string>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `token` | `string` | — |


### CreateFamilyMemberCommand

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "request": {
    "displayName": "<string>",
    "relationshipCode": "<string>",
    "gender": "<Male | Female>",
    "ageYears": 0,
    "nutritionalGoalCode": "<string>",
    "activityLevel": "<Sedentary | Moderate | Active>",
    "programId": null,
    "bundleId": null,
    "selectedTier": null,
    "durationDays": null,
    "includeFriday": false
  }
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `request` | `CreateFamilyMemberRequest` | — |


### CreateFamilyMemberRequest

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "displayName": "<string>",
  "relationshipCode": "<string>",
  "gender": "<Male | Female>",
  "ageYears": 0,
  "nutritionalGoalCode": "<string>",
  "activityLevel": "<Sedentary | Moderate | Active>",
  "programId": null,
  "bundleId": null,
  "selectedTier": null,
  "durationDays": null,
  "includeFriday": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `displayName` | `string` | — |
| `relationshipCode` | `string` | — |
| `gender` | `FamilyMemberGender` | — |
| `ageYears` | `int` | — |
| `nutritionalGoalCode` | `string` | — |
| `activityLevel` | `FamilyActivityLevel` | — |
| `programId` | `Guid?` | — |
| `bundleId` | `Guid?` | — |
| `selectedTier` | `RestaurantTier?` | — |
| `durationDays` | `int?` | — |
| `includeFriday` | `bool` | — |


### FamilyCapabilityDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "action": "<string>",
  "allowed": false,
  "reasonCode": null,
  "requiredPermission": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `action` | `string` | — |
| `allowed` | `bool` | — |
| `reasonCode` | `string?` | — |
| `requiredPermission` | `string?` | — |


### FamilyHomeDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "groupId": "<UUID من السيرفر>",
  "name": "<string>",
  "status": "<Draft | CheckoutPending | Active | Frozen | Dispute | PendingDetach | Cancelled>",
  "packageLabelAr": null,
  "packageLabelEn": null,
  "quotedFinalPrice": 0,
  "currencyCode": "<string>",
  "members": [
    {
      "memberId": "<UUID من السيرفر>",
      "displayName": "<string>",
      "role": "<Manager | Member>",
      "isPaused": false,
      "memberSubscriptionId": null,
      "programLabelAr": null,
      "programLabelEn": null,
      "todayUiStatus": null,
      "todayFilledCount": null,
      "todayRequiredCount": null,
      "remainingDays": 0,
      "lifecycle": null,
      "upcomingSubscriptionId": null,
      "upcomingStartDate": null
    }
  ],
  "todaySchedule": [
    {
      "memberId": "<UUID من السيرفر>",
      "displayName": "<string>",
      "role": "<Manager | Member>",
      "isPaused": false,
      "memberSubscriptionId": null,
      "programLabelAr": null,
      "programLabelEn": null,
      "todayUiStatus": null,
      "todayFilledCount": null,
      "todayRequiredCount": null,
      "remainingDays": 0,
      "lifecycle": null,
      "upcomingSubscriptionId": null,
      "upcomingStartDate": null
    }
  ],
  "shortcuts": [
    {
      "code": "<string>",
      "enabled": false,
      "unavailableReason": null
    }
  ],
  "wallet": {
    "available": false,
    "unavailableReason": null
  }
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `groupId` | `Guid` | — |
| `name` | `string` | — |
| `status` | `FamilyGroupStatus` | — |
| `packageLabelAr` | `string?` | — |
| `packageLabelEn` | `string?` | — |
| `quotedFinalPrice` | `decimal` | — |
| `currencyCode` | `string` | — |
| `members` | `IReadOnlyList<FamilyHomeMemberDto>` | — |
| `todaySchedule` | `IReadOnlyList<FamilyHomeMemberDto>` | — |
| `shortcuts` | `IReadOnlyList<FamilyShortcutDto>` | — |
| `wallet` | `FamilyWalletDto` | — |


### FamilyHomeMemberDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "memberId": "<UUID من السيرفر>",
  "displayName": "<string>",
  "role": "<Manager | Member>",
  "isPaused": false,
  "memberSubscriptionId": null,
  "programLabelAr": null,
  "programLabelEn": null,
  "todayUiStatus": null,
  "todayFilledCount": null,
  "todayRequiredCount": null,
  "remainingDays": 0,
  "lifecycle": null,
  "upcomingSubscriptionId": null,
  "upcomingStartDate": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |
| `displayName` | `string` | — |
| `role` | `FamilyMemberRole` | — |
| `isPaused` | `bool` | — |
| `memberSubscriptionId` | `Guid?` | — |
| `programLabelAr` | `string?` | — |
| `programLabelEn` | `string?` | — |
| `todayUiStatus` | `string?` | — |
| `todayFilledCount` | `int?` | — |
| `todayRequiredCount` | `int?` | — |
| `remainingDays` | `int` | `0` |
| `lifecycle` | `FamilyMemberLifecycleDto?` | `null` |
| `upcomingSubscriptionId` | `Guid?` | `null` |
| `upcomingStartDate` | `DateOnly?` | `null` |


### FamilyMemberAccessDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "memberId": "<UUID من السيرفر>",
  "hasSeparateAccount": false,
  "linkedCustomerUserId": null,
  "isPaused": false,
  "pausedAtUtc": null,
  "removedAtUtc": null,
  "inviteDeepLink": null,
  "inviteExpiresAtUtc": null,
  "phoneNumber": null,
  "hasExistingLogin": false,
  "generatedPassword": null,
  "countryCode": null,
  "nationalPhoneNumber": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |
| `hasSeparateAccount` | `bool` | — |
| `linkedCustomerUserId` | `Guid?` | — |
| `isPaused` | `bool` | — |
| `pausedAtUtc` | `DateTime?` | — |
| `removedAtUtc` | `DateTime?` | — |
| `inviteDeepLink` | `string?` | — |
| `inviteExpiresAtUtc` | `DateTime?` | — |
| `phoneNumber` | `string?` | `null` |
| `hasExistingLogin` | `bool` | `false` |
| `generatedPassword` | `string?` | `null` |
| `countryCode` | `string?` | `null` |
| `nationalPhoneNumber` | `string?` | `null` |


### FamilyMemberDetailDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "memberId": "<UUID من السيرفر>",
  "displayName": "<string>",
  "role": "<Manager | Member>",
  "relationshipCode": "<string>",
  "gender": "<Male | Female>",
  "ageYears": 0,
  "nutritionalGoalCode": "<string>",
  "activityLevel": "<Sedentary | Moderate | Active>",
  "setupStatus": "<NotStarted | InProgress | Ready>",
  "programId": null,
  "bundleId": null,
  "selectedTier": null,
  "durationDays": null,
  "includeFriday": false,
  "memberSubscriptionId": null,
  "programNameAr": null,
  "programNameEn": null,
  "isPaused": false,
  "hasSeparateAccount": false,
  "linkedCustomerUserId": null,
  "phoneNumber": null,
  "remainingDays": 0,
  "startDate": null,
  "endDate": null,
  "canRenew": false,
  "renewalPath": null,
  "todayNutrition": null,
  "countryCode": null,
  "nationalPhoneNumber": null,
  "lifecycle": null,
  "upcomingSubscriptionId": null,
  "upcomingStartDate": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |
| `displayName` | `string` | — |
| `role` | `FamilyMemberRole` | — |
| `relationshipCode` | `string` | — |
| `gender` | `FamilyMemberGender` | — |
| `ageYears` | `int` | — |
| `nutritionalGoalCode` | `string` | — |
| `activityLevel` | `FamilyActivityLevel` | — |
| `setupStatus` | `FamilyMemberSetupStatus` | — |
| `programId` | `Guid?` | — |
| `bundleId` | `Guid?` | — |
| `selectedTier` | `RestaurantTier?` | — |
| `durationDays` | `int?` | — |
| `includeFriday` | `bool` | — |
| `memberSubscriptionId` | `Guid?` | — |
| `programNameAr` | `string?` | — |
| `programNameEn` | `string?` | — |
| `isPaused` | `bool` | — |
| `hasSeparateAccount` | `bool` | — |
| `linkedCustomerUserId` | `Guid?` | — |
| `phoneNumber` | `string?` | `null` |
| `remainingDays` | `int` | `0` |
| `startDate` | `DateOnly?` | `null` |
| `endDate` | `DateOnly?` | `null` |
| `canRenew` | `bool` | `false` |
| `renewalPath` | `string?` | `null` |
| `todayNutrition` | `FamilyTodayNutritionDto?` | `null` |
| `countryCode` | `string?` | `null` |
| `nationalPhoneNumber` | `string?` | `null` |
| `lifecycle` | `FamilyMemberLifecycleDto?` | `null` |
| `upcomingSubscriptionId` | `Guid?` | `null` |
| `upcomingStartDate` | `DateOnly?` | `null` |


### FamilyMemberLifecycleDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "setupStatus": "<NotStarted | InProgress | Ready>",
  "checkoutSelected": false,
  "paymentStatus": null,
  "fulfillmentStatus": "<string>",
  "serviceStatus": null,
  "accessStatus": "<string>",
  "displayStatus": "<string>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `setupStatus` | `FamilyMemberSetupStatus` | — |
| `checkoutSelected` | `bool` | — |
| `paymentStatus` | `string?` | — |
| `fulfillmentStatus` | `string` | — |
| `serviceStatus` | `string?` | — |
| `accessStatus` | `string` | — |
| `displayStatus` | `string` | — |


### FamilyMemberListItemDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "memberId": "<UUID من السيرفر>",
  "displayName": "<string>",
  "role": "<Manager | Member>",
  "ageYears": 0,
  "isPaused": false,
  "memberSubscriptionId": null,
  "programNameAr": null,
  "programNameEn": null,
  "remainingDays": 0,
  "setupStatus": "<NotStarted | InProgress | Ready>",
  "lifecycle": null,
  "upcomingSubscriptionId": null,
  "upcomingStartDate": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |
| `displayName` | `string` | — |
| `role` | `FamilyMemberRole` | — |
| `ageYears` | `int` | — |
| `isPaused` | `bool` | — |
| `memberSubscriptionId` | `Guid?` | — |
| `programNameAr` | `string?` | — |
| `programNameEn` | `string?` | — |
| `remainingDays` | `int` | — |
| `setupStatus` | `FamilyMemberSetupStatus` | — |
| `lifecycle` | `FamilyMemberLifecycleDto?` | `null` |
| `upcomingSubscriptionId` | `Guid?` | `null` |
| `upcomingStartDate` | `DateOnly?` | `null` |


### FamilyMemberPermissionsDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "memberId": "<UUID من السيرفر>",
  "editDailyMeals": false,
  "changeDeliveryTime": false,
  "viewProgram": false,
  "changeProgram": false,
  "editMealsAndDays": false,
  "editAddress": false,
  "unitExchange": false,
  "changePassword": false,
  "groups": [
    {
      "code": "<string>",
      "labelAr": "<string>",
      "labelEn": "<string>",
      "permissions": [
        {
          "key": "<string>",
          "labelAr": "<string>",
          "labelEn": "<string>",
          "value": false
        }
      ]
    }
  ],
  "version": 0,
  "viewCalendar": false,
  "viewInvoices": false,
  "selectRestaurant": false,
  "confirmMeals": false,
  "requestFreeze": false,
  "requestRenewal": false,
  "requestUpgrade": false,
  "cancelSubscription": false,
  "viewFamilyCalendars": false,
  "viewFamilyPlans": false,
  "viewFamilyInvoices": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |
| `editDailyMeals` | `bool` | — |
| `changeDeliveryTime` | `bool` | — |
| `viewProgram` | `bool` | — |
| `changeProgram` | `bool` | — |
| `editMealsAndDays` | `bool` | — |
| `editAddress` | `bool` | — |
| `unitExchange` | `bool` | — |
| `changePassword` | `bool` | — |
| `groups` | `IReadOnlyList<FamilyPermissionGroupDto>` | — |
| `version` | `long` | `0` |
| `viewCalendar` | `bool` | `false` |
| `viewInvoices` | `bool` | `false` |
| `selectRestaurant` | `bool` | `false` |
| `confirmMeals` | `bool` | `false` |
| `requestFreeze` | `bool` | `false` |
| `requestRenewal` | `bool` | `false` |
| `requestUpgrade` | `bool` | `false` |
| `cancelSubscription` | `bool` | `false` |
| `viewFamilyCalendars` | `bool` | `false` |
| `viewFamilyPlans` | `bool` | `false` |
| `viewFamilyInvoices` | `bool` | `false` |


### FamilyPermissionGroupDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "code": "<string>",
  "labelAr": "<string>",
  "labelEn": "<string>",
  "permissions": [
    {
      "key": "<string>",
      "labelAr": "<string>",
      "labelEn": "<string>",
      "value": false
    }
  ]
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `code` | `string` | — |
| `labelAr` | `string` | — |
| `labelEn` | `string` | — |
| `permissions` | `IReadOnlyList<FamilyPermissionItemDto>` | — |


### FamilyPermissionItemDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "key": "<string>",
  "labelAr": "<string>",
  "labelEn": "<string>",
  "value": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `key` | `string` | — |
| `labelAr` | `string` | — |
| `labelEn` | `string` | — |
| `value` | `bool` | — |


### FamilyShortcutDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "code": "<string>",
  "enabled": false,
  "unavailableReason": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `code` | `string` | — |
| `enabled` | `bool` | — |
| `unavailableReason` | `string?` | — |


### FamilyTodayNutritionDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "calories": 0,
  "proteinG": 0,
  "carbsG": 0,
  "fatG": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `calories` | `decimal` | — |
| `proteinG` | `decimal` | — |
| `carbsG` | `decimal` | — |
| `fatG` | `decimal` | — |


### FamilyWalletDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "available": false,
  "unavailableReason": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `available` | `bool` | — |
| `unavailableReason` | `string?` | — |


### GetFamilyMemberAccessQuery

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "memberId": "<UUID من السيرفر>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |


### GetFamilyMemberPermissionsQuery

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "memberId": "<UUID من السيرفر>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |


### GetFamilyMemberQuery

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "memberId": "<UUID من السيرفر>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |


### GetMyFamilyCapabilitiesQuery

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "memberId": "<UUID من السيرفر>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |


### LocalizedPairDto

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "ar": "<string>",
  "en": "<string>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `ar` | `string` | — |
| `en` | `string` | — |


### UpdateFamilyMemberAccessCommand

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "memberId": "<UUID من السيرفر>",
  "request": {
    "hasSeparateAccount": null,
    "isPaused": null,
    "remove": null
  }
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |
| `request` | `UpdateFamilyMemberAccessRequest` | — |


### UpdateFamilyMemberAccessRequest

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "hasSeparateAccount": null,
  "isPaused": null,
  "remove": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `hasSeparateAccount` | `bool?` | — |
| `isPaused` | `bool?` | — |
| `remove` | `bool?` | — |


### UpdateFamilyMemberPermissionsCommand

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "memberId": "<UUID من السيرفر>",
  "request": {
    "editDailyMeals": false,
    "changeDeliveryTime": false,
    "viewProgram": false,
    "changeProgram": false,
    "editMealsAndDays": false,
    "editAddress": false,
    "unitExchange": false,
    "changePassword": false,
    "expectedVersion": null,
    "viewCalendar": null,
    "viewInvoices": null,
    "selectRestaurant": null,
    "confirmMeals": null,
    "requestFreeze": null,
    "requestRenewal": null,
    "requestUpgrade": null,
    "cancelSubscription": null,
    "viewFamilyCalendars": null,
    "viewFamilyPlans": null,
    "viewFamilyInvoices": null
  }
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |
| `request` | `UpdateFamilyMemberPermissionsRequest` | — |


### UpdateFamilyMemberPermissionsRequest

المصدر: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`.

```json
{
  "editDailyMeals": false,
  "changeDeliveryTime": false,
  "viewProgram": false,
  "changeProgram": false,
  "editMealsAndDays": false,
  "editAddress": false,
  "unitExchange": false,
  "changePassword": false,
  "expectedVersion": null,
  "viewCalendar": null,
  "viewInvoices": null,
  "selectRestaurant": null,
  "confirmMeals": null,
  "requestFreeze": null,
  "requestRenewal": null,
  "requestUpgrade": null,
  "cancelSubscription": null,
  "viewFamilyCalendars": null,
  "viewFamilyPlans": null,
  "viewFamilyInvoices": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `editDailyMeals` | `bool` | — |
| `changeDeliveryTime` | `bool` | — |
| `viewProgram` | `bool` | — |
| `changeProgram` | `bool` | — |
| `editMealsAndDays` | `bool` | — |
| `editAddress` | `bool` | — |
| `unitExchange` | `bool` | — |
| `changePassword` | `bool` | — |
| `expectedVersion` | `long?` | `null` |
| `viewCalendar` | `bool?` | `null` |
| `viewInvoices` | `bool?` | `null` |
| `selectRestaurant` | `bool?` | `null` |
| `confirmMeals` | `bool?` | `null` |
| `requestFreeze` | `bool?` | `null` |
| `requestRenewal` | `bool?` | `null` |
| `requestUpgrade` | `bool?` | `null` |
| `cancelSubscription` | `bool?` | `null` |
| `viewFamilyCalendars` | `bool?` | `null` |
| `viewFamilyPlans` | `bool?` | `null` |
| `viewFamilyInvoices` | `bool?` | `null` |

