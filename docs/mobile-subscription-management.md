# إدارة الاشتراك والفواتير للموبايل

Base URL: `http://maelmate.runasp.net/api/v1`

كل المسارات تحتاج Customer access token.

## تحميل الشاشة

```http
GET /customer/subscriptions/management?includeHistory=true
```

الاستجابة تحتوي:

- `currentSubscription`: الاشتراك الجاري الذي يظهر في الكارت الرئيسي.
- `subscriptions`: الاشتراك الجاري والاشتراكات السابقة.
- `invoices`: إيصالات الدفع الخاصة بالاشتراك الجاري.
- `emptyStateCode`: يساوي `no_current_subscription` عندما لا يوجد اشتراك جارٍ.

داخل الاشتراك استخدم القيم المرجعة مباشرة:

- `progressPercentage` لرسم شريط الاستهلاك.
- `completedServiceDays` و`remainingServiceDays` بدل حسابهما في الموبايل.
- `suggestedRenewalStartDate` كبداية مقترحة للتجديد.
- `status` ككود ثابت، و`statusNameAr` للعرض العربي.
- `actions.canRenew`, `actions.canFreeze`, `actions.canCancel` لتفعيل الأزرار.
- إذا كان زر غير متاح اعرض سبب `*UnavailableReason` أو اخفِ الزر حسب التصميم.

بيانات التفاصيل لاشتراك واحد:

```http
GET /customer/subscriptions/{subscriptionId}/management
```

الفواتير فقط:

```http
GET /customer/subscriptions/{subscriptionId}/invoices
```

`maskedCardNumber` حاليًا `null` لأن MealMate لا يخزن بيانات البطاقة. اخفِ كارت البطاقة عند غيابها. كذلك `canDownload=false` إلى أن توفر بوابة الدفع مستند PDF حقيقي؛ لا تنشئ رابط تحميل محلي وهمي.

حالة الفاتورة تكون `PAID` أو `PAID_PENDING_FULFILLMENT` أو `REVIEW_REQUIRED`. محاولات الدفع الفاشلة أو Webhooks المرفوضة لا تظهر كفواتير.

## التجميد

افتح اختيار التاريخ باستخدام:

- `actions.freezeStartDelayDays`: أقل مهلة قبل بداية التجميد.
- `actions.maximumFreezeDays`: أقصى مدة.
- `actions.freezeExtendsSubscriptionEnd`: هل نهاية الاشتراك تمتد بعد التجميد.

ثم أرسل:

```http
POST /customer/subscriptions/{subscriptionId}/freeze-requests
Content-Type: application/json

{
  "startDate": "2026-08-20",
  "endDate": "2026-08-26",
  "reason": "سفر"
}
```

عند وجود طلب قيد المراجعة سترجع الشاشة `pendingFreeze` ويصبح `canFreeze=false`.

## التجديد

```http
POST /customer/subscriptions/{subscriptionId}/renewal-requests
```

يرجع `programId`, `bundleId`, `tier`, `durationDays`, `includeFriday`, `regionId` و`suggestedStartDate`. افتح بها فلو الاشتراك العادي، واسمح للعميل بمراجعة المطعم والسعر قبل إنشاء Checkout جديد. الطلب idempotent ولا يجدد أو يخصم تلقائيًا.

## الإلغاء

```http
POST /customer/subscriptions/{subscriptionId}/cancellation-requests
Content-Type: application/json

{
  "reason": "سبب الإلغاء",
  "requestedEffectiveDate": "2026-08-20"
}
```

الاستجابة `202 Accepted`. هذا طلب مراجعة فقط: لا تغيّر واجهة الموبايل الاشتراك إلى ملغي ولا توقف التقويم حتى تتغير حالة الاشتراك من الباك. تكرار الطلب يرجع نفس `requestId` ولا ينشئ طلبًا مكررًا.

## حالات مهمة

- `404 subscription.not_found`: الاشتراك غير موجود أو لا يخص العميل الحالي.
- `409 subscription.freeze_request_already_pending`: يوجد طلب تجميد مفتوح.
- `409 subscription.renewal_not_available`: الحالة الحالية لا تسمح بالتجديد.
- `409 subscription.cancellation_not_available`: الحالة الحالية لا تسمح بطلب الإلغاء.
- لا تعرض مطعمًا جديدًا داخل الاشتراك؛ `restaurant` مرجع من Restaurant Pool المثبتة عند Checkout.
