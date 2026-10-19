# عقد الموبايل: اختيار باقة العرض ثم الدفع مباشرة

الإصدار: 1.0 — 2026-10-19. النطاق: الاشتراك الفردي. هذه وثيقة للكود في المستودع؛ يلزم نشره وتطبيق migration الخاصة بـ `OfferCheckoutQuotes` قبل استخدام الميزات على السيرفر. أمثلة الأرقام والمعرفات هنا لتوضيح العقد فقط، وليست بيانات تجارية أو أسعارًا مضمونة.

## 1. ترتيب الاستدعاءات

1. افتح تفاصيل العرض بواسطة `GET /api/v1/customer/offers/{offerId}/details`.
2. احصل على الكروت من `GET /api/v1/customer/offers/{offerId}/purchase-options`.
3. عند اختيار كارت، اطلب ملخص الفردي بنفس `offerId` وتركيبة الكارت. اعرض نتيجته والإجمالي وزر الشراء داخل نفس الشاشة؛ لا تحتاج شاشة ملخص منفصلة.
4. اسمح بالشراء فقط عندما `canCheckout=true`، و`quoteId` موجودة وغير منتهية، وبعد موافقة المستخدم على الشروط والتاريخ والإجمالي.
5. خزّن مفتاح المحاولة محليًا قبل إرسال checkout. افتح `paymentUrl` الراجعة، ثم تابع حالة الدفع من السيرفر عند عودة التطبيق.

كل الاستدعاءات أدناه تتطلب هوية عميل مصادق عليها. الهوية من الـ token؛ لا ترسل `customerId` لتحديد المشتري. احتفظ بالقيم الآلية والمعرفات كما يرجعها السيرفر، ولا تطابق باقة بالاسم أو الصورة.

## 2. بيانات العرض

تفاصيل العرض ترجع `offer` مع `serverNowUtc`, `currencyCode`, `moneyScale`. استخدم `offer.bannerImageUrl`, الأسماء والأوصاف العربية/الإنجليزية إن وجدت، `startsAtUtc`, `endsAtUtc`, `promoCode`, الشروط وحدود الاستخدام والخصم.

`discountType=Percentage` و`discountRate=0.2` معناهما 20%. `FixedAmount` يستخدم `discountAmountKwd`. المؤقت مبني على `endsAtUtc` مقارنة بـ `serverNowUtc`؛ لا تمد صلاحية العرض اعتمادًا على ساعة الجهاز. السعر الفعلي والأهلية من الكروت والملخص.

فراغ `packageTargets` لا يعني بناء باقات من اسم العرض. المسار الجديد يكتشف التركيبات من الكتالوج الحقيقي، ويطبق نطاق البرامج والباقات والمطاعم والمدد. عرض الحملة الذي لم تتم مزامنته كـ offer دائم قد يظهر في قائمة العروض؛ مسار الشراء المباشر يتطلب معرفًا موجودًا فعليًا في جدول العروض، وإلا يرجع 404. لا تستبدل `campaignId` بـ `offerId` من الموبايل.

## 3. الكروت المناسبة

```http
GET /api/v1/customer/offers/{offerId}/purchase-options
GET /api/v1/customer/offers/{offerId}/purchase-options?startDate=2026-10-26&includeFriday=false
Authorization: Bearer {token}
Accept-Language: ar
```

`startDate` اختياري، وصيغته `yyyy-MM-dd`. عند غيابه يحسب السيرفر أقرب تاريخ صالح وفق قواعد الجدولة. `includeFriday` اختياري وافتراضيًا `false`. لا تثبت `durationDays=26` عند فتح هذه الشاشة؛ الكروت تحدد المدد المتاحة من الإعدادات ونطاق العرض.

الاستجابة 200 كائن مباشر:

```json
{
  "offerId": "11111111-1111-1111-1111-111111111111",
  "subscriptionType": "Individual",
  "serverNowUtc": "2026-10-19T12:00:00Z",
  "prerequisites": {
    "isComplete": true,
    "hasDeliveryAddress": true,
    "hasAllergies": true,
    "hasDietaryPreferences": true,
    "deliveryRegionId": "22222222-2222-2222-2222-222222222222"
  },
  "blockingReasons": [],
  "options": [
    {
      "programId": "33333333-3333-3333-3333-333333333333",
      "programNameAr": "اسم البرنامج من الكتالوج",
      "programNameEn": "Catalog program name",
      "bundleId": "44444444-4444-4444-4444-444444444444",
      "bundleNameAr": "اسم الباقة من الكتالوج",
      "bundleNameEn": "Catalog bundle name",
      "imageUrl": null,
      "tier": "Basic",
      "tierNameAr": "اسم التصنيف من الإعدادات",
      "tierNameEn": "Configured tier name",
      "durationDays": 6,
      "startDate": "2026-10-26",
      "endDate": "2026-11-01",
      "includeFriday": false,
      "totalServiceDays": 6,
      "mealsPerDay": 2,
      "basePrice": 100.000,
      "campaignDiscount": 20.000,
      "promoDiscount": 0.000,
      "walletCredit": 5.000,
      "gatewayFee": 2.077,
      "finalPrice": 77.250,
      "currencyCode": "KWD",
      "canCheckout": true,
      "blockingReasons": [],
      "discount": 20.000
    }
  ],
  "familyFlowDestination": null
}
```

قد يحذف إعداد JSON الحقول الاختيارية ذات القيمة `null`؛ تعامل مع غيابها مثل null. الصورة اختيارية؛ اعرض حالة الصورة غير المتاحة محليًا، ولا تستبدلها بصورة باقة أخرى. أسماء tier الآلية ثابتة؛ اعرض `tierNameAr/En` بدل تحويل enum إلى تسمية تجارية ثابتة.

`durationDays` و`totalServiceDays` قيمتان منفصلتان؛ استخدم التاريخ والعدد كما يرجعان من الجدولة. لا تفرض تسميات «شهر/نصف شهر/أسبوع» على مدد غير مدعومة. `mealsPerDay` مشتقة من أعداد slots المحفوظة؛ التركيبات التي لا تحتوي slots صحيحة لا تتحول إلى وجبة افتراضية.

السيرفر يفحص التغطية والتوفر طوال الفترة والتصنيف والمطاعم الداخلة في العرض. عدم اكتمال البيانات أو عدم توفر التسعير/الفترة يرجع `canCheckout=false` وأسبابًا؛ حقول السعر تكون null عند تعذر إقرار سعر صالح. قد ترجع `options: []`؛ اعرض السبب ولا تنشئ اختيارات محلية.

`blockingReasons` العامة تخص الحساب/العرض. أسباب كل كارت تخص الحساب وتركيبته. شكل السبب:

```json
{
  "code": "checkout.address_required",
  "messageAr": "يرجى إضافة عنوان توصيل للمتابعة.",
  "messageEn": "Please add a delivery address to continue.",
  "field": "address",
  "advice": {
    "retryable": false,
    "refreshSummaryRequired": true,
    "refreshAvailabilityRequired": false
  }
}
```

الأسباب تشمل العنوان، تأكيد الحساسية والتفضيلات، الاشتراك الموجود، نقص الكتالوج/التسعير، عدم توفر الفترة وعدم أهلية العرض. بعد استكمال البيانات أعد الكروت والملخص مع الاحتفاظ بـ IDs المختارة إن ظلت متاحة.

لو `subscriptionType=Family` تكون `options=[]` و`familyFlowDestination` تشير إلى وجهة العائلة. هذه وجهة تنقل للتطبيق وليست endpoint HTTP. استخدم فلو العائلة وعقوده؛ لا ترسل عرضه إلى checkout الفردي.

## 4. الملخص عند اختيار كارت

```http
GET /api/v1/customer/subscription-flow/summary?offerId={offerId}&programId={programId}&bundleId={bundleId}&tier=Basic&durationDays=6&startDate=2026-10-26&includeFriday=false
```

الحقول المطلوبة: `programId`, `bundleId`, `tier`, `durationDays`, `startDate`. أرسل `offerId` في فلو العرض الجديد. `includeFriday` اختياري؛ كرر قيمته من الكارت. `promoCode` اختياري ويُرسل URL-encoded. لا ترسل body مع GET. حقول pagination للمطاعم اختيارية ولا تغير الإجمالي أو التوفر.

الاستجابة كائن `SubscriptionOrderSummaryDto` مباشر. يحتفظ ببيانات البرنامج والباقة والتصنيف والتواريخ والوجبات والعنوان والمطاعم. الحقول التي يحتاجها قرار الدفع:

```json
{
  "basePrice": 100.000,
  "campaignDiscount": 20.000,
  "promoDiscount": 0.000,
  "walletCredit": 5.000,
  "gatewayFee": 2.077,
  "finalPrice": 77.250,
  "currencyCode": "KWD",
  "startDate": "2026-10-26",
  "paymentDate": "2026-10-26",
  "endDate": "2026-11-01",
  "serviceDates": ["2026-10-26", "2026-10-27", "2026-10-28", "2026-10-29", "2026-10-31", "2026-11-01"],
  "canCheckout": true,
  "checkoutBlockReason": null,
  "blockingReasons": [],
  "eligibility": {"isEligible": true, "reasons": []},
  "quoteId": "55555555-5555-5555-5555-555555555555",
  "summaryExpiresAtUtc": "2026-10-19T12:15:00Z",
  "serverNowUtc": "2026-10-19T12:00:00Z",
  "termsAcceptanceRequired": true,
  "checkoutEndpoint": "POST /api/v1/customer/subscriptions/checkout"
}
```

المثال أعلاه مجموعة حقول من الاستجابة، وليس حذفًا لباقي حقول DTO. `availability` كائن التوفر الحالي وله `status` وأعداد المطاعم وسياق الفترة؛ لا تفترض أنه مجرد `{isAvailable:true}`. `checkoutBlockReason` سبب مختصر؛ `blockingReasons` للتفاصيل، و`canCheckout` قرار السماح. عند تغير الاختيار أو العنوان أو الكوبون أو التاريخ اطلب ملخصًا جديدًا.

الحقول المضمونة للسعر في ملخص الفردي هي `basePrice`, `campaignDiscount`, `promoDiscount`, `walletCredit`, `gatewayFee`, `finalPrice`. لا يعتمد الموبايل على وجود `subtotal` أو `discount` في هذا الملخص. إجمالي الخصم المعروض = `campaignDiscount + promoDiscount`؛ كائن coupon يكرر جزء الخصم وليس مبلغًا إضافيًا. استخدم `finalPrice` كما هو؛ رسوم البوابة والتقريب النهائي من السيرفر وقد لا يساوي جمع الحقول المقربة محليًا.

الـ quote تحفظ السعر والاختيار والعميل وصورة العنوان. مدة صلاحيتها من `Checkout:QuoteLifetimeMinutes` (افتراضيًا 15 دقيقة)؛ اعتمد timestamp الراجعة. ليست حجز سعة أو ضمان توفر. الملخص الممنوع لا يصدر quote قابلة للدفع، وفشل حفظها يفشل الاستدعاء بدل إرجاع نجاح دونها. اكتمال الدفع يتطلب إعادة التحقق والحجز في checkout.

### الكوبون

أرسل نفس الكود في الملخص والدفع؛ احتفظ بإدخال المستخدم عند الرفض. كائن `coupon` إن وجد:

```json
{
  "code": "SAVE20",
  "status": "valid",
  "offerId": "11111111-1111-1111-1111-111111111111",
  "discountAmount": 20.000,
  "messageAr": "رسالة التقييم العربية من السيرفر",
  "messageEn": "Server coupon evaluation message"
}
```

الحالات الموثقة في الخدمة: `valid`, `not_found`, `inactive`, `not_started`, `expired`, `usage_exhausted`, `stacking_conflict`, `ineligible`, `self_referral`, `already_attributed`, `reward_policy_unset`. تعامل مع غير المعروفة كحالة تحتاج تحديثًا، لا كنجاح. لا تفترض جمع عرض وكوبون؛ سياسة السيرفر تحدد المصدر/الخصم المطبق. كوبون مرفوض مع عرض مباشر يمنع إصدار quote والدفع إلى أن يصححه المستخدم أو يزيله.

## 5. بدء الدفع

```http
POST /api/v1/customer/subscriptions/checkout
Content-Type: application/json
```

```json
{
  "programId": "33333333-3333-3333-3333-333333333333",
  "bundleId": "44444444-4444-4444-4444-444444444444",
  "tier": "Basic",
  "durationDays": 6,
  "startDate": "2026-10-26",
  "includeFriday": false,
  "offerId": "11111111-1111-1111-1111-111111111111",
  "quoteId": "55555555-5555-5555-5555-555555555555",
  "idempotencyKey": "mobile-persisted-key-for-this-attempt",
  "paymentMethodId": 3,
  "callbackUrl": "https://your-allowed-app-host/payment/success",
  "errorUrl": "https://your-allowed-app-host/payment/error"
}
```

بدّل عناوين المثال بعناوين مسموح بها في إعدادات السيرفر. `callbackUrl` و`errorUrl` والاختيار مطلوبة. `paymentMethodId` اختياري افتراضيًا 3؛ استخدم الطرق المدعومة من السيرفر. `promoCode` اختياري، ويجب مطابقته لما وافق عليه المستخدم في quote. `correlationId` و`clientContractVersion` اختياريان؛ لا يحلان محل مفتاح المحاولة.

`idempotencyKey` في **body**، مطلوب مع `quoteId`، غير فارغ وبحد أقصى 200 حرف. أرسل نفس الطلب ونفس المفتاح عند timeout أو retry. احتفظ به عبر إعادة تشغيل التطبيق. نفس المفتاح مع تغيير الاختيار/التاريخ/العرض/الكوبون/طريقة الدفع/روابط العودة يرجع conflict. لا تغيّر المفتاح تلقائيًا لمجرد timeout.

نجاح 200 هو `CheckoutStartedDto` مباشر، وأهم حقوله:

```json
{
  "subscriptionId": "66666666-6666-6666-6666-666666666666",
  "paymentUrl": "https://payzah.net/example-hosted-payment",
  "holdExpiresAtUtc": "2026-10-19T12:16:00Z",
  "menuPlanVersionId": "77777777-7777-7777-7777-777777777777",
  "finalPrice": 77.250,
  "paymentStatus": "PendingPayment",
  "fulfillmentStatus": "PendingPayment",
  "startDate": "2026-10-26",
  "paymentDate": "2026-10-26"
}
```

استخدم `subscriptionId` مع status وresume. لا تستخدم `offerId`, `quoteId`, أو معرف باقة بدل الاشتراك. `paymentUrl` ليست دليل نجاح الدفع. استجابة الطلب الأصلي الناجح تعاد كما هي عند retry حتى لو انتهت quote؛ لا تعِد فتح URL بعد `holdExpiresAtUtc`. اقرأ الحالة/الاستئناف بدل اعتبار replay حجزًا جديدًا.

طلبات العملاء القديمة دون `quoteId` تستمر بسلوكها القديم. ضمان إعادة نفس النتيجة في هذا التحديث يخص فلو **quote + stable key**؛ مجرد `correlationId` أو مفتاح دون quote لا يمنح هذا الضمان.

## 6. أخطاء HTTP وسلوك الموبايل

شكل أخطاء checkout الحالية غالبًا `{ "error": "code" }`، وقد يضم payload إضافية. أخطاء تفاصيل العرض والكروت تستخدم ProblemDetails وفيها `code`؛ أخطاء summary لها محولها الحالي. اقرأ `error` أو `code` بحسب المسار، ولا تفترض envelope موحدة أو نصين ar/en بكل خطأ. اعرض ترجمة الكود في التطبيق، واستخدم الرسائل الثنائية عندما تكون ضمن coupon أو blockingReasons.

| HTTP | الكود/الحالة | الإجراء |
|---|---|---|
| 400 | `checkout.idempotency_key_required`, `checkout.idempotency_key_invalid` | صحح المفتاح؛ لا تعاود الطلب كما هو |
| 400 | `checkout.redirect_url_not_allowed`, `checkout.start_date_required`, `checkout.start_date_invalid` | صحح الروابط أو أعد اختيار تاريخ صالح |
| 400 | `checkout.coupon_rejected`, `checkout.coupon_full_discount_unsupported` | اعرض السبب، صحح/أزل الكود وأعد الملخص |
| 404 | `offers.customer.unavailable` | العرض/الـ quote غير متاح أو ليس للعميل؛ لا تخمن معرفًا بديلًا |
| 409 | `checkout.quote_changed` | الاختيار/السعر/العنوان تغير أو quote استُخدمت؛ تحقق من المحاولة السابقة ثم اطلب ملخصًا جديدًا عند الحاجة |
| 409 | `checkout.idempotency_conflict` | المفتاح مستخدم لطلب مختلف؛ استرجع المحاولة الأصلية أولًا |
| 409 | `checkout.in_progress` | تابع الاشتراك الراجع؛ لا تنشئ محاولة دفع ثانية |
| 409 | `checkout.address_required`, `checkout.allergies_required`, `checkout.dietary_preferences_required`, `checkout.existing_subscription` | استكمل الإجراء أو اعرض الاشتراك الموجود |
| 410 | `checkout.quote_expired` | quote لم تعد صالحة لبدء محاولة جديدة؛ أعد الملخص والتوفر |
| 422 | `checkout.selection_not_eligible` | أعد الكروت والملخص؛ الاختيار لم يعد مؤهلًا |
| 400 | `checkout.capacity_unavailable` | السعة تغيرت أثناء الحجز؛ أعد التوفر والملخص |
| 502 | `checkout.payment_init_failed` في المسار القديم | لا تفترض أن retry آمن؛ تحقق من الحالة وفق عقد المسار |

العنوان الناقص قبل الملخص قد يرجع `409 subscription_flow.profile_incomplete` مع readiness. أخطاء المدد والكتالوج والتسعير تحتفظ بأكوادها الحالية. لا تعالج كل 409 ككوبون ولا كل 400 كخطأ شبكة.

مثال المحاولة غير المحسومة:

```json
{
  "error": "checkout.in_progress",
  "subscriptionId": "66666666-6666-6666-6666-666666666666",
  "paymentStatusUrl": "/api/v1/customer/subscriptions/66666666-6666-6666-6666-666666666666/payment"
}
```

## 7. حالة الدفع والاستئناف

```http
GET /api/v1/customer/subscriptions/{subscriptionId}/payment
GET /api/v1/customer/subscriptions/{subscriptionId}/resume
POST /api/v1/customer/subscriptions/{subscriptionId}/resume
```

حالة الدفع 200:

```json
{
  "subscriptionId": "66666666-6666-6666-6666-666666666666",
  "paymentStatus": "PendingPayment",
  "isPaid": false,
  "isFailed": false,
  "amount": 77.250,
  "currencyCode": "KWD"
}
```

`paymentStatus` الحالية مشتقة من حالة الاشتراك وقد تشمل حالات fulfillment؛ ليست قائمة منفصلة ثابتة `Pending/Paid/Failed` فقط. اعتمد `isPaid` لتأكيد النجاح، و`isFailed` للفشل كما يقرر السيرفر. لا تعتبر قيمة غير معروفة نجاحًا؛ حالة Expired المحلية ليست دائمًا `isFailed=true`، فاقرأ الاستئناف والحالة بدل استنتاج النتيجة محليًا. تحقق بعد العودة من WebView أو deep link أو استعادة التطبيق. استخدم polling متباعدًا بتراجع، وأوقفه عند نتيجة حاسمة أو مغادرة الشاشة؛ انقطاع الشبكة ليس نتيجة دفع.

GET resume مجرد preview يضم `canPayDirectly`, `reason`, `holdExpiresAtUtc`, `suggestedStartDate`, `checkoutStartDate`, ومسارات الاستئناف. POST resume يقبل اختياريًا `startDate`, `callbackUrl`, `errorUrl`, `paymentMethodId` وفق العقد القديم.

في محاولة quote ناجحة محفوظة، POST resume يعيد رابطها فقط إذا الاشتراك PendingPayment والحجز صالح. لو انتهى الحجز أو أصبحت الحالة غير مناسبة يرجع conflict بدل إعادة URL قديمة أو إصدار فاتورة ثانية. المحاولة التي لم يثبت نجاح تهيئتها تبقى `checkout.in_progress` مع متابعة الدفع؛ لا تستخدم resume كوسيلة لتجاوزها.

**حد الاستعادة الحالي:** Payzah يحفظ journal محاولة provider قبل الاتصال. عند فقد الرد يحاول السيرفر قراءة الحالة باستخدام المرجع المحفوظ الذي يدعمه provider. إذا لم يُحفظ PaymentID أو لا يمكن إثبات النتيجة، لا يعيد إنشاء فاتورة تلقائيًا. قد تتطلب المحاولة مراجعة/تسوية تشغيلية، خصوصًا تعطل العملية بين حفظ الحجز وبدء الاتصال. لا نعد باستعادة آلية كاملة أو exactly-once لدى provider. لا تبدأ محاولة جديدة حتى تتأكد نتيجة السابقة.

## 8. التواريخ والدقة والتسليم

في الفلو الفردي الحالي `startDate` المطلوب هو **أول يوم توصيل مختار**. الكروت تحسب أقرب يوم مرة واحدة إذا غاب، ثم ترسله كما هو للملخص وcheckout. لا تضف أيام التجهيز في Flutter. `paymentDate` echo للتاريخ المختار في استجابات الفردي، ويساوي `startDate` في هذا المسار؛ ليس وقت دفع البنك ولا إثبات الدفع. سلوك تواريخ العائلة منفصل.

تاريخ الخدمة `yyyy-MM-dd`، وتواريخ `*Utc` ISO UTC. قواعد اليوم التجاري الكويت/UTC+3 من السيرفر. اعرض العملة كما هي (KWD، ثلاث خانات) ولا تعِد حساب الرسوم/التقريب. عداد صلاحية quote مستقل عن عداد حجز checkout.

قبل التسليم على بيئة حقيقية: تحقق من migration والكتالوج ونطاق العرض والأسعار/slots، إعداد العناوين والتغطية والسعة والقوائم طوال المدة، إعداد Payzah وروابط العودة، ثم تجربة دفع sandbox مرخصة من إنشاء checkout حتى تأكيد السيرفر. الاختبارات المحلية ورفع Git وحدهما لا يثبتان النشر أو نجاح بوابة الدفع.
