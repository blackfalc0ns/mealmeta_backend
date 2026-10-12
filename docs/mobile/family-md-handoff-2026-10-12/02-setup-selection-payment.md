# تجهيز العيلة والاختيار والدفع

## ما تغير وطريقة الربط

إضافة فرد أو اكتمال الخطة لا تختار شراءه. استعمل PUT members/{memberId}/checkout-selection مع {"subscribeNow":true} أو false. احتفظ batchId/batchVersion من الاستجابة واقرأ summary بعد أي تعديل. rosterCount ليس عدد المشتركين؛ selectedNewSubscriberCount هو اختيار الشراء الجديد. الفرد المؤجل يبقى ظاهرًا دون خدمة مفعلة. لا تستعمل مسار resume الفردي للعيلة.

GET metadata ينشر supportedContractVersion وminimumInvoiceContractVersion. أرسل clientContractVersion=1 في الفواتير الجديدة. GET current وPUT group وPOST/PUT members وPUT plan تعيد FamilyGroupScreenDto. PUT plan هو partial patch: الحقل المحذوف يحتفظ بقيمته، null يمسح القيمة؛ programId/bundleId/tier/durationDays/includeFriday/startDate مدعومة، selectedBundleId/selectedTier aliases. لا ترسل aliases المتعارضة.

checkout يعيد رابطًا ووقت حجز وليس إثبات دفع. عودة WebView أو callback العميل ليست مصدر الحقيقة؛ أعد GET current وmanagement/home لمعرفة حالة السيرفر. أعد نفس idempotencyKey لنفس النية فقط، وعند تغيير الاختيار/السعر أعد summary وأنشئ مفتاح نية جديد. selected بدون linked account يرفض family.member_account_required. لا تولّد حسابًا أو subscriptionId على العميل.

هذه **أشكال JSON توضيحية** مأخوذة من DTOs، وليست بيانات أعمال أو requests جاهزة للإرسال. استبدل الرموز بقيم السيرفر. الصفر وfalse يوضحان نوع القيمة فقط وليسا defaults أو قرارات صلاحية. الحقول nullable تظهر null هنا؛ راجع نوعها في الجدول لمعرفة القيمة عند وجودها. عنصر array يشرح نوع العنصر؛ الاستجابة الحقيقية قد ترجع []. IDs لا تُولّد بدل IDs السيرفر. Defaults موضحة منفصلة في الجداول. enums تُرسل بأسمائها الأصلية، والحقول camelCase. بعض حقول الاستجابة قد تُحذف عند null وفق خيارات serializer؛ العميل يتحمل null والغياب.

### AdminFamilyCancellationRowDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/AdminFamilyGroupQueries.cs`.

```json
{
  "cancellationId": "<UUID من السيرفر>",
  "subscriptionId": "<UUID من السيرفر>",
  "memberId": null,
  "batchId": null,
  "batchLineId": null,
  "actorUserId": null,
  "payerCustomerUserId": null,
  "status": "<string>",
  "requestedAtUtc": "<ISO-8601 UTC>",
  "completedAtUtc": null,
  "countedDays": 0,
  "unusedDays": 0,
  "finalCashRefund": null,
  "currencyCode": null,
  "refundCalculationId": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `cancellationId` | `Guid` | — |
| `subscriptionId` | `Guid` | — |
| `memberId` | `Guid?` | — |
| `batchId` | `Guid?` | — |
| `batchLineId` | `Guid?` | — |
| `actorUserId` | `Guid?` | — |
| `payerCustomerUserId` | `Guid?` | — |
| `status` | `string` | — |
| `requestedAtUtc` | `DateTime` | — |
| `completedAtUtc` | `DateTime?` | — |
| `countedDays` | `int` | — |
| `unusedDays` | `int` | — |
| `finalCashRefund` | `decimal?` | — |
| `currencyCode` | `string?` | — |
| `refundCalculationId` | `Guid?` | — |


### AdminFamilyGroupHistoryRowDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/AdminFamilyGroupQueries.cs`.

```json
{
  "batchId": "<UUID من السيرفر>",
  "batchLineId": "<UUID من السيرفر>",
  "groupId": "<UUID من السيرفر>",
  "memberId": "<UUID من السيرفر>",
  "subscriptionId": null,
  "payerCustomerUserId": "<UUID من السيرفر>",
  "checkoutSelected": false,
  "batchStatus": "<string>",
  "evidenceStatus": "<string>",
  "purpose": "<string>",
  "netPaidAmount": null,
  "currencyCode": "<string>",
  "version": 0,
  "batchCreatedAtUtc": "<ISO-8601 UTC>",
  "lineCreatedAtUtc": "<ISO-8601 UTC>",
  "attempts": [
    {
      "attemptId": "<UUID من السيرفر>",
      "attemptKey": "<string>",
      "gatewayPaymentId": null,
      "gatewayInvoiceId": null,
      "requestedAtUtc": "<ISO-8601 UTC>",
      "verifiedAtUtc": null,
      "version": 0,
      "resultCode": null
    }
  ],
  "receipts": [
    {
      "receiptId": "<UUID من السيرفر>",
      "paymentId": null,
      "invoiceId": null,
      "verifiedAmount": null,
      "currencyCode": null,
      "status": "<string>",
      "verifiedAtUtc": null,
      "processedAtUtc": "<ISO-8601 UTC>"
    }
  ],
  "upgrades": [
    {
      "upgradeId": "<UUID من السيرفر>",
      "receiptId": "<UUID من السيرفر>",
      "actualPayerCustomerUserId": null,
      "paymentId": null,
      "invoiceId": null,
      "verifiedPaidAmount": null,
      "currencyCode": null,
      "status": "<string>",
      "verifiedPaidAtUtc": null,
      "isApplied": false
    }
  ],
  "attemptCount": 0,
  "receiptCount": 0,
  "upgradeCount": 0,
  "childrenTruncated": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `batchId` | `Guid` | — |
| `batchLineId` | `Guid` | — |
| `groupId` | `Guid` | — |
| `memberId` | `Guid` | — |
| `subscriptionId` | `Guid?` | — |
| `payerCustomerUserId` | `Guid` | — |
| `checkoutSelected` | `bool` | — |
| `batchStatus` | `string` | — |
| `evidenceStatus` | `string` | — |
| `purpose` | `string` | — |
| `netPaidAmount` | `decimal?` | — |
| `currencyCode` | `string` | — |
| `version` | `long` | — |
| `batchCreatedAtUtc` | `DateTime` | — |
| `lineCreatedAtUtc` | `DateTime` | — |
| `attempts` | `IReadOnlyList<AdminFamilyPaymentAttemptDto>` | — |
| `receipts` | `IReadOnlyList<AdminFamilyReceiptDto>` | — |
| `upgrades` | `IReadOnlyList<AdminFamilyUpgradeDto>` | — |
| `attemptCount` | `int` | — |
| `receiptCount` | `int` | — |
| `upgradeCount` | `int` | — |
| `childrenTruncated` | `bool` | — |


### AdminFamilyGroupRowDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/AdminFamilyGroupQueries.cs`.

```json
{
  "id": "<UUID من السيرفر>",
  "batchId": null,
  "name": "<string>",
  "managerCustomerUserId": "<UUID من السيرفر>",
  "managerName": "<string>",
  "anchorSubscriptionId": null,
  "programNameAr": "<string>",
  "programNameEn": "<string>",
  "bundleNameAr": "<string>",
  "bundleNameEn": "<string>",
  "tierNameAr": "<string>",
  "tierNameEn": "<string>",
  "memberCount": 0,
  "subscriptionDays": 0,
  "usedDays": 0,
  "activeOrders": 0,
  "status": "<Draft | CheckoutPending | Active | Frozen | Dispute | PendingDetach | Cancelled>",
  "members": [
    {
      "groupId": "<UUID من السيرفر>",
      "memberId": "<UUID من السيرفر>",
      "customerUserId": null,
      "memberSubscriptionId": null,
      "currentSubscriptionId": null,
      "upcomingSubscriptionId": null,
      "batchId": null,
      "displayName": "<string>",
      "role": "<Manager | Member>",
      "setupStatus": "<NotStarted | InProgress | Ready>",
      "checkoutSelectionStatus": "<string>",
      "paymentStatus": null,
      "fulfillmentStatus": "<string>",
      "serviceStatus": "<string>",
      "accountStatus": "<string>",
      "readinessBlocker": null,
      "programNameAr": null,
      "programNameEn": null,
      "bundleNameAr": null,
      "bundleNameEn": null,
      "durationDays": null,
      "usedDays": 0,
      "activeOrders": 0
    }
  ]
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `id` | `Guid` | — |
| `batchId` | `Guid?` | — |
| `name` | `string` | — |
| `managerCustomerUserId` | `Guid` | — |
| `managerName` | `string` | — |
| `anchorSubscriptionId` | `Guid?` | — |
| `programNameAr` | `string` | — |
| `programNameEn` | `string` | — |
| `bundleNameAr` | `string` | — |
| `bundleNameEn` | `string` | — |
| `tierNameAr` | `string` | — |
| `tierNameEn` | `string` | — |
| `memberCount` | `int` | — |
| `subscriptionDays` | `int` | — |
| `usedDays` | `int` | — |
| `activeOrders` | `int` | — |
| `status` | `FamilyGroupStatus` | — |
| `members` | `IReadOnlyList<AdminFamilyMemberRowDto>` | — |


### AdminFamilyGroupStatsDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/AdminFamilyGroupQueries.cs`.

```json
{
  "familyGroups": 0,
  "familyMembers": 0,
  "familyDisputes": 0,
  "familyPendingDetach": 0,
  "draft": 0,
  "checkoutPending": 0,
  "active": 0,
  "frozen": 0,
  "dispute": 0,
  "pendingDetach": 0,
  "cancelled": 0,
  "activeServiceMembers": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `familyGroups` | `int` | — |
| `familyMembers` | `int` | — |
| `familyDisputes` | `int` | — |
| `familyPendingDetach` | `int` | — |
| `draft` | `int` | — |
| `checkoutPending` | `int` | — |
| `active` | `int` | — |
| `frozen` | `int` | — |
| `dispute` | `int` | — |
| `pendingDetach` | `int` | — |
| `cancelled` | `int` | — |
| `activeServiceMembers` | `int` | `0` |


### AdminFamilyMemberRowDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/AdminFamilyGroupQueries.cs`.

```json
{
  "groupId": "<UUID من السيرفر>",
  "memberId": "<UUID من السيرفر>",
  "customerUserId": null,
  "memberSubscriptionId": null,
  "currentSubscriptionId": null,
  "upcomingSubscriptionId": null,
  "batchId": null,
  "displayName": "<string>",
  "role": "<Manager | Member>",
  "setupStatus": "<NotStarted | InProgress | Ready>",
  "checkoutSelectionStatus": "<string>",
  "paymentStatus": null,
  "fulfillmentStatus": "<string>",
  "serviceStatus": "<string>",
  "accountStatus": "<string>",
  "readinessBlocker": null,
  "programNameAr": null,
  "programNameEn": null,
  "bundleNameAr": null,
  "bundleNameEn": null,
  "durationDays": null,
  "usedDays": 0,
  "activeOrders": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `groupId` | `Guid` | — |
| `memberId` | `Guid` | — |
| `customerUserId` | `Guid?` | — |
| `memberSubscriptionId` | `Guid?` | — |
| `currentSubscriptionId` | `Guid?` | — |
| `upcomingSubscriptionId` | `Guid?` | — |
| `batchId` | `Guid?` | — |
| `displayName` | `string` | — |
| `role` | `FamilyMemberRole` | — |
| `setupStatus` | `FamilyMemberSetupStatus` | — |
| `checkoutSelectionStatus` | `string` | — |
| `paymentStatus` | `string?` | — |
| `fulfillmentStatus` | `string` | — |
| `serviceStatus` | `string` | — |
| `accountStatus` | `string` | — |
| `readinessBlocker` | `string?` | — |
| `programNameAr` | `string?` | — |
| `programNameEn` | `string?` | — |
| `bundleNameAr` | `string?` | — |
| `bundleNameEn` | `string?` | — |
| `durationDays` | `int?` | — |
| `usedDays` | `int` | — |
| `activeOrders` | `int` | — |


### AdminFamilyPaymentAttemptDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/AdminFamilyGroupQueries.cs`.

```json
{
  "attemptId": "<UUID من السيرفر>",
  "attemptKey": "<string>",
  "gatewayPaymentId": null,
  "gatewayInvoiceId": null,
  "requestedAtUtc": "<ISO-8601 UTC>",
  "verifiedAtUtc": null,
  "version": 0,
  "resultCode": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `attemptId` | `Guid` | — |
| `attemptKey` | `string` | — |
| `gatewayPaymentId` | `string?` | — |
| `gatewayInvoiceId` | `string?` | — |
| `requestedAtUtc` | `DateTime` | — |
| `verifiedAtUtc` | `DateTime?` | — |
| `version` | `long` | — |
| `resultCode` | `string?` | — |


### AdminFamilyReceiptDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/AdminFamilyGroupQueries.cs`.

```json
{
  "receiptId": "<UUID من السيرفر>",
  "paymentId": null,
  "invoiceId": null,
  "verifiedAmount": null,
  "currencyCode": null,
  "status": "<string>",
  "verifiedAtUtc": null,
  "processedAtUtc": "<ISO-8601 UTC>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `receiptId` | `Guid` | — |
| `paymentId` | `string?` | — |
| `invoiceId` | `string?` | — |
| `verifiedAmount` | `decimal?` | — |
| `currencyCode` | `string?` | — |
| `status` | `string` | — |
| `verifiedAtUtc` | `DateTime?` | — |
| `processedAtUtc` | `DateTime` | — |


### AdminFamilyUpgradeDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/AdminFamilyGroupQueries.cs`.

```json
{
  "upgradeId": "<UUID من السيرفر>",
  "receiptId": "<UUID من السيرفر>",
  "actualPayerCustomerUserId": null,
  "paymentId": null,
  "invoiceId": null,
  "verifiedPaidAmount": null,
  "currencyCode": null,
  "status": "<string>",
  "verifiedPaidAtUtc": null,
  "isApplied": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `upgradeId` | `Guid` | — |
| `receiptId` | `Guid` | — |
| `actualPayerCustomerUserId` | `Guid?` | — |
| `paymentId` | `string?` | — |
| `invoiceId` | `string?` | — |
| `verifiedPaidAmount` | `decimal?` | — |
| `currencyCode` | `string?` | — |
| `status` | `string` | — |
| `verifiedPaidAtUtc` | `DateTime?` | — |
| `isApplied` | `bool` | — |


### ConvertFromIndividualRequest

المصدر: `src/MealMate.Application/Features/FamilyCheckout/ConvertIndividualToFamilyCommands.cs`.

```json
{
  "name": null,
  "familyPackageId": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `name` | `string?` | `null` |
| `familyPackageId` | `Guid?` | `null` |


### FamilyCheckoutSelectionDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyCheckoutSelectionService.cs`.

```json
{
  "batchId": "<UUID من السيرفر>",
  "batchVersion": 0,
  "memberId": "<UUID من السيرفر>",
  "checkoutSelected": false,
  "selectedNewSubscriberCount": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `batchId` | `Guid` | — |
| `batchVersion` | `long` | — |
| `memberId` | `Guid` | — |
| `checkoutSelected` | `bool` | — |
| `selectedNewSubscriberCount` | `int` | — |


### FamilyCheckoutStartedDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyCheckoutService.cs`.

```json
{
  "familyGroupId": "<UUID من السيرفر>",
  "paymentAnchorSubscriptionId": "<UUID من السيرفر>",
  "paymentUrl": "<string>",
  "holdExpiresAtUtc": "<ISO-8601 UTC>",
  "finalPrice": 0,
  "batchId": null,
  "batchVersion": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `familyGroupId` | `Guid` | — |
| `paymentAnchorSubscriptionId` | `Guid` | — |
| `paymentUrl` | `string` | — |
| `holdExpiresAtUtc` | `DateTime` | — |
| `finalPrice` | `decimal` | — |
| `batchId` | `Guid?` | `null` |
| `batchVersion` | `long?` | `null` |


### FamilyCheckoutSummaryDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "familyGroupId": "<UUID من السيرفر>",
  "familyName": "<string>",
  "selectedMemberId": "<UUID من السيرفر>",
  "selectedMember": {
    "memberId": "<UUID من السيرفر>",
    "displayName": "<string>",
    "role": "<Manager | Member>",
    "isManager": false,
    "roleAr": "<string>",
    "roleEn": "<string>",
    "setupStatus": "<NotStarted | InProgress | Ready>",
    "isReady": false,
    "programId": null,
    "programNameAr": "<string>",
    "programNameEn": "<string>",
    "bundleId": null,
    "bundleNameAr": "<string>",
    "bundleNameEn": "<string>",
    "selectedTier": null,
    "tierNameAr": null,
    "tierNameEn": null,
    "durationDays": 0,
    "durationLabelAr": "<string>",
    "durationLabelEn": "<string>",
    "includeFriday": false,
    "breakfast": false,
    "lunch": false,
    "snack": false,
    "dinner": false,
    "mealsPerDay": 0,
    "breakfastCount": 0,
    "lunchCount": 0,
    "snackCount": 0,
    "dinnerCount": 0,
    "memberPrice": 0
  },
  "members": [
    {
      "memberId": "<UUID من السيرفر>",
      "role": "<Manager | Member>",
      "displayName": "<string>",
      "relationshipCode": "<string>",
      "gender": "<Male | Female>",
      "ageYears": 0,
      "deliveryAddressId": null,
      "setupStatus": "<NotStarted | InProgress | Ready>",
      "programId": null,
      "bundleId": null,
      "selectedTier": null,
      "durationDays": null,
      "includeFriday": false,
      "isCurrentUser": false,
      "phoneNumber": null,
      "occupation": null,
      "startDate": null,
      "deliveryAddress": null,
      "allergies": null,
      "allergiesCompleted": false,
      "dislikedFoods": null,
      "dietaryPreferences": null,
      "dietaryCompleted": false,
      "subscribeNow": false
    }
  ],
  "subtotal": 0,
  "familyDiscountPercent": 0,
  "familyDiscountAmount": 0,
  "finalPrice": 0,
  "currencyCode": "<string>",
  "mealOverview": [
    {
      "memberId": "<UUID من السيرفر>",
      "displayName": "<string>",
      "role": "<Manager | Member>",
      "isManager": false,
      "setupStatus": "<NotStarted | InProgress | Ready>",
      "isReady": false,
      "breakfast": false,
      "lunch": false,
      "snack": false,
      "dinner": false,
      "mealsPerDay": 0
    }
  ],
  "canCheckout": false,
  "checkoutBlockReason": null,
  "checkoutEndpoint": "<string>",
  "managerPrepaid": false,
  "managerSubscriptionId": null,
  "newMembersCount": 0,
  "newMembersSubtotal": 0,
  "selectedFamilyPackageId": null,
  "effectiveFamilyPackageId": null,
  "effectiveFamilyPackageMinMembers": null,
  "effectiveFamilyPackageMaxMembers": null,
  "participantCount": 0,
  "packageSelectionMode": "<string>",
  "rosterCount": 0,
  "selectedNewSubscriberCount": 0,
  "eligibleExistingSubscriberCount": 0,
  "unsubscribedCount": 0,
  "batchId": null,
  "batchVersion": null,
  "lockedQuote": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `familyGroupId` | `Guid` | — |
| `familyName` | `string` | — |
| `selectedMemberId` | `Guid` | — |
| `selectedMember` | `FamilyMemberSummaryDto` | — |
| `members` | `IReadOnlyList<FamilyMemberCardDto>` | — |
| `subtotal` | `decimal` | — |
| `familyDiscountPercent` | `decimal` | — |
| `familyDiscountAmount` | `decimal` | — |
| `finalPrice` | `decimal` | — |
| `currencyCode` | `string` | — |
| `mealOverview` | `IReadOnlyList<FamilyMealOverviewRowDto>` | — |
| `canCheckout` | `bool` | — |
| `checkoutBlockReason` | `string?` | — |
| `checkoutEndpoint` | `string` | — |
| `managerPrepaid` | `bool` | `false` |
| `managerSubscriptionId` | `Guid?` | `null` |
| `newMembersCount` | `int` | `0` |
| `newMembersSubtotal` | `decimal` | `0m` |
| `selectedFamilyPackageId` | `Guid?` | `null` |
| `effectiveFamilyPackageId` | `Guid?` | `null` |
| `effectiveFamilyPackageMinMembers` | `int?` | `null` |
| `effectiveFamilyPackageMaxMembers` | `int?` | `null` |
| `participantCount` | `int` | `0` |
| `packageSelectionMode` | `string` | `"selected"` |
| `rosterCount` | `int` | `0` |
| `selectedNewSubscriberCount` | `int` | `0` |
| `eligibleExistingSubscriberCount` | `int` | `0` |
| `unsubscribedCount` | `int` | `0` |
| `batchId` | `Guid?` | `null` |
| `batchVersion` | `long?` | `null` |
| `lockedQuote` | `bool` | `false` |


### FamilyDraftMetadataDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "relationships": [
    {
      "code": "<string>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "forDependents": false
    }
  ],
  "packages": [
    {
      "id": "<UUID من السيرفر>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "minMembers": 0,
      "maxMembers": 0,
      "discountPercent": 0,
      "sortOrder": 0,
      "imageStorageKey": null,
      "imageUrl": null
    }
  ],
  "maxMembers": 0,
  "discountPercent": 0,
  "minMembersForDiscount": 0,
  "supportedContractVersion": 0,
  "minimumInvoiceContractVersion": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `relationships` | `IReadOnlyList<FamilyLookupDto>` | — |
| `packages` | `IReadOnlyList<FamilySizePackageDto>` | — |
| `maxMembers` | `int` | — |
| `discountPercent` | `decimal` | — |
| `minMembersForDiscount` | `int` | — |
| `supportedContractVersion` | `int` | `FamilyInvoiceGate.SupportedContractVersion` |
| `minimumInvoiceContractVersion` | `int` | `FamilyInvoiceGate.SupportedContractVersion` |


### FamilyGroupScreenDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "familyGroupId": "<UUID من السيرفر>",
  "name": "<string>",
  "status": "<Draft | CheckoutPending | Active | Frozen | Dispute | PendingDetach | Cancelled>",
  "selectedFamilyPackageId": null,
  "packageNameAr": null,
  "packageNameEn": null,
  "minMembers": 0,
  "maxMembers": 0,
  "discountPercent": 0,
  "members": [
    {
      "memberId": "<UUID من السيرفر>",
      "role": "<Manager | Member>",
      "displayName": "<string>",
      "relationshipCode": "<string>",
      "gender": "<Male | Female>",
      "ageYears": 0,
      "deliveryAddressId": null,
      "setupStatus": "<NotStarted | InProgress | Ready>",
      "programId": null,
      "bundleId": null,
      "selectedTier": null,
      "durationDays": null,
      "includeFriday": false,
      "isCurrentUser": false,
      "phoneNumber": null,
      "occupation": null,
      "startDate": null,
      "deliveryAddress": null,
      "allergies": null,
      "allergiesCompleted": false,
      "dislikedFoods": null,
      "dietaryPreferences": null,
      "dietaryCompleted": false,
      "subscribeNow": false
    }
  ],
  "currentStep": 0,
  "sessionExpired": false,
  "sessionExpiresAtUtc": null,
  "sessionMessage": null,
  "packageAvailable": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `familyGroupId` | `Guid` | — |
| `name` | `string` | — |
| `status` | `FamilyGroupStatus` | — |
| `selectedFamilyPackageId` | `Guid?` | — |
| `packageNameAr` | `string?` | — |
| `packageNameEn` | `string?` | — |
| `minMembers` | `int` | — |
| `maxMembers` | `int` | — |
| `discountPercent` | `decimal` | — |
| `members` | `IReadOnlyList<FamilyMemberCardDto>` | — |
| `currentStep` | `int` | — |
| `sessionExpired` | `bool` | `false` |
| `sessionExpiresAtUtc` | `DateTime?` | `null` |
| `sessionMessage` | `string?` | `null` |
| `packageAvailable` | `bool` | `false` |


### FamilyLookupDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyCatalog.cs`.

```json
{
  "code": "<string>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "forDependents": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `code` | `string` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `forDependents` | `bool` | — |


### FamilyMealOverviewRowDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "memberId": "<UUID من السيرفر>",
  "displayName": "<string>",
  "role": "<Manager | Member>",
  "isManager": false,
  "setupStatus": "<NotStarted | InProgress | Ready>",
  "isReady": false,
  "breakfast": false,
  "lunch": false,
  "snack": false,
  "dinner": false,
  "mealsPerDay": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |
| `displayName` | `string` | — |
| `role` | `FamilyMemberRole` | — |
| `isManager` | `bool` | — |
| `setupStatus` | `FamilyMemberSetupStatus` | — |
| `isReady` | `bool` | — |
| `breakfast` | `bool` | — |
| `lunch` | `bool` | — |
| `snack` | `bool` | — |
| `dinner` | `bool` | — |
| `mealsPerDay` | `int` | — |


### FamilyMemberAllergiesDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "selected": [
    {
      "code": "<string>",
      "nameAr": null,
      "nameEn": null,
      "severity": null
    }
  ],
  "completed": false,
  "dislikedFoods": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `selected` | `IReadOnlyList<FoodPreferenceItemDto>` | — |
| `completed` | `bool` | — |
| `dislikedFoods` | `IReadOnlyList<FoodPreferenceItemDto>?` | `null` |


### FamilyMemberCardDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "memberId": "<UUID من السيرفر>",
  "role": "<Manager | Member>",
  "displayName": "<string>",
  "relationshipCode": "<string>",
  "gender": "<Male | Female>",
  "ageYears": 0,
  "deliveryAddressId": null,
  "setupStatus": "<NotStarted | InProgress | Ready>",
  "programId": null,
  "bundleId": null,
  "selectedTier": null,
  "durationDays": null,
  "includeFriday": false,
  "isCurrentUser": false,
  "phoneNumber": null,
  "occupation": null,
  "startDate": null,
  "deliveryAddress": null,
  "allergies": null,
  "allergiesCompleted": false,
  "dislikedFoods": null,
  "dietaryPreferences": null,
  "dietaryCompleted": false,
  "subscribeNow": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |
| `role` | `FamilyMemberRole` | — |
| `displayName` | `string` | — |
| `relationshipCode` | `string` | — |
| `gender` | `FamilyMemberGender` | — |
| `ageYears` | `int` | — |
| `deliveryAddressId` | `Guid?` | — |
| `setupStatus` | `FamilyMemberSetupStatus` | — |
| `programId` | `Guid?` | — |
| `bundleId` | `Guid?` | — |
| `selectedTier` | `RestaurantTier?` | — |
| `durationDays` | `int?` | — |
| `includeFriday` | `bool` | — |
| `isCurrentUser` | `bool` | — |
| `phoneNumber` | `string?` | `null` |
| `occupation` | `string?` | `null` |
| `startDate` | `DateOnly?` | `null` |
| `deliveryAddress` | `CustomerAddressDto?` | `null` |
| `allergies` | `IReadOnlyList<FoodPreferenceItemDto>?` | `null` |
| `allergiesCompleted` | `bool` | `false` |
| `dislikedFoods` | `IReadOnlyList<FoodPreferenceItemDto>?` | `null` |
| `dietaryPreferences` | `IReadOnlyList<DietaryPreferenceOptionDto>?` | `null` |
| `dietaryCompleted` | `bool` | `false` |
| `subscribeNow` | `bool` | `false` |


### FamilyMemberDietaryDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "selected": [
    {
      "code": "<string>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "isSelected": false,
      "isNone": false
    }
  ],
  "completed": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `selected` | `IReadOnlyList<DietaryPreferenceOptionDto>` | — |
| `completed` | `bool` | — |


### FamilyMemberDislikedFoodsDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "selected": [
    {
      "code": "<string>",
      "nameAr": null,
      "nameEn": null,
      "severity": null
    }
  ],
  "completed": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `selected` | `IReadOnlyList<FoodPreferenceItemDto>` | — |
| `completed` | `bool` | — |


### FamilyMemberLookupDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "found": false,
  "displayName": null,
  "ageYears": null,
  "gender": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `found` | `bool` | — |
| `displayName` | `string?` | — |
| `ageYears` | `int?` | — |
| `gender` | `FamilyMemberGender?` | — |


### FamilyMemberLookupRequest

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "phone": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `phone` | `string?` | — |


### FamilyMemberSummaryDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "memberId": "<UUID من السيرفر>",
  "displayName": "<string>",
  "role": "<Manager | Member>",
  "isManager": false,
  "roleAr": "<string>",
  "roleEn": "<string>",
  "setupStatus": "<NotStarted | InProgress | Ready>",
  "isReady": false,
  "programId": null,
  "programNameAr": "<string>",
  "programNameEn": "<string>",
  "bundleId": null,
  "bundleNameAr": "<string>",
  "bundleNameEn": "<string>",
  "selectedTier": null,
  "tierNameAr": null,
  "tierNameEn": null,
  "durationDays": 0,
  "durationLabelAr": "<string>",
  "durationLabelEn": "<string>",
  "includeFriday": false,
  "breakfast": false,
  "lunch": false,
  "snack": false,
  "dinner": false,
  "mealsPerDay": 0,
  "breakfastCount": 0,
  "lunchCount": 0,
  "snackCount": 0,
  "dinnerCount": 0,
  "memberPrice": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `memberId` | `Guid` | — |
| `displayName` | `string` | — |
| `role` | `FamilyMemberRole` | — |
| `isManager` | `bool` | — |
| `roleAr` | `string` | — |
| `roleEn` | `string` | — |
| `setupStatus` | `FamilyMemberSetupStatus` | — |
| `isReady` | `bool` | — |
| `programId` | `Guid?` | — |
| `programNameAr` | `string` | — |
| `programNameEn` | `string` | — |
| `bundleId` | `Guid?` | — |
| `bundleNameAr` | `string` | — |
| `bundleNameEn` | `string` | — |
| `selectedTier` | `RestaurantTier?` | — |
| `tierNameAr` | `string?` | — |
| `tierNameEn` | `string?` | — |
| `durationDays` | `int` | — |
| `durationLabelAr` | `string` | — |
| `durationLabelEn` | `string` | — |
| `includeFriday` | `bool` | — |
| `breakfast` | `bool` | — |
| `lunch` | `bool` | — |
| `snack` | `bool` | — |
| `dinner` | `bool` | — |
| `mealsPerDay` | `int` | — |
| `breakfastCount` | `int` | — |
| `lunchCount` | `int` | — |
| `snackCount` | `int` | — |
| `dinnerCount` | `int` | — |
| `memberPrice` | `decimal` | — |


### FamilyMemberWriteDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "displayName": "<string>",
  "relationshipCode": "<string>",
  "gender": "<Male | Female>",
  "ageYears": 0,
  "deliveryAddressId": null,
  "phone": null,
  "occupation": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `displayName` | `string` | — |
| `relationshipCode` | `string` | — |
| `gender` | `FamilyMemberGender` | — |
| `ageYears` | `int` | — |
| `deliveryAddressId` | `Guid?` | — |
| `phone` | `string?` | `null` |
| `occupation` | `string?` | `null` |


### FamilyRenewalCheckoutRequest

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyCheckoutService.cs`.

```json
{
  "startDate": "<YYYY-MM-DD>",
  "addressId": "<UUID من السيرفر>",
  "callbackUrl": "<string>",
  "errorUrl": "<string>",
  "correlationId": null,
  "paymentMethodId": 0,
  "idempotencyKey": null,
  "clientContractVersion": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `startDate` | `DateOnly` | — |
| `addressId` | `Guid` | — |
| `callbackUrl` | `string` | — |
| `errorUrl` | `string` | — |
| `correlationId` | `string?` | `null` |
| `paymentMethodId` | `int` | `3` |
| `idempotencyKey` | `string?` | `null` |
| `clientContractVersion` | `int?` | `null` |


### FamilySizePackageDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "id": "<UUID من السيرفر>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "minMembers": 0,
  "maxMembers": 0,
  "discountPercent": 0,
  "sortOrder": 0,
  "imageStorageKey": null,
  "imageUrl": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `id` | `Guid` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `minMembers` | `int` | — |
| `maxMembers` | `int` | — |
| `discountPercent` | `decimal` | — |
| `sortOrder` | `int` | — |
| `imageStorageKey` | `string?` | `null` |
| `imageUrl` | `string?` | `null` |


### FamilyStartCheckoutRequest

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyCheckoutService.cs`.

```json
{
  "callbackUrl": "<string>",
  "errorUrl": "<string>",
  "correlationId": null,
  "paymentMethodId": 0,
  "idempotencyKey": null,
  "batchId": null,
  "batchVersion": null,
  "clientContractVersion": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `callbackUrl` | `string` | — |
| `errorUrl` | `string` | — |
| `correlationId` | `string?` | `null` |
| `paymentMethodId` | `int` | `3` |
| `idempotencyKey` | `string?` | `null` |
| `batchId` | `Guid?` | `null` |
| `batchVersion` | `long?` | `null` |
| `clientContractVersion` | `int?` | `null` |


### SaveFamilyMemberAllergiesRequest

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "allergies": null,
  "selected": null,
  "selectedCodes": null,
  "codes": null,
  "allergens": null,
  "dislikedFoods": null,
  "ingredientCodes": null,
  "dietaryPreferenceCodes": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `allergies` | `IReadOnlyList<MealMate.Application.Features.IdentityAccess.Customers.FoodPreferenceItemDto>?` | `null` |
| `selected` | `IReadOnlyList<MealMate.Application.Features.IdentityAccess.Customers.FoodPreferenceItemDto>?` | `null` |
| `selectedCodes` | `IReadOnlyList<string>?` | `null` |
| `codes` | `IReadOnlyList<string>?` | `null` |
| `allergens` | `IReadOnlyList<FoodPreferenceAllergenOptionDto>?` | `null` |
| `dislikedFoods` | `IReadOnlyList<MealMate.Application.Features.IdentityAccess.Customers.FoodPreferenceItemDto>?` | `null` |
| `ingredientCodes` | `IReadOnlyList<string>?` | `null` |
| `dietaryPreferenceCodes` | `IReadOnlyList<string>?` | `null` |


### SaveFamilyMemberDietaryRequest

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "dietaryPreferenceCodes": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `dietaryPreferenceCodes` | `IReadOnlyList<string>?` | — |


### SaveFamilyMemberDislikedFoodsRequest

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "dislikedFoods": null,
  "dislikedIngredients": null,
  "selected": null,
  "items": null,
  "ingredientCodes": null,
  "selectedCodes": null,
  "ingredients": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `dislikedFoods` | `IReadOnlyList<FoodPreferenceItemDto>?` | `null` |
| `dislikedIngredients` | `IReadOnlyList<FoodPreferenceItemDto>?` | `null` |
| `selected` | `IReadOnlyList<FoodPreferenceItemDto>?` | `null` |
| `items` | `IReadOnlyList<FoodPreferenceItemDto>?` | `null` |
| `ingredientCodes` | `IReadOnlyList<string>?` | `null` |
| `selectedCodes` | `IReadOnlyList<string>?` | `null` |
| `ingredients` | `IReadOnlyList<string>?` | `null` |


### SetFamilyCheckoutSelectionRequest

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyCheckoutSelectionService.cs`.

```json
{
  "subscribeNow": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscribeNow` | `bool` | — |


### SubscriptionConversionOptionDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/ConvertIndividualToFamilyCommands.cs`.

```json
{
  "code": "<string>",
  "available": false,
  "titleAr": "<string>",
  "titleEn": "<string>",
  "action": null,
  "disabledReason": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `code` | `string` | — |
| `available` | `bool` | — |
| `titleAr` | `string` | — |
| `titleEn` | `string` | — |
| `action` | `string?` | — |
| `disabledReason` | `string?` | — |


### SubscriptionConversionOptionsDto

المصدر: `src/MealMate.Application/Features/FamilyCheckout/ConvertIndividualToFamilyCommands.cs`.

```json
{
  "subscriptionId": "<UUID من السيرفر>",
  "status": "<string>",
  "options": [
    {
      "code": "<string>",
      "available": false,
      "titleAr": "<string>",
      "titleEn": "<string>",
      "action": null,
      "disabledReason": null
    }
  ]
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscriptionId` | `Guid` | — |
| `status` | `string` | — |
| `options` | `IReadOnlyList<SubscriptionConversionOptionDto>` | — |


### UpsertFamilyGroupRequest

المصدر: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs`.

```json
{
  "name": "<string>",
  "familyPackageId": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `name` | `string` | — |
| `familyPackageId` | `Guid?` | `null` |

