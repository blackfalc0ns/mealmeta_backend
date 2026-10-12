# إدارة الاشتراك والتجديد والترقية والتجميد والإلغاء والفواتير

## ما تغير وطريقة الربط

ابدأ management ثم اقرأ options/details/quote قبل mutation. استعمل subscriptionId للفرد المستهدف وصلاحيات السيرفر. POST renewal-requests بلا body يرجع 202 CustomerRenewalRequestDto ثم POST family-subscription-flow/renewals/{renewalRequestId}/checkout. القادم يبقى مستقلًا حتى موعده، والحالي صالح حتى نهاية الخدمة الشاملة.

GET upgrade-options/upgrade-summary/upgrade-payment-methods ثم POST upgrade-requests، وبعده GET upgrade-requests/{upgradeId}. الدفع في ReviewRequired يظل دليلًا ماليًا دون منح الخدمة الجديدة. freeze-details ثم freeze-requests مستقلان عن account access. cancellation-quote ثم cancellation-requests: لا تحسب refund من gross price في Flutter؛ تعتمد نتيجة السيرفر payer/net/history، وقد تحتاج provider_refund_required أو مراجعة.

GET /api/v1/customer/invoices وGET invoices/{receiptId} وGET subscriptions/{id}/invoices تفصل نطاق الفرد والعيلة ودليل actual payer. رفض 404 يحمي الخصوصية؛ لا تعرض تفاصيل مورد آخر ولا تعاود تلقائيًا. لا يستنتج payer حق رؤية allocations للأشقاء.

GET/PUT subscriptions/{id}/delivery-address: اقرأ currentVersion ثم أرسل expectedVersion. affectedDayIds فقط تغيرت؛ skippedDayIds أيام محمية. الأصل المدفوع immutable. عند family.delivery_version_changed أعد القراءة.

GET auto-renewal وPUT/DELETE auto-renewal/consent لا يشغّلان تحصيلًا تلقائيًا: billingStatus=NotConnected وautoChargeEnabled=false. requiresPaymentSetup في GET يساوي consentActive (وليس true دائمًا). هذا تسجيل موافقة منفصل عن Payzah.

هذه **أشكال JSON توضيحية** مأخوذة من DTOs، وليست بيانات أعمال أو requests جاهزة للإرسال. استبدل الرموز بقيم السيرفر. الصفر وfalse يوضحان نوع القيمة فقط وليسا defaults أو قرارات صلاحية. الحقول nullable تظهر null هنا؛ راجع نوعها في الجدول لمعرفة القيمة عند وجودها. عنصر array يشرح نوع العنصر؛ الاستجابة الحقيقية قد ترجع []. IDs لا تُولّد بدل IDs السيرفر. Defaults موضحة منفصلة في الجداول. enums تُرسل بأسمائها الأصلية، والحقول camelCase. بعض حقول الاستجابة قد تُحذف عند null وفق خيارات serializer؛ العميل يتحمل null والغياب.

### AdminCancelSubscriptionRequest

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/SubscriptionCancellationContracts.cs`.

```json
{
  "reason": null,
  "forceImmediate": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `reason` | `string?` | — |
| `forceImmediate` | `bool` | `false` |


### CustomerActiveFreezeDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Freeze/SubscriptionFreezeContracts.cs`.

```json
{
  "requestId": "<UUID من السيرفر>",
  "startDate": "<YYYY-MM-DD>",
  "endDate": "<YYYY-MM-DD>",
  "freezeDays": 0,
  "frozenServiceDaysCount": 0,
  "status": "<string>",
  "statusNameAr": "<string>",
  "isCurrentlyActive": false,
  "isUpcoming": false,
  "reason": null,
  "createdAtUtc": "<ISO-8601 UTC>",
  "processedAtUtc": null,
  "frozenServiceDates": [
    "<YYYY-MM-DD>"
  ],
  "compensationDates": [
    "<YYYY-MM-DD>"
  ],
  "subscriptionEndDate": "<YYYY-MM-DD>",
  "summaryAr": "<string>",
  "summaryEn": "<string>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `requestId` | `Guid` | — |
| `startDate` | `DateOnly` | — |
| `endDate` | `DateOnly` | — |
| `freezeDays` | `int` | — |
| `frozenServiceDaysCount` | `int` | — |
| `status` | `string` | — |
| `statusNameAr` | `string` | — |
| `isCurrentlyActive` | `bool` | — |
| `isUpcoming` | `bool` | — |
| `reason` | `string?` | — |
| `createdAtUtc` | `DateTime` | — |
| `processedAtUtc` | `DateTime?` | — |
| `frozenServiceDates` | `IReadOnlyList<DateOnly>` | — |
| `compensationDates` | `IReadOnlyList<DateOnly>` | — |
| `subscriptionEndDate` | `DateOnly` | — |
| `summaryAr` | `string` | — |
| `summaryEn` | `string` | — |


### CustomerCancellationQuoteDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/SubscriptionCancellationContracts.cs`.

```json
{
  "subscriptionId": "<UUID من السيرفر>",
  "totalServiceDays": 0,
  "deliveredDays": 0,
  "lockedKitchenDays": 0,
  "refundableDays": 0,
  "dailyBoxPrice": 0,
  "refundAmount": 0,
  "currencyCode": "<string>",
  "effectiveCancellationDate": "<YYYY-MM-DD>",
  "policyMessageAr": "<string>",
  "cancellationFeeAmount": 0,
  "netRefundAmount": 0,
  "refundToCashWallet": false,
  "policyMessageEn": null,
  "messageCode": null,
  "payerCustomerUserId": null,
  "batchId": null,
  "beneficiaries": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscriptionId` | `Guid` | — |
| `totalServiceDays` | `int` | — |
| `deliveredDays` | `int` | — |
| `lockedKitchenDays` | `int` | — |
| `refundableDays` | `int` | — |
| `dailyBoxPrice` | `decimal` | — |
| `refundAmount` | `decimal` | — |
| `currencyCode` | `string` | — |
| `effectiveCancellationDate` | `DateOnly` | — |
| `policyMessageAr` | `string` | — |
| `cancellationFeeAmount` | `decimal` | `0m` |
| `netRefundAmount` | `decimal` | `0m` |
| `refundToCashWallet` | `bool` | `true` |
| `policyMessageEn` | `string?` | `null` |
| `messageCode` | `string?` | `null` |
| `payerCustomerUserId` | `Guid?` | `null` |
| `batchId` | `Guid?` | `null` |
| `beneficiaries` | `IReadOnlyList<FamilyRefundBeneficiaryDto>?` | `null` |


### CustomerCancellationRequest

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/SubscriptionCancellationContracts.cs`.

```json
{
  "reason": null,
  "requestedEffectiveDate": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `reason` | `string?` | — |
| `requestedEffectiveDate` | `DateOnly?` | `null` |


### CustomerCancellationRequestDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/SubscriptionCancellationContracts.cs`.

```json
{
  "requestId": "<UUID من السيرفر>",
  "subscriptionId": "<UUID من السيرفر>",
  "status": "<string>",
  "reason": null,
  "requestedAtUtc": "<ISO-8601 UTC>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `requestId` | `Guid` | — |
| `subscriptionId` | `Guid` | — |
| `status` | `string` | — |
| `reason` | `string?` | — |
| `requestedAtUtc` | `DateTime` | — |


### CustomerCancellationResultDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/SubscriptionCancellationContracts.cs`.

```json
{
  "requestId": "<UUID من السيرفر>",
  "subscriptionId": "<UUID من السيرفر>",
  "status": "<string>",
  "statusNameAr": "<string>",
  "totalServiceDays": 0,
  "deliveredDays": 0,
  "lockedKitchenDays": 0,
  "refundableDays": 0,
  "dailyBoxPrice": 0,
  "refundAmount": 0,
  "currencyCode": "<string>",
  "effectiveCancellationDate": null,
  "messageAr": "<string>",
  "timestampUtc": "<ISO-8601 UTC>",
  "cancellationFeeAmount": 0,
  "netRefundAmount": 0,
  "refundedToWallet": false,
  "messageEn": null,
  "messageCode": null,
  "payerCustomerUserId": null,
  "settlementId": null,
  "batchId": null,
  "beneficiaries": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `requestId` | `Guid` | — |
| `subscriptionId` | `Guid` | — |
| `status` | `string` | — |
| `statusNameAr` | `string` | — |
| `totalServiceDays` | `int` | — |
| `deliveredDays` | `int` | — |
| `lockedKitchenDays` | `int` | — |
| `refundableDays` | `int` | — |
| `dailyBoxPrice` | `decimal` | — |
| `refundAmount` | `decimal` | — |
| `currencyCode` | `string` | — |
| `effectiveCancellationDate` | `DateOnly?` | — |
| `messageAr` | `string` | — |
| `timestampUtc` | `DateTime` | — |
| `cancellationFeeAmount` | `decimal` | `0m` |
| `netRefundAmount` | `decimal` | `0m` |
| `refundedToWallet` | `bool` | `false` |
| `messageEn` | `string?` | `null` |
| `messageCode` | `string?` | `null` |
| `payerCustomerUserId` | `Guid?` | `null` |
| `settlementId` | `Guid?` | `null` |
| `batchId` | `Guid?` | `null` |
| `beneficiaries` | `IReadOnlyList<FamilyRefundBeneficiaryDto>?` | `null` |


### CustomerCurrentSubscriptionScreenDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/CustomerSubscriptionContracts.cs`.

```json
{
  "subscription": null,
  "emptyStateCode": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscription` | `CustomerSubscriptionSlimDto?` | — |
| `emptyStateCode` | `string?` | — |


### CustomerDefaultPaymentMethodDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/CustomerSubscriptionContracts.cs`.

```json
{
  "hasSavedMethod": false,
  "brand": null,
  "last4": null,
  "expiry": null,
  "gateway": null,
  "emptyStateCode": "<string>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `hasSavedMethod` | `bool` | — |
| `brand` | `string?` | — |
| `last4` | `string?` | — |
| `expiry` | `string?` | — |
| `gateway` | `string?` | — |
| `emptyStateCode` | `string` | — |


### CustomerFreezeHistoryItemDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Freeze/SubscriptionFreezeContracts.cs`.

```json
{
  "requestId": "<UUID من السيرفر>",
  "startDate": "<YYYY-MM-DD>",
  "endDate": "<YYYY-MM-DD>",
  "freezeDays": 0,
  "status": "<string>",
  "statusNameAr": "<string>",
  "reason": null,
  "createdAtUtc": "<ISO-8601 UTC>",
  "processedAtUtc": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `requestId` | `Guid` | — |
| `startDate` | `DateOnly` | — |
| `endDate` | `DateOnly` | — |
| `freezeDays` | `int` | — |
| `status` | `string` | — |
| `statusNameAr` | `string` | — |
| `reason` | `string?` | — |
| `createdAtUtc` | `DateTime` | — |
| `processedAtUtc` | `DateTime?` | — |


### CustomerFreezeRequest

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Freeze/SubscriptionFreezeContracts.cs`.

```json
{
  "startDate": "<YYYY-MM-DD>",
  "endDate": "<YYYY-MM-DD>",
  "reason": null,
  "acknowledgeLostSelections": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `startDate` | `DateOnly` | — |
| `endDate` | `DateOnly` | — |
| `reason` | `string?` | — |
| `acknowledgeLostSelections` | `bool` | `false` |


### CustomerFrozenRestaurantDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/CustomerSubscriptionContracts.cs`.

```json
{
  "restaurantId": "<UUID من السيرفر>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "logoUrl": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `restaurantId` | `Guid` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `logoUrl` | `string?` | — |


### CustomerManagedSubscriptionDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/CustomerSubscriptionContracts.cs`.

```json
{
  "subscriptionId": "<UUID من السيرفر>",
  "status": "<string>",
  "statusNameAr": "<string>",
  "programId": "<UUID من السيرفر>",
  "programNameAr": "<string>",
  "programNameEn": "<string>",
  "bundleId": "<UUID من السيرفر>",
  "bundleNameAr": "<string>",
  "bundleNameEn": "<string>",
  "tier": "<Basic | Platinum | Elite>",
  "tierNameAr": "<string>",
  "tierNameEn": "<string>",
  "tierLogoUrl": null,
  "mealsPerDay": 0,
  "imageUrl": null,
  "startDate": "<YYYY-MM-DD>",
  "endDate": "<YYYY-MM-DD>",
  "suggestedRenewalStartDate": "<YYYY-MM-DD>",
  "totalServiceDays": 0,
  "completedServiceDays": 0,
  "remainingServiceDays": 0,
  "progressPercentage": 0,
  "paidAmount": null,
  "currencyCode": "<string>",
  "restaurant": null,
  "pendingFreeze": null,
  "pendingUpgrade": null,
  "actions": {
    "canRenew": false,
    "canFreeze": false,
    "canCancel": false,
    "canUpgrade": false,
    "renewUnavailableReason": null,
    "freezeUnavailableReason": null,
    "cancelUnavailableReason": null,
    "upgradeUnavailableReason": null,
    "freezeStartDelayDays": 0,
    "maximumFreezeDays": 0,
    "freezeExtendsSubscriptionEnd": false,
    "earliestFreezeStartDate": "<YYYY-MM-DD>",
    "renewalEndpoint": "<string>",
    "freezeEndpoint": "<string>",
    "cancellationEndpoint": "<string>",
    "upgradeEndpoint": "<string>",
    "cancellationQuoteEndpoint": null,
    "freezeNoticeAr": null,
    "freezeNoticeEn": null,
    "freezeNotice": null,
    "freezeDetailsEndpoint": null
  },
  "invoices": [
    {
      "receiptId": "<UUID من السيرفر>",
      "invoiceNumber": null,
      "paymentId": null,
      "issuedAtUtc": "<ISO-8601 UTC>",
      "amount": 0,
      "currencyCode": "<string>",
      "status": "<string>",
      "gateway": "<string>",
      "maskedCardNumber": null,
      "canDownload": false,
      "downloadUnavailableReason": null,
      "batchId": null,
      "familyMemberId": null,
      "payerCustomerUserId": null,
      "isMemberAllocation": false,
      "upgradeId": null,
      "isPaidReviewRequired": false
    }
  ],
  "activeFreeze": null,
  "kind": "<string>",
  "familyGroupId": null,
  "memberCount": 0,
  "familyName": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscriptionId` | `Guid` | — |
| `status` | `string` | — |
| `statusNameAr` | `string` | — |
| `programId` | `Guid` | — |
| `programNameAr` | `string` | — |
| `programNameEn` | `string` | — |
| `bundleId` | `Guid` | — |
| `bundleNameAr` | `string` | — |
| `bundleNameEn` | `string` | — |
| `tier` | `RestaurantTier` | — |
| `tierNameAr` | `string` | — |
| `tierNameEn` | `string` | — |
| `tierLogoUrl` | `string?` | — |
| `mealsPerDay` | `int` | — |
| `imageUrl` | `string?` | — |
| `startDate` | `DateOnly` | — |
| `endDate` | `DateOnly` | — |
| `suggestedRenewalStartDate` | `DateOnly` | — |
| `totalServiceDays` | `int` | — |
| `completedServiceDays` | `int` | — |
| `remainingServiceDays` | `int` | — |
| `progressPercentage` | `decimal` | — |
| `paidAmount` | `decimal?` | — |
| `currencyCode` | `string` | — |
| `restaurant` | `CustomerFrozenRestaurantDto?` | — |
| `pendingFreeze` | `CustomerPendingFreezeDto?` | — |
| `pendingUpgrade` | `CustomerPendingUpgradeDto?` | — |
| `actions` | `CustomerSubscriptionActionsDto` | — |
| `invoices` | `IReadOnlyList<CustomerSubscriptionInvoiceDto>` | — |
| `activeFreeze` | `CustomerActiveFreezeDto?` | `null` |
| `kind` | `string` | `"INDIVIDUAL"` |
| `familyGroupId` | `Guid?` | `null` |
| `memberCount` | `int` | `1` |
| `familyName` | `string?` | `null` |


### CustomerPaymentMethodDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs`.

```json
{
  "paymentMethodId": null,
  "code": "<string>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "integrationType": "<string>",
  "serviceCharge": null,
  "totalAmount": null,
  "currencyCode": "<string>",
  "isAvailable": false,
  "unavailableReason": null,
  "imageUrl": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `paymentMethodId` | `int?` | — |
| `code` | `string` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `integrationType` | `string` | — |
| `serviceCharge` | `decimal?` | — |
| `totalAmount` | `decimal?` | — |
| `currencyCode` | `string` | — |
| `isAvailable` | `bool` | — |
| `unavailableReason` | `string?` | — |
| `imageUrl` | `string?` | `null` |


### CustomerPendingFreezeDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Freeze/SubscriptionFreezeContracts.cs`.

```json
{
  "requestId": "<UUID من السيرفر>",
  "startDate": "<YYYY-MM-DD>",
  "endDate": "<YYYY-MM-DD>",
  "freezeDays": 0,
  "status": "<Draft | Submitted | UnderReview | Approved | Rejected | Expired | ResubmissionRequired | NeedsChanges>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `requestId` | `Guid` | — |
| `startDate` | `DateOnly` | — |
| `endDate` | `DateOnly` | — |
| `freezeDays` | `int` | — |
| `status` | `VerificationStatus` | — |


### CustomerPendingUpgradeDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs`.

```json
{
  "requestId": "<UUID من السيرفر>",
  "fromTier": "<Basic | Platinum | Elite>",
  "toTier": "<Basic | Platinum | Elite>",
  "priceDifference": 0,
  "status": "<Draft | Submitted | UnderReview | Approved | Rejected | Expired | ResubmissionRequired | NeedsChanges>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `requestId` | `Guid` | — |
| `fromTier` | `RestaurantTier` | — |
| `toTier` | `RestaurantTier` | — |
| `priceDifference` | `decimal` | — |
| `status` | `VerificationStatus` | — |


### CustomerRenewalRequestDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Renewal/SubscriptionRenewalContracts.cs`.

```json
{
  "requestId": "<UUID من السيرفر>",
  "previousSubscriptionId": "<UUID من السيرفر>",
  "status": "<string>",
  "programId": "<UUID من السيرفر>",
  "bundleId": "<UUID من السيرفر>",
  "tier": "<Basic | Platinum | Elite>",
  "durationDays": 0,
  "includeFriday": false,
  "regionId": null,
  "suggestedStartDate": "<YYYY-MM-DD>",
  "configurationEndpoint": null,
  "checkoutEndpoint": null,
  "familyGroupId": null,
  "familyMemberId": null,
  "familyRenewalCheckoutEndpoint": null,
  "renewalStartDate": null,
  "batchId": null,
  "batchVersion": null,
  "flowKind": "<string>",
  "newSubscriptionId": null,
  "checkoutStatus": null,
  "paymentUrl": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `requestId` | `Guid` | — |
| `previousSubscriptionId` | `Guid` | — |
| `status` | `string` | — |
| `programId` | `Guid` | — |
| `bundleId` | `Guid` | — |
| `tier` | `RestaurantTier` | — |
| `durationDays` | `int` | — |
| `includeFriday` | `bool` | — |
| `regionId` | `Guid?` | — |
| `suggestedStartDate` | `DateOnly` | — |
| `configurationEndpoint` | `string?` | — |
| `checkoutEndpoint` | `string?` | — |
| `familyGroupId` | `Guid?` | `null` |
| `familyMemberId` | `Guid?` | `null` |
| `familyRenewalCheckoutEndpoint` | `string?` | `null` |
| `renewalStartDate` | `DateOnly?` | `null` |
| `batchId` | `Guid?` | `null` |
| `batchVersion` | `long?` | `null` |
| `flowKind` | `string` | `"Individual"` |
| `newSubscriptionId` | `Guid?` | `null` |
| `checkoutStatus` | `string?` | `null` |
| `paymentUrl` | `string?` | `null` |


### CustomerSubscriptionActionsDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/CustomerSubscriptionContracts.cs`.

```json
{
  "canRenew": false,
  "canFreeze": false,
  "canCancel": false,
  "canUpgrade": false,
  "renewUnavailableReason": null,
  "freezeUnavailableReason": null,
  "cancelUnavailableReason": null,
  "upgradeUnavailableReason": null,
  "freezeStartDelayDays": 0,
  "maximumFreezeDays": 0,
  "freezeExtendsSubscriptionEnd": false,
  "earliestFreezeStartDate": "<YYYY-MM-DD>",
  "renewalEndpoint": "<string>",
  "freezeEndpoint": "<string>",
  "cancellationEndpoint": "<string>",
  "upgradeEndpoint": "<string>",
  "cancellationQuoteEndpoint": null,
  "freezeNoticeAr": null,
  "freezeNoticeEn": null,
  "freezeNotice": null,
  "freezeDetailsEndpoint": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `canRenew` | `bool` | — |
| `canFreeze` | `bool` | — |
| `canCancel` | `bool` | — |
| `canUpgrade` | `bool` | — |
| `renewUnavailableReason` | `string?` | — |
| `freezeUnavailableReason` | `string?` | — |
| `cancelUnavailableReason` | `string?` | — |
| `upgradeUnavailableReason` | `string?` | — |
| `freezeStartDelayDays` | `int` | — |
| `maximumFreezeDays` | `int` | — |
| `freezeExtendsSubscriptionEnd` | `bool` | — |
| `earliestFreezeStartDate` | `DateOnly` | — |
| `renewalEndpoint` | `string` | — |
| `freezeEndpoint` | `string` | — |
| `cancellationEndpoint` | `string` | — |
| `upgradeEndpoint` | `string` | — |
| `cancellationQuoteEndpoint` | `string?` | `null` |
| `freezeNoticeAr` | `string?` | `null` |
| `freezeNoticeEn` | `string?` | `null` |
| `freezeNotice` | `string?` | `null` |
| `freezeDetailsEndpoint` | `string?` | `null` |


### CustomerSubscriptionFreezeDetailsDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Freeze/SubscriptionFreezeContracts.cs`.

```json
{
  "subscriptionId": "<UUID من السيرفر>",
  "subscriptionStatus": "<string>",
  "subscriptionStatusNameAr": "<string>",
  "hasFreeze": false,
  "isCurrentlyFrozen": false,
  "currentFreeze": null,
  "freezeHistory": [
    {
      "requestId": "<UUID من السيرفر>",
      "startDate": "<YYYY-MM-DD>",
      "endDate": "<YYYY-MM-DD>",
      "freezeDays": 0,
      "status": "<string>",
      "statusNameAr": "<string>",
      "reason": null,
      "createdAtUtc": "<ISO-8601 UTC>",
      "processedAtUtc": null
    }
  ],
  "noticeAr": "<string>",
  "noticeEn": "<string>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscriptionId` | `Guid` | — |
| `subscriptionStatus` | `string` | — |
| `subscriptionStatusNameAr` | `string` | — |
| `hasFreeze` | `bool` | — |
| `isCurrentlyFrozen` | `bool` | — |
| `currentFreeze` | `CustomerActiveFreezeDto?` | — |
| `freezeHistory` | `IReadOnlyList<CustomerFreezeHistoryItemDto>` | — |
| `noticeAr` | `string` | — |
| `noticeEn` | `string` | — |


### CustomerSubscriptionFreezePolicyDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Freeze/SubscriptionFreezeContracts.cs`.

```json
{
  "defaultDurationDays": 0,
  "startDelayDays": 0,
  "extendSubscriptionEnd": false,
  "earliestAllowedStartDate": "<YYYY-MM-DD>",
  "messageAr": "<string>",
  "messageEn": "<string>",
  "noticeAr": null,
  "noticeEn": null,
  "enabled": false,
  "minDays": 0,
  "maxOccurrences": 0,
  "minGapDays": 0,
  "maxTotalDays": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `defaultDurationDays` | `int` | — |
| `startDelayDays` | `int` | — |
| `extendSubscriptionEnd` | `bool` | — |
| `earliestAllowedStartDate` | `DateOnly` | — |
| `messageAr` | `string` | — |
| `messageEn` | `string` | — |
| `noticeAr` | `string?` | `null` |
| `noticeEn` | `string?` | `null` |
| `enabled` | `bool` | `true` |
| `minDays` | `int` | `1` |
| `maxOccurrences` | `int` | `1000` |
| `minGapDays` | `int` | `0` |
| `maxTotalDays` | `int?` | `null` |


### CustomerSubscriptionInvoiceDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/CustomerSubscriptionContracts.cs`.

```json
{
  "receiptId": "<UUID من السيرفر>",
  "invoiceNumber": null,
  "paymentId": null,
  "issuedAtUtc": "<ISO-8601 UTC>",
  "amount": 0,
  "currencyCode": "<string>",
  "status": "<string>",
  "gateway": "<string>",
  "maskedCardNumber": null,
  "canDownload": false,
  "downloadUnavailableReason": null,
  "batchId": null,
  "familyMemberId": null,
  "payerCustomerUserId": null,
  "isMemberAllocation": false,
  "upgradeId": null,
  "isPaidReviewRequired": false
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `receiptId` | `Guid` | — |
| `invoiceNumber` | `string?` | — |
| `paymentId` | `string?` | — |
| `issuedAtUtc` | `DateTime` | — |
| `amount` | `decimal` | — |
| `currencyCode` | `string` | — |
| `status` | `string` | — |
| `gateway` | `string` | — |
| `maskedCardNumber` | `string?` | — |
| `canDownload` | `bool` | — |
| `downloadUnavailableReason` | `string?` | — |
| `batchId` | `Guid?` | `null` |
| `familyMemberId` | `Guid?` | `null` |
| `payerCustomerUserId` | `Guid?` | `null` |
| `isMemberAllocation` | `bool` | `false` |
| `upgradeId` | `Guid?` | `null` |
| `isPaidReviewRequired` | `bool` | `false` |


### CustomerSubscriptionInvoiceListDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/CustomerSubscriptionContracts.cs`.

```json
{
  "receiptId": "<UUID من السيرفر>",
  "invoiceNumber": null,
  "paymentId": null,
  "issuedAtUtc": "<ISO-8601 UTC>",
  "amount": 0,
  "currencyCode": "<string>",
  "status": "<string>",
  "statusNameAr": "<string>",
  "gateway": "<string>",
  "maskedCardNumber": null,
  "canDownload": false,
  "downloadUnavailableReason": null,
  "batchId": null,
  "familyMemberId": null,
  "payerCustomerUserId": null,
  "isMemberAllocation": false,
  "upgradeId": null,
  "isPaidReviewRequired": false,
  "statusNameEn": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `receiptId` | `Guid` | — |
| `invoiceNumber` | `string?` | — |
| `paymentId` | `string?` | — |
| `issuedAtUtc` | `DateTime` | — |
| `amount` | `decimal` | — |
| `currencyCode` | `string` | — |
| `status` | `string` | — |
| `statusNameAr` | `string` | — |
| `gateway` | `string` | — |
| `maskedCardNumber` | `string?` | — |
| `canDownload` | `bool` | — |
| `downloadUnavailableReason` | `string?` | — |
| `batchId` | `Guid?` | `null` |
| `familyMemberId` | `Guid?` | `null` |
| `payerCustomerUserId` | `Guid?` | `null` |
| `isMemberAllocation` | `bool` | `false` |
| `upgradeId` | `Guid?` | `null` |
| `isPaidReviewRequired` | `bool` | `false` |
| `statusNameEn` | `string?` | `null` |


### CustomerSubscriptionManagementScreenDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/CustomerSubscriptionContracts.cs`.

```json
{
  "currentSubscription": null,
  "subscriptions": [
    {
      "subscriptionId": "<UUID من السيرفر>",
      "status": "<string>",
      "statusNameAr": "<string>",
      "programId": "<UUID من السيرفر>",
      "programNameAr": "<string>",
      "programNameEn": "<string>",
      "bundleId": "<UUID من السيرفر>",
      "bundleNameAr": "<string>",
      "bundleNameEn": "<string>",
      "tier": "<Basic | Platinum | Elite>",
      "tierNameAr": "<string>",
      "tierNameEn": "<string>",
      "tierLogoUrl": null,
      "mealsPerDay": 0,
      "imageUrl": null,
      "startDate": "<YYYY-MM-DD>",
      "endDate": "<YYYY-MM-DD>",
      "suggestedRenewalStartDate": "<YYYY-MM-DD>",
      "totalServiceDays": 0,
      "completedServiceDays": 0,
      "remainingServiceDays": 0,
      "progressPercentage": 0,
      "paidAmount": null,
      "currencyCode": "<string>",
      "restaurant": null,
      "pendingFreeze": null,
      "pendingUpgrade": null,
      "actions": {
        "canRenew": false,
        "canFreeze": false,
        "canCancel": false,
        "canUpgrade": false,
        "renewUnavailableReason": null,
        "freezeUnavailableReason": null,
        "cancelUnavailableReason": null,
        "upgradeUnavailableReason": null,
        "freezeStartDelayDays": 0,
        "maximumFreezeDays": 0,
        "freezeExtendsSubscriptionEnd": false,
        "earliestFreezeStartDate": "<YYYY-MM-DD>",
        "renewalEndpoint": "<string>",
        "freezeEndpoint": "<string>",
        "cancellationEndpoint": "<string>",
        "upgradeEndpoint": "<string>",
        "cancellationQuoteEndpoint": null,
        "freezeNoticeAr": null,
        "freezeNoticeEn": null,
        "freezeNotice": null,
        "freezeDetailsEndpoint": null
      },
      "invoices": [
        {
          "receiptId": "<UUID من السيرفر>",
          "invoiceNumber": null,
          "paymentId": null,
          "issuedAtUtc": "<ISO-8601 UTC>",
          "amount": 0,
          "currencyCode": "<string>",
          "status": "<string>",
          "gateway": "<string>",
          "maskedCardNumber": null,
          "canDownload": false,
          "downloadUnavailableReason": null,
          "batchId": null,
          "familyMemberId": null,
          "payerCustomerUserId": null,
          "isMemberAllocation": false,
          "upgradeId": null,
          "isPaidReviewRequired": false
        }
      ],
      "activeFreeze": null,
      "kind": "<string>",
      "familyGroupId": null,
      "memberCount": 0,
      "familyName": null
    }
  ],
  "invoices": [
    {
      "receiptId": "<UUID من السيرفر>",
      "invoiceNumber": null,
      "paymentId": null,
      "issuedAtUtc": "<ISO-8601 UTC>",
      "amount": 0,
      "currencyCode": "<string>",
      "status": "<string>",
      "gateway": "<string>",
      "maskedCardNumber": null,
      "canDownload": false,
      "downloadUnavailableReason": null,
      "batchId": null,
      "familyMemberId": null,
      "payerCustomerUserId": null,
      "isMemberAllocation": false,
      "upgradeId": null,
      "isPaidReviewRequired": false
    }
  ],
  "emptyStateCode": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `currentSubscription` | `CustomerManagedSubscriptionDto?` | — |
| `subscriptions` | `IReadOnlyList<CustomerManagedSubscriptionDto>` | — |
| `invoices` | `IReadOnlyList<CustomerSubscriptionInvoiceDto>` | — |
| `emptyStateCode` | `string?` | — |


### CustomerSubscriptionSlimDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/CustomerSubscriptionContracts.cs`.

```json
{
  "subscriptionId": "<UUID من السيرفر>",
  "status": "<string>",
  "statusNameAr": "<string>",
  "programId": "<UUID من السيرفر>",
  "programNameAr": "<string>",
  "programNameEn": "<string>",
  "bundleId": "<UUID من السيرفر>",
  "bundleNameAr": "<string>",
  "bundleNameEn": "<string>",
  "tier": "<Basic | Platinum | Elite>",
  "tierNameAr": "<string>",
  "tierNameEn": "<string>",
  "tierLogoUrl": null,
  "mealsPerDay": 0,
  "imageUrl": null,
  "startDate": "<YYYY-MM-DD>",
  "endDate": "<YYYY-MM-DD>",
  "nextRenewalDate": "<YYYY-MM-DD>",
  "totalServiceDays": 0,
  "completedServiceDays": 0,
  "remainingServiceDays": 0,
  "progressPercentage": 0,
  "paidAmount": null,
  "currencyCode": "<string>",
  "restaurant": null,
  "pendingFreeze": null,
  "pendingUpgrade": null,
  "actions": {
    "canRenew": false,
    "canFreeze": false,
    "canCancel": false,
    "canUpgrade": false,
    "renewUnavailableReason": null,
    "freezeUnavailableReason": null,
    "cancelUnavailableReason": null,
    "upgradeUnavailableReason": null,
    "freezeStartDelayDays": 0,
    "maximumFreezeDays": 0,
    "freezeExtendsSubscriptionEnd": false,
    "earliestFreezeStartDate": "<YYYY-MM-DD>",
    "renewalEndpoint": "<string>",
    "freezeEndpoint": "<string>",
    "cancellationEndpoint": "<string>",
    "upgradeEndpoint": "<string>",
    "cancellationQuoteEndpoint": null,
    "freezeNoticeAr": null,
    "freezeNoticeEn": null,
    "freezeNotice": null,
    "freezeDetailsEndpoint": null
  },
  "activeFreeze": null,
  "kind": "<string>",
  "familyGroupId": null,
  "memberCount": 0,
  "familyName": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscriptionId` | `Guid` | — |
| `status` | `string` | — |
| `statusNameAr` | `string` | — |
| `programId` | `Guid` | — |
| `programNameAr` | `string` | — |
| `programNameEn` | `string` | — |
| `bundleId` | `Guid` | — |
| `bundleNameAr` | `string` | — |
| `bundleNameEn` | `string` | — |
| `tier` | `RestaurantTier` | — |
| `tierNameAr` | `string` | — |
| `tierNameEn` | `string` | — |
| `tierLogoUrl` | `string?` | — |
| `mealsPerDay` | `int` | — |
| `imageUrl` | `string?` | — |
| `startDate` | `DateOnly` | — |
| `endDate` | `DateOnly` | — |
| `nextRenewalDate` | `DateOnly` | — |
| `totalServiceDays` | `int` | — |
| `completedServiceDays` | `int` | — |
| `remainingServiceDays` | `int` | — |
| `progressPercentage` | `decimal` | — |
| `paidAmount` | `decimal?` | — |
| `currencyCode` | `string` | — |
| `restaurant` | `CustomerFrozenRestaurantDto?` | — |
| `pendingFreeze` | `CustomerPendingFreezeDto?` | — |
| `pendingUpgrade` | `CustomerPendingUpgradeDto?` | — |
| `actions` | `CustomerSubscriptionActionsDto` | — |
| `activeFreeze` | `CustomerActiveFreezeDto?` | `null` |
| `kind` | `string` | `"INDIVIDUAL"` |
| `familyGroupId` | `Guid?` | `null` |
| `memberCount` | `int` | `1` |
| `familyName` | `string?` | `null` |


### CustomerUncompletedActionsDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/CustomerSubscriptionContracts.cs`.

```json
{
  "canDelete": false,
  "canResume": false,
  "deleteEndpoint": "<string>",
  "resumeEndpoint": "<string>",
  "nextScreen": "<string>",
  "payEndpoint": "<string>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `canDelete` | `bool` | — |
| `canResume` | `bool` | — |
| `deleteEndpoint` | `string` | — |
| `resumeEndpoint` | `string` | — |
| `nextScreen` | `string` | — |
| `payEndpoint` | `string` | — |


### CustomerUncompletedSubscriptionDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/CustomerSubscriptionContracts.cs`.

```json
{
  "subscriptionId": "<UUID من السيرفر>",
  "status": "<string>",
  "statusNameAr": "<string>",
  "programId": "<UUID من السيرفر>",
  "programNameAr": "<string>",
  "programNameEn": "<string>",
  "bundleId": "<UUID من السيرفر>",
  "bundleNameAr": "<string>",
  "bundleNameEn": "<string>",
  "tier": "<Basic | Platinum | Elite>",
  "tierNameAr": "<string>",
  "tierNameEn": "<string>",
  "tierLogoUrl": null,
  "mealsPerDay": 0,
  "imageUrl": null,
  "startDate": "<YYYY-MM-DD>",
  "endDate": "<YYYY-MM-DD>",
  "durationDays": 0,
  "includeFriday": false,
  "amount": 0,
  "currencyCode": "<string>",
  "deliveryAddress": null,
  "createdAtUtc": "<ISO-8601 UTC>",
  "isHoldActive": false,
  "holdRemainingSeconds": null,
  "holdExpiresAtUtc": null,
  "actions": {
    "canDelete": false,
    "canResume": false,
    "deleteEndpoint": "<string>",
    "resumeEndpoint": "<string>",
    "nextScreen": "<string>",
    "payEndpoint": "<string>"
  },
  "kind": "<string>",
  "familyGroupId": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscriptionId` | `Guid` | — |
| `status` | `string` | — |
| `statusNameAr` | `string` | — |
| `programId` | `Guid` | — |
| `programNameAr` | `string` | — |
| `programNameEn` | `string` | — |
| `bundleId` | `Guid` | — |
| `bundleNameAr` | `string` | — |
| `bundleNameEn` | `string` | — |
| `tier` | `RestaurantTier` | — |
| `tierNameAr` | `string` | — |
| `tierNameEn` | `string` | — |
| `tierLogoUrl` | `string?` | — |
| `mealsPerDay` | `int` | — |
| `imageUrl` | `string?` | — |
| `startDate` | `DateOnly` | — |
| `endDate` | `DateOnly` | — |
| `durationDays` | `int` | — |
| `includeFriday` | `bool` | — |
| `amount` | `decimal` | — |
| `currencyCode` | `string` | — |
| `deliveryAddress` | `string?` | — |
| `createdAtUtc` | `DateTime` | — |
| `isHoldActive` | `bool` | — |
| `holdRemainingSeconds` | `int?` | — |
| `holdExpiresAtUtc` | `DateTime?` | — |
| `actions` | `CustomerUncompletedActionsDto` | — |
| `kind` | `string` | `"INDIVIDUAL"` |
| `familyGroupId` | `Guid?` | `null` |


### CustomerUpgradeOptionDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs`.

```json
{
  "targetTier": "<Basic | Platinum | Elite>",
  "tierNameAr": "<string>",
  "tierNameEn": "<string>",
  "logoUrl": null,
  "isCurrent": false,
  "selectable": false,
  "accessibleRestaurantCount": 0,
  "currentRemainingCredit": 0,
  "targetRemainingPrice": 0,
  "gatewayRecovery": 0,
  "priceDifference": 0,
  "currencyCode": "<string>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `targetTier` | `RestaurantTier` | — |
| `tierNameAr` | `string` | — |
| `tierNameEn` | `string` | — |
| `logoUrl` | `string?` | — |
| `isCurrent` | `bool` | — |
| `selectable` | `bool` | — |
| `accessibleRestaurantCount` | `int` | — |
| `currentRemainingCredit` | `decimal` | — |
| `targetRemainingPrice` | `decimal` | — |
| `gatewayRecovery` | `decimal` | — |
| `priceDifference` | `decimal` | — |
| `currencyCode` | `string` | — |


### CustomerUpgradeOptionsDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs`.

```json
{
  "canUpgrade": false,
  "unavailableReason": null,
  "currentTier": "<Basic | Platinum | Elite>",
  "remainingServiceDays": 0,
  "totalServiceDays": 0,
  "options": [
    {
      "targetTier": "<Basic | Platinum | Elite>",
      "tierNameAr": "<string>",
      "tierNameEn": "<string>",
      "logoUrl": null,
      "isCurrent": false,
      "selectable": false,
      "accessibleRestaurantCount": 0,
      "currentRemainingCredit": 0,
      "targetRemainingPrice": 0,
      "gatewayRecovery": 0,
      "priceDifference": 0,
      "currencyCode": "<string>"
    }
  ],
  "upgradeEndpoint": "<string>",
  "paymentMethods": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `canUpgrade` | `bool` | — |
| `unavailableReason` | `string?` | — |
| `currentTier` | `RestaurantTier` | — |
| `remainingServiceDays` | `int` | — |
| `totalServiceDays` | `int` | — |
| `options` | `IReadOnlyList<CustomerUpgradeOptionDto>` | — |
| `upgradeEndpoint` | `string` | — |
| `paymentMethods` | `IReadOnlyList<CustomerPaymentMethodDto>?` | `null` |


### CustomerUpgradeRequest

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs`.

```json
{
  "targetTier": "<Basic | Platinum | Elite>",
  "callbackUrl": "<string>",
  "errorUrl": "<string>",
  "paymentMethodId": 0,
  "reason": null,
  "idempotencyKey": null,
  "clientContractVersion": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `targetTier` | `RestaurantTier` | — |
| `callbackUrl` | `string` | — |
| `errorUrl` | `string` | — |
| `paymentMethodId` | `int` | `3` |
| `reason` | `string?` | `null` |
| `idempotencyKey` | `string?` | `null` |
| `clientContractVersion` | `int?` | `null` |


### CustomerUpgradeRestaurantDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs`.

```json
{
  "id": "<UUID من السيرفر>",
  "nameAr": "<string>",
  "nameEn": "<string>",
  "logoUrl": null,
  "rating": null,
  "tier": "<Basic | Platinum | Elite>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `id` | `Guid` | — |
| `nameAr` | `string` | — |
| `nameEn` | `string` | — |
| `logoUrl` | `string?` | — |
| `rating` | `decimal?` | — |
| `tier` | `RestaurantTier` | — |


### CustomerUpgradeStartedDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs`.

```json
{
  "upgradeId": "<UUID من السيرفر>",
  "subscriptionId": "<UUID من السيرفر>",
  "fromTier": "<Basic | Platinum | Elite>",
  "toTier": "<Basic | Platinum | Elite>",
  "amount": 0,
  "currencyCode": "<string>",
  "paymentUrl": null,
  "isPaid": false,
  "status": "<Draft | Submitted | UnderReview | Approved | Rejected | Expired | ResubmissionRequired | NeedsChanges>",
  "remainingServiceDays": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `upgradeId` | `Guid` | — |
| `subscriptionId` | `Guid` | — |
| `fromTier` | `RestaurantTier` | — |
| `toTier` | `RestaurantTier` | — |
| `amount` | `decimal` | — |
| `currencyCode` | `string` | — |
| `paymentUrl` | `string?` | — |
| `isPaid` | `bool` | — |
| `status` | `VerificationStatus` | — |
| `remainingServiceDays` | `int` | — |


### CustomerUpgradeStatusDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs`.

```json
{
  "upgradeId": "<UUID من السيرفر>",
  "subscriptionId": "<UUID من السيرفر>",
  "fromTier": "<Basic | Platinum | Elite>",
  "toTier": "<Basic | Platinum | Elite>",
  "amount": 0,
  "currencyCode": "<string>",
  "status": "<Draft | Submitted | UnderReview | Approved | Rejected | Expired | ResubmissionRequired | NeedsChanges>",
  "isPaid": false,
  "isFailed": false,
  "effectiveAtUtc": null,
  "paymentUrl": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `upgradeId` | `Guid` | — |
| `subscriptionId` | `Guid` | — |
| `fromTier` | `RestaurantTier` | — |
| `toTier` | `RestaurantTier` | — |
| `amount` | `decimal` | — |
| `currencyCode` | `string` | — |
| `status` | `VerificationStatus` | — |
| `isPaid` | `bool` | — |
| `isFailed` | `bool` | — |
| `effectiveAtUtc` | `DateTime?` | — |
| `paymentUrl` | `string?` | — |


### CustomerUpgradeSummaryDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs`.

```json
{
  "subscriptionId": "<UUID من السيرفر>",
  "currentTier": {
    "tier": "<Basic | Platinum | Elite>",
    "tierNameAr": "<string>",
    "tierNameEn": "<string>",
    "logoUrl": null,
    "accessibleRestaurantCount": 0
  },
  "targetTier": {
    "tier": "<Basic | Platinum | Elite>",
    "tierNameAr": "<string>",
    "tierNameEn": "<string>",
    "logoUrl": null,
    "accessibleRestaurantCount": 0
  },
  "currentTierRestaurants": [
    {
      "id": "<UUID من السيرفر>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "logoUrl": null,
      "rating": null,
      "tier": "<Basic | Platinum | Elite>"
    }
  ],
  "targetTierRestaurants": [
    {
      "id": "<UUID من السيرفر>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "logoUrl": null,
      "rating": null,
      "tier": "<Basic | Platinum | Elite>"
    }
  ],
  "newlyAddedRestaurants": [
    {
      "id": "<UUID من السيرفر>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "logoUrl": null,
      "rating": null,
      "tier": "<Basic | Platinum | Elite>"
    }
  ],
  "effectiveFromDate": "<YYYY-MM-DD>",
  "effectiveNoticeAr": "<string>",
  "effectiveNoticeEn": "<string>",
  "restaurantRepetition": {
    "limit": 0,
    "restaurantCount": 0,
    "durationDays": 0
  },
  "currentRemainingCredit": 0,
  "targetRemainingPrice": 0,
  "priceDifference": 0,
  "currencyCode": "<string>",
  "remainingServiceDays": 0,
  "totalServiceDays": 0,
  "paymentMethods": [
    {
      "paymentMethodId": null,
      "code": "<string>",
      "nameAr": "<string>",
      "nameEn": "<string>",
      "integrationType": "<string>",
      "serviceCharge": null,
      "totalAmount": null,
      "currencyCode": "<string>",
      "isAvailable": false,
      "unavailableReason": null,
      "imageUrl": null
    }
  ],
  "upgradeEndpoint": "<string>"
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscriptionId` | `Guid` | — |
| `currentTier` | `CustomerUpgradeTierInfoDto` | — |
| `targetTier` | `CustomerUpgradeTierInfoDto` | — |
| `currentTierRestaurants` | `IReadOnlyList<CustomerUpgradeRestaurantDto>` | — |
| `targetTierRestaurants` | `IReadOnlyList<CustomerUpgradeRestaurantDto>` | — |
| `newlyAddedRestaurants` | `IReadOnlyList<CustomerUpgradeRestaurantDto>` | — |
| `effectiveFromDate` | `DateOnly` | — |
| `effectiveNoticeAr` | `string` | — |
| `effectiveNoticeEn` | `string` | — |
| `restaurantRepetition` | `RestaurantRepetitionSummaryDto` | — |
| `currentRemainingCredit` | `decimal` | — |
| `targetRemainingPrice` | `decimal` | — |
| `priceDifference` | `decimal` | — |
| `currencyCode` | `string` | — |
| `remainingServiceDays` | `int` | — |
| `totalServiceDays` | `int` | — |
| `paymentMethods` | `IReadOnlyList<CustomerPaymentMethodDto>` | — |
| `upgradeEndpoint` | `string` | — |


### CustomerUpgradeTierInfoDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs`.

```json
{
  "tier": "<Basic | Platinum | Elite>",
  "tierNameAr": "<string>",
  "tierNameEn": "<string>",
  "logoUrl": null,
  "accessibleRestaurantCount": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `tier` | `RestaurantTier` | — |
| `tierNameAr` | `string` | — |
| `tierNameEn` | `string` | — |
| `logoUrl` | `string?` | — |
| `accessibleRestaurantCount` | `int` | — |


### FamilyDeliveryAddressChoice

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/FamilySubscriptionDeliveryAmendments.cs`.

```json
{
  "id": "<UUID من السيرفر>",
  "label": null,
  "addressLine": "<string>",
  "regionId": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `id` | `Guid` | — |
| `label` | `string?` | — |
| `addressLine` | `string` | — |
| `regionId` | `Guid?` | — |


### FamilyDeliveryAddressDetails

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/FamilySubscriptionDeliveryAmendments.cs`.

```json
{
  "subscriptionId": "<UUID من السيرفر>",
  "currentVersion": 0,
  "acceptedAddress": null,
  "eligibleAddresses": [
    {
      "id": "<UUID من السيرفر>",
      "label": null,
      "addressLine": "<string>",
      "regionId": null
    }
  ]
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscriptionId` | `Guid` | — |
| `currentVersion` | `long` | — |
| `acceptedAddress` | `CustomerActiveDeliveryAddress?` | — |
| `eligibleAddresses` | `IReadOnlyList<FamilyDeliveryAddressChoice>` | — |


### FamilyDeliveryAddressRequest

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/FamilySubscriptionDeliveryAmendments.cs`.

```json
{
  "addressId": "<UUID من السيرفر>",
  "effectiveFromDate": "<YYYY-MM-DD>",
  "expectedVersion": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `addressId` | `Guid` | — |
| `effectiveFromDate` | `DateOnly` | — |
| `expectedVersion` | `long` | — |


### FamilyDeliveryAddressResult

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions/FamilySubscriptionDeliveryAmendments.cs`.

```json
{
  "subscriptionId": "<UUID من السيرفر>",
  "version": 0,
  "amendmentId": "<UUID من السيرفر>",
  "effectiveFromDate": "<YYYY-MM-DD>",
  "affectedDayIds": [
    "<UUID من السيرفر>"
  ],
  "skippedDayIds": [
    "<UUID من السيرفر>"
  ]
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscriptionId` | `Guid` | — |
| `version` | `long` | — |
| `amendmentId` | `Guid` | — |
| `effectiveFromDate` | `DateOnly` | — |
| `affectedDayIds` | `IReadOnlyList<Guid>` | — |
| `skippedDayIds` | `IReadOnlyList<Guid>` | — |


### FamilyRefundBeneficiaryDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/SubscriptionCancellationContracts.cs`.

```json
{
  "payerCustomerUserId": "<UUID من السيرفر>",
  "confirmedPaidAmount": 0,
  "refundAmount": 0,
  "currencyCode": "<string>",
  "walletTransactionId": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `payerCustomerUserId` | `Guid` | — |
| `confirmedPaidAmount` | `decimal` | — |
| `refundAmount` | `decimal` | — |
| `currencyCode` | `string` | — |
| `walletTransactionId` | `Guid?` | `null` |


### RequestSubscriptionCancellationCommand

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/SubscriptionCancellationContracts.cs`.

```json
{
  "subscriptionId": "<UUID من السيرفر>",
  "callerUserId": "<UUID من السيرفر>",
  "callerUserType": "<Customer | Restaurant | Driver | Admin | Influencer | DeliveryManager>",
  "reason": null
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `subscriptionId` | `Guid` | — |
| `callerUserId` | `Guid` | — |
| `callerUserType` | `UserType` | — |
| `reason` | `string?` | `null` |


### RestaurantRepetitionSummaryDto

المصدر: `src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs`.

```json
{
  "limit": 0,
  "restaurantCount": 0,
  "durationDays": 0
}
```

| الحقل | نوع المصدر | القيمة الافتراضية إن وجدت |
|---|---|---|
| `limit` | `int` | — |
| `restaurantCount` | `int` | — |
| `durationDays` | `int` | — |

