# حالات الشاشة والأزرار

## 1. مصدر الحقيقة

عند فتح الشاشة نفذ طلبًا واحدًا:

```http
GET /api/v1/customer/subscriptions/management?includeHistory=true
```

لا تختر الاشتراك الحالي في الموبايل. الباك يعيد `currentSubscription` بعد تطبيق أولوية الحالات.

## 2. حالات تحميل الشاشة

### Loading

- اعرض Skeleton للكارت الرئيسي وقائمة الفواتير.
- لا تعرض أزرار الإجراءات قبل وصول `actions`.
- امنع الضغط المتكرر أثناء التحميل.

### Loaded مع اشتراك جارٍ

الشرط:

```dart
state.data.currentSubscription != null
```

ارسم:

- الصورة من `imageUrl` مع Placeholder عند `null`.
- اسم الباقة من `bundleNameAr` أو `bundleNameEn` حسب اللغة.
- اسم البرنامج من `programNameAr` أو `programNameEn`.
- عدد الوجبات من `mealsPerDay`.
- تاريخ النهاية من `endDate`.
- الأيام المتبقية من `remainingServiceDays`.
- شريط النسبة من `progressPercentage / 100` بعد Clamp بين 0 و1.
- المبلغ من `paidAmount` مع `currencyCode` باستخدام NumberFormat، وليس String concatenation ثابتة.
- المطعم من `restaurant` عند توفره.

### Loaded بدون اشتراك جارٍ

الشرط:

```dart
currentSubscription == null && emptyStateCode == 'no_current_subscription'
```

اعرض:

- Empty State «لا يوجد اشتراك حالي».
- زر «اشترك الآن» يفتح فلو الاشتراك من البداية.
- قسم «اشتراكات سابقة» إذا كانت `subscriptions` غير فارغة.

لا تعتبر وجود عنصر `Expired` أو `Completed` اشتراكًا جاريًا.

### Error

- احتفظ بآخر بيانات ناجحة إن وجدت.
- اعرض Snackbar أو Inline Retry.
- عند 401 نفذ Refresh Token مرة واحدة فقط.
- عند 500 اعرض رسالة عامة وسجل `traceId` في Crash/Logging service.

## 3. حالات الاشتراك

| `status` | العرض المقترح | هل هو Current؟ | ملاحظة |
|---|---|---:|---|
| `Draft` | مسودة | لا | لا يفترض ظهوره كاشتراك نشط. |
| `PendingPayment` | بانتظار الدفع | نعم | يمكن توجيه العميل لاستكمال الدفع إذا كان رابط الدفع ما زال صالحًا في فلو Checkout. |
| `Paid` | تم الدفع وجارٍ التجهيز | نعم | لا تفتح Calendar قبل اكتمال Fulfillment. |
| `Scheduled` | سيبدأ قريبًا | نعم | اعرض `startDate`. |
| `Active` | نشط | نعم | الحالة الأساسية للشاشة. |
| `Frozen` | مجمد | نعم | اعرض تنبيه التجميد. |
| `Completed` | مكتمل | لا | يظهر في التاريخ ويمكن تجديده. |
| `Cancelled` | ملغي | لا | لا تعرض أزرار إدارة. |
| `CancellationPending` | إلغاء قيد المراجعة | لا حاليًا | حافظ على Parser للقيمة. |
| `Upgraded` | تمت ترقيته | لا | استخدم الاشتراك الجديد إن وجد في القائمة. |
| `Expired` | منتهي | لا | يظهر في التاريخ ويمكن تجديده. |
| `SuspendedForReview` | معلق للمراجعة | نعم | اعرض رسالة دعم ولا تفترض فشل الدفع. |
| `PaymentFailed` | فشل الدفع | لا | افتح Checkout جديدًا بدل اعتبار الاشتراك فعالًا. |

يفضل عرض `statusNameAr` في العربية، مع Mapping محلي احتياطي إذا كانت القيمة فارغة.

## 4. زر التجديد

مصدر القرار:

```text
actions.canRenew
actions.renewUnavailableReason
actions.renewalEndpoint
```

عند `canRenew=true`:

1. ضع الزر في Loading مستقل.
2. نفذ `POST actions.renewalEndpoint` بدون Body.
3. خزّن `requestId` للـanalytics فقط؛ لا تحتاجه الشاشة التالية حاليًا.
4. مرر `programId`, `bundleId`, `tier`, `durationDays`, `includeFriday`, `regionId`, `suggestedStartDate` إلى Subscription Flow.
5. أعد جلب Configuration والمطاعم والسعر من الباك.
6. لا تستخدم `paidAmount` القديم كسعر التجديد.

عند `canRenew=false`:

- عطّل الزر أو أخفه حسب التصميم.
- لا تحاول استنتاج السماح من `status` محليًا.

## 5. زر التجميد

مصدر القرار والسياسة:

```text
actions.canFreeze
actions.freezeUnavailableReason
actions.freezeStartDelayDays
actions.maximumFreezeDays
actions.freezeExtendsSubscriptionEnd
actions.freezeEndpoint
```

حساب الحدود لواجهة اختيار التاريخ:

```text
minimumStartDate = today + freezeStartDelayDays
maximumEndDate = selectedStartDate + maximumFreezeDays - 1
```

هذه الحسابات لتحسين UX فقط. الباك يعيد التحقق من القواعد، وهو مصدر الحقيقة النهائي.

إذا `freezeExtendsSubscriptionEnd=true` اعرض قبل التأكيد:

> سيتم تمديد نهاية الاشتراك بعدد أيام التجميد بعد موافقة الإدارة.

بعد الإرسال الناجح:

- لا تغير `status` إلى `Frozen` محليًا.
- اعرض «طلب التجميد قيد المراجعة».
- نفذ Refresh.
- استخدم `pendingFreeze` لعرض الفترة المطلوبة.

شكل `pendingFreeze`:

```json
{
  "requestId": "865039ea-13d0-4381-ab29-1672a9ff0470",
  "startDate": "2026-08-20",
  "endDate": "2026-08-26",
  "freezeDays": 7,
  "status": "Submitted"
}
```

## 6. زر الإلغاء

مصدر القرار:

```text
actions.canCancel
actions.cancelUnavailableReason
actions.cancellationEndpoint
```

UX المقترح:

1. افتح Confirmation Sheet.
2. وضح أن الطلب يخضع للمراجعة.
3. اطلب السبب، والتاريخ الفعلي إن احتاج التصميم.
4. بعد `202` اعرض «تم إرسال طلب الإلغاء للمراجعة».
5. لا تمسح الاشتراك من الواجهة.
6. لا توقف Calendar أو Notifications محليًا.
7. نفذ Refresh ليصبح الزر غير متاح.

السبب `cancellation_request_pending` يعني أن الطلب مسجل مسبقًا.

## 7. الفواتير وبيانات البطاقة

ارسم كل فاتورة باستخدام:

- الرقم: `invoiceNumber ?? paymentId ?? '-'`.
- التاريخ: تحويل `issuedAtUtc` إلى التوقيت المحلي قبل العرض.
- القيمة: `amount + currencyCode` من Formatter.
- الحالة: Mapping من `status`.
- البوابة: `gateway`.

زر التفاصيل يمكن أن يعرض Bottom Sheet من نفس البيانات، أو ينفذ `GET /{id}/invoices` عند الحاجة إلى Refresh.

قواعد البطاقة:

- إذا `maskedCardNumber == null` اخفِ صف البطاقة بالكامل.
- لا تعرض رقمًا افتراضيًا مثل `•••• 4582`؛ هذا سيكون بيانًا غير حقيقي.
- لا تحفظ CVV أو PAN أو Expiry داخل التطبيق أو ترسلها إلى MealMate API.

قواعد التحميل:

- إذا `canDownload=false` اخفِ السهم/زر التحميل.
- يمكن عرض `downloadUnavailableReason` داخليًا للـdebug فقط.

## 8. أسباب تعطيل الأزرار

| الكود | الرسالة المقترحة |
|---|---|
| `renewal_not_available` | التجديد غير متاح لحالة الاشتراك الحالية. |
| `freeze_not_available_for_status` | لا يمكن تجميد الاشتراك في حالته الحالية. |
| `freeze_request_pending` | يوجد طلب تجميد قيد المراجعة. |
| `cancellation_not_available_for_status` | لا يمكن طلب الإلغاء في الحالة الحالية. |
| `cancellation_request_pending` | يوجد طلب إلغاء قيد المراجعة. |

اجعل Mapping الأكواد في طبقة Presentation، واترك القيمة الأصلية متاحة للـlogging.

## 9. Refresh وConcurrency

- استخدم Pull-to-refresh للشاشة.
- بعد كل Command ناجح أعد تحميل تفاصيل الاشتراك.
- امنع Double tap لكل زر.
- طلبا التجديد والإلغاء Idempotent، لكن ذلك لا يغني عن تعطيل الزر أثناء الطلب.
- إذا عاد 409 بعد عرض الزر متاحًا، فمعناه أن الحالة تغيرت على جهاز آخر؛ نفذ Refresh واعرض الرسالة.
- لا تنفذ Polling دائمًا. استخدم Refresh عند العودة من شاشة الدفع أو عند استئناف التطبيق فقط.

## 10. Analytics مقترحة

بدون إرسال معلومات حساسة:

```text
subscription_management_opened
subscription_renew_clicked
subscription_renew_request_created
subscription_freeze_clicked
subscription_freeze_submitted
subscription_cancel_clicked
subscription_cancel_submitted
subscription_invoice_opened
subscription_management_error
```

أرسل `subscriptionId`, `status`, `errorCode` فقط عند الحاجة، ولا ترسل سبب الإلغاء أو بيانات الدفع.

