# استكمال العائلة وإعادة محاولة الدفع — دليل Flutter

عنوان API: `https://api.trymealmate.com`

هذا الدليل يشرح مكان المسارين في التطبيق والمدخلات والردود. أمثلة JSON التالية توضيحية ومختصرة؛ القيم بين `< >` يجب استبدالها بالقيم المعادة من API أو إعدادات التطبيق، وليست بيانات فعلية.

## الترويسات

المساران يتطلبان حساب عميل مسجلًا وتوكنًا صادرًا من السيرفر الجديد:

```http
Authorization: Bearer <accessToken>
Accept-Language: ar
```

للطلب POST أضف `Content-Type: application/json`. استخدم `en` لطلب اللغة الإنجليزية. لا تكرر `/api/v1` عند جمع baseUrl والمسار.

## أين يظهر زر الاستكمال؟

في صفحة الاشتراكات، اقرأ:

```http
GET /api/v1/customer/subscriptions/uncompleted
```

اعرض عناصر `kind = FAMILY` مع حالة المسودة أو الدفع المعلق. لا تعرض الدفع المعلق كاشتراك نشط. مقطع من بطاقة العائلة:

```json
{
  "kind": "FAMILY",
  "subscriptionId": "<familyGroupId>",
  "familyGroupId": "<familyGroupId>",
  "status": "CheckoutPending",
  "actions": {
    "canResume": true,
    "resumeEndpoint": "/api/v1/customer/family-subscription-flow/current",
    "nextScreen": "family_payment",
    "retryPaymentEndpoint": "/api/v1/customer/family-subscription-flow/retry-payment"
  }
}
```

في بطاقة FAMILY، `subscriptionId` هنا يمثل مجموعة العائلة. لا تمرره إلى `/customer/subscriptions/{id}/resume`. استخدم `actions.resumeEndpoint` عند الضغط على زر الاستكمال، واحترم `canResume`.

## GET current — تحميل العائلة الحالية

```http
GET https://api.trymealmate.com/api/v1/customer/family-subscription-flow/current
```

لا يحتاج body أو query أو familyGroupId؛ السيرفر يحدد مجموعة العائلة من الحساب المصادق عليه.

مثال مختصر لمسودة:

```json
{
  "familyGroupId": "<familyGroupId>",
  "name": "<اسم العائلة المحفوظ>",
  "status": "Draft",
  "members": [],
  "currentStep": 3,
  "sessionExpired": false,
  "pendingCheckout": null,
  "retryPaymentEndpoint": null
}
```

`members` في الرد الفعلي يحتوي الأعضاء المحفوظين. استكمل شاشة الإعداد وفق `currentStep` وبياناتهم؛ لا تعتمد على رقم الخطوة في المثال.

مثال مختصر لدفع معلق:

```json
{
  "familyGroupId": "<familyGroupId>",
  "status": "CheckoutPending",
  "currentStep": 4,
  "sessionExpired": false,
  "pendingCheckout": {
    "familyGroupId": "<familyGroupId>",
    "paymentAnchorSubscriptionId": "<subscriptionId>",
    "paymentUrl": "<gatewayPaymentUrl>",
    "holdExpiresAtUtc": "<UTC timestamp>",
    "finalPrice": 77.28,
    "batchId": "<batchId>",
    "batchVersion": 19
  },
  "retryPaymentEndpoint": "/api/v1/customer/family-subscription-flow/retry-payment"
}
```

اعرض شاشة دفع العائلة مع السعر ووقت انتهاء الحجز. مجرد تحميل current لا ينشئ دفعًا. افحص `pendingCheckout` و`retryPaymentEndpoint` فعليًا؛ قد يغيب رابط الدفع أثناء التهيئة أو بعد انتهاء الحجز.

## POST retry-payment — زر استكمال الدفع

مكانه داخل شاشة دفع العائلة عندما يعيد السيرفر `retryPaymentEndpoint`. لا تستخدمه لإنشاء أول عملية شراء لمسودة جديدة؛ أول شراء يستخدم `POST /family-subscription-flow/checkout` بعد الملخص.

```http
POST https://api.trymealmate.com/api/v1/customer/family-subscription-flow/retry-payment
```

Body:

```json
{
  "callbackUrl": "<approved-app-success-url>",
  "errorUrl": "<approved-app-error-url>",
  "paymentMethodId": 3,
  "clientContractVersion": 1
}
```

| الحقل | الاستخدام |
| --- | --- |
| callbackUrl | مطلوب؛ رابط العودة للعميل عند النجاح من إعدادات التطبيق المعتمدة. |
| errorUrl | مطلوب؛ رابط العودة للعميل عند الخطأ من إعدادات التطبيق المعتمدة. |
| paymentMethodId | اختياري، القيمة الافتراضية 3؛ استخدم وسيلة الدفع التي اختارها المستخدم من API. |
| clientContractVersion | اختياري في DTO؛ أرسل الإصدار المدعوم من العميل، 1 في العقد الحالي. إنشاء فاتورة جديدة يخضع لبوابة إصدار العقد. |
| correlationId | اختياري للتتبع. |
| batchId / batchVersion / idempotencyKey | اختيارية؛ خدمة retry تحدد الحجز المفتوح ومفتاحه وإصداره من قاعدة البيانات وتستخدمها بدل قيم العميل. |

لا يحتاج الطلب familyGroupId أو معرف الاشتراك الفردي. callbackUrl الخاص بالتطبيق يختلف عن عنوان عودة Payzah إلى السيرفر. السيرفر يولد عنوانه من `Payments__PublicBaseUrl`:

```text
https://api.trymealmate.com/api/v1/payments/payzah/return
```

لا تستخدم هذا العنوان تلقائيًا كـ callbackUrl للعميل، ولا تخترع رابطًا غير مسموح به.

مثال الرد:

```json
{
  "familyGroupId": "<familyGroupId>",
  "paymentAnchorSubscriptionId": "<subscriptionId>",
  "paymentUrl": "<gatewayPaymentUrl>",
  "holdExpiresAtUtc": "<UTC timestamp>",
  "finalPrice": 77.28,
  "batchId": "<batchId>",
  "batchVersion": 19
}
```

افتح `paymentUrl` المعاد من السيرفر. عند الرجوع أعد تحميل current وصفحة الاشتراكات؛ فتح الرابط أو رجوع المتصفح لا يثبت التحصيل.

عندما يكون الحجز PaymentPending صالحًا، تعاد نفس محاولة الدفع. بعد انتهاء محاولة سابقة يمكن أن ينشئ السيرفر batch جديدًا بعد التحقق من الشروط. حالة ReviewRequired تمنع تهيئة دفع جديد تلقائيًا.

## ترتيب التكامل

```text
صفحة الاشتراكات -> GET uncompleted
بطاقة FAMILY -> زر استكمال -> GET actions.resumeEndpoint
Draft -> شاشة إعداد العائلة
CheckoutPending -> شاشة دفع العائلة
زر استكمال الدفع -> POST retryPaymentEndpoint -> فتح paymentUrl
الرجوع من الدفع -> إعادة تحميل الحالة من API
```

## معالجة الأخطاء

| الرمز | تصرف التطبيق |
| --- | --- |
| subscription.not_found في المسار الفردي | تحقق أن عنصر FAMILY لم يرسل إلى resume الفردي. |
| family.not_found | أعد تحميل القائمة؛ لا تستخدم بيانات عائلة حساب آخر. |
| checkout.redirect_url_not_allowed | راجع callbackUrl وerrorUrl وإعدادات روابط العودة. |
| family.batch_review_required | اعرض حالة مراجعة الدفع؛ امنع إعادة الدفع التلقائي. |
| family.quote_changed | أعد قراءة الملخص والحالة. |

## تحقق Flutter

- ظهور FAMILY Draft وCheckoutPending في صفحة الاشتراكات دون تكرار البطاقة.
- عدم إرسال familyGroupId إلى مسار resume الفردي.
- استخدام actions وpendingCheckout من الرد بدل المسارات المركبة.
- منع الضغط المكرر أثناء طلب الدفع، وعرض الخطأ دون إعلان نجاح.
- تحديث الحالة بعد الرجوع من البوابة.
- ترجمة النصوص في موارد التطبيق العربية والإنجليزية واختبار RTL وتغيير اللغة.

المراجع: `CustomerFamilySubscriptionFlowController`, `FamilyPaymentRetryService`, `FamilyStartCheckoutRequest`, `FamilyGroupScreenDto`, `CustomerUncompletedSubscriptionDto`.
