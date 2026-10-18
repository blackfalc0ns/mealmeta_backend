# عقود الباك للموبايل — الإصدار 2

هذا المستند يثبت الحقول الإضافية المشتركة التي يقرأها الموبايل. كل الحقول الجديدة additive، لذلك يظل العميل القديم قادرًا على قراءة العقود السابقة.

## السعر والملخص

### التوافق بين أسماء السعر

العقد القديم يظل مضمونًا. العلاقة الرسمية هي:

```text
subtotal = basePrice
discount = campaignDiscount + promoDiscount
finalPrice = subtotal - discount - walletCredit + gatewayFee
```

`subtotal` و`discount` حقول تجميعية مضافة، وليسا بديلًا يغيّر معنى الحقول القديمة. العميل القديم يقرأ الحقول القديمة، والعميل الجديد يفضل الحقول التجميعية مع الاحتفاظ بالتفاصيل عند الحاجة.

ملخص الاشتراك الفردي والعائلي يدعم:

```json
{
  "subtotal": 300.000,
  "discount": 30.000,
  "walletCredit": 0.000,
  "gatewayFee": 0.000,
  "finalPrice": 270.000,
  "currencyCode": "KWD",
  "blockingReasons": [],
  "eligibility": { "isEligible": true, "reasons": [] },
  "summaryExpiresAtUtc": "2026-10-18T12:05:00Z",
  "serverNowUtc": "2026-10-18T12:00:00Z"
}
```

المبالغ بالدينار الكويتي وبثلاث خانات عشرية. وقت الخادم UTC هو المرجع للعدادات وانتهاء الملخص والحجز.

## أسباب المنع

كل سبب يحتوي `code` ثابتًا ورسالة عربية/إنجليزية وقرار retry اختياريًا:

```json
{
  "code": "checkout.address_required",
  "field": "address",
  "messageAr": "عنوان التوصيل مطلوب",
  "messageEn": "Delivery address is required",
  "advice": {
    "retryable": false,
    "refreshSummaryRequired": true,
    "refreshAvailabilityRequired": false
  }
}
```

## الكوبون

كائن `coupon` داخل summary له الشكل التالي:

```json
{
  "code": "WELCOME10",
  "status": "valid",
  "offerId": "00000000-0000-0000-0000-000000000000",
  "discountAmount": 10.000,
  "messageAr": "تم تطبيق الكوبون",
  "messageEn": "Coupon applied"
}
```

القيم الرسمية لـ `status`: `valid`, `not_found`, `expired`, `ineligible`, `stacking_conflict`, `usage_exhausted`. عند الفشل يحتفظ التطبيق بالكود ويعرض الرسالة القادمة من الخادم؛ لا يعيد حساب الخصم محليًا.

## العائلة

`GET /api/v1/customer/family-subscription-flow/summary` يعيد عدد الأفراد، `members[]`، حدود الباقة `minMembers/maxMembers`، السعر لكل عضو، `selectedNewSubscriberCount`، ونتيجة أهلية وتغطية كل عضو. يرسل الموبايل `memberId` في query عند اختيار بطاقة عضو، بينما يرسل checkout `batchId` و`batchVersion` و`idempotencyKey` من آخر summary.

الـ response الكامل المختصر:

```json
{
  "familyGroupId": "<uuid>",
  "familyName": "عائلة أحمد",
  "selectedMemberId": "<uuid>",
  "members": [{
    "memberId": "<uuid>",
    "displayName": "أحمد",
    "role": "Manager",
    "setupStatus": "Ready",
    "checkoutSelected": true,
    "isReady": true,
    "memberSubscriptionId": null,
    "upcomingSubscriptionId": null,
    "programId": "<uuid>",
    "bundleId": "<uuid>",
    "selectedTier": "Basic",
    "durationDays": 26,
    "deliveryAddressId": "<uuid>",
    "eligibility": { "isEligible": true, "reasons": [] },
    "availability": { "isAvailable": true, "reasons": [] }
  }],
  "subtotal": 300.000,
  "familyDiscountPercent": 0.100,
  "familyDiscountAmount": 30.000,
  "finalPrice": 270.000,
  "currencyCode": "KWD",
  "selectedNewSubscriberCount": 2,
  "batchId": "<uuid>",
  "batchVersion": 7,
  "canCheckout": true,
  "checkoutBlockReason": null,
  "blockingReasons": [],
  "summaryExpiresAtUtc": "2026-10-18T12:05:00Z",
  "serverNowUtc": "2026-10-18T12:00:00Z"
}
```

`batchId` و`batchVersion` مصدرهما summary ويُرسلان في checkout. checkout يعيد `familyGroupId` و`paymentAnchorSubscriptionId` و`pendingCheckout`/بيانات الرابط عند نجاح بدء الدفع. payment-status لا ينشئ هذه القيم؛ يستخدم `subscriptionId` للفردي أو `familyGroupId` لقراءة محاولة العائلة ويعيد الحالة الحالية.

### المسارات القانونية

- الفلو الفردي: `/api/v1/customer/subscription-flow/summary`.
- الفلو العائلي: `/api/v1/customer/family-subscription-flow/summary`.

لا تستعمل المسار الفردي داخل شاشة العائلة، ولا ترسل معرف العائلة إلى `/subscriptions/{id}/resume`.

## العروض

`GET /api/v1/customer/offers/{offerId}/details` يعيد `offer` و`serverNowUtc` و`currencyCode` و`moneyScale`. يحتوي العرض على أهداف البرنامج/الباقة والتصنيف والمدد، حالة التوفر، وcodes عدم الأهلية عند وجودها.

## الدفع والاستئناف

يظل `paymentUrl` و`holdExpiresAtUtc` و`subscriptionId`/`paymentAnchorSubscriptionId` مصدر بدء الدفع فقط؛ الرجوع من WebView لا يثبت الدفع. يجب إعادة قراءة حالة الخادم.

مفتاح idempotency ثابت للمحاولة المنطقية نفسها، ومفتاح جديد عند تغيير السعر أو الاختيار. إعادة استخدام المفتاح مع payload مختلف تعارض.

`subscriptionId` هو معرف الاشتراك الفردي المستخدم في `GET payment`, `GET/POST resume`, وpolling الفردي. `paymentAnchorSubscriptionId` هو معرف مرساة الدفع في checkout العائلي؛ يستخدم للتحقق من محاولة البوابة، بينما قراءة حالة العائلة تتم عبر `familyGroupId` و`pendingCheckout`.

### انتهاء الحجز

عند انتهاء `holdExpiresAtUtc` يعيد الخادم `409` بالرمز `checkout.capacity_hold_expired` (أو رمز العائلة المكافئ إذا أعلنه endpoint)، ولا يعيد رابطًا صالحًا قديمًا. يحتوي الخطأ على `refreshSummaryRequired=true` و`refreshAvailabilityRequired=true` و`retryable=true`. يعيد الموبايل summary ثم يبدأ checkout/retry جديدًا بمفتاح idempotency جديد. لا يستخدم `resume` القديم إذا تغير السعر أو التوفر.

### الحالات الرسمية

حالات الدفع العامة: `Pending`, `Paid`, `Failed`, `Expired`, `Cancelled`, `Refunded`, `PartiallyRefunded`. حالات العرض الإضافية مثل `PaidPreparingCalendar` و`ReviewRequired` ليست نجاح دفع؛ لا يعلن التطبيق النجاح إلا بعد حالة خادم مؤكدة.

## HTTP والأخطاء

| HTTP | الاستخدام | الإجراء |
|---|---|---|
| `400` | request أو رابط رجوع أو مدة غير صالحة | أصلح الحقل، ولا تعيد الطلب كما هو |
| `404` | مورد غير موجود أو عرض غير متاح للعميل | حدّث الشاشة/الكتالوج |
| `409` | تعارض حالة، سعر قديم، حجز منتهٍ، idempotency mismatch | أعد summary أو الحالة حسب `advice` |
| `410` | مورد منتهي نهائيًا عندما يعلنه endpoint | ابدأ flow جديدًا |
| `422` | request مفهوم لكن غير قابل للتنفيذ تجاريًا | اعرض `blockingReasons` ولا تبدأ الدفع |

كل خطأ قابل للعرض يحمل `code` ورسالة عربية/إنجليزية، وقد يضيف `field` و`advice.retryable` ورايات refresh.

## العنوان والتغطية

للفردي: readiness/summary تستخدم العنوان الافتراضي من `GET /api/v1/customers/me/addresses` وتتحقق من الحساسية والتفضيلات عبر endpoints food-preferences، ثم تفحص التغطية عبر `restaurant-availability`. للعائلة، لكل عضو عنوان مستقل ومسار coverage preview تحت `family-subscription-flow/members/{memberId}/addresses`. لا يسمح checkout إذا كان `address_required`, `allergies_required`, `dietary_preferences_required`, `no_restaurants_for_address`, أو `no_restaurants_for_selection`.

## التوافق مع العميل القديم

حذف الحقول الجديدة غير مطلوب. إذا لم يرسل العميل headers أو `clientContractVersion`، يستخدم الخادم العقد القديم ويعيد الحقول القديمة؛ لا يفشل الطلب بسبب غياب الحقول المضافة. `Idempotency-Key`/`idempotencyKey` اختياري في العميل القديم، لكن الإصدارات الجديدة يجب أن ترسله.

## الإعلانات

`GET /api/v1/customer/advertising/placements` يعرض الإعلان داخل نافذة `displayStartsAtUtc`/`displayEndsAtUtc` فقط. `POST events` idempotent على `(placementId,eventId)`، ويجب تجاهل إعادة إرسال نفس الحدث.
