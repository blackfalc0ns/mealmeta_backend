# زر إعادة محاولة الدفع — الاشتراك العائلي

## مكان الزر والقراءة

في صفحة الاشتراكات، اجلب الاشتراكات غير المكتملة أيضًا:

```http
GET /api/v1/customer/subscriptions/uncompleted
```

لا تعتمد على `subscriptions/current` وحده: الاشتراك غير المدفوع لا يظهر كاشتراك مفعّل. بطاقة العائلة تحمل `kind="FAMILY"` و`familyGroupId`. قيمة `subscriptionId` في هذه البطاقة هي معرف العائلة للتوافق، وليست معرف اشتراك فردي؛ لا ترسلها إلى `/subscriptions/{id}/resume`.

أضيفت إلى `actions`:

- `retryPaymentEndpoint`: مسار إعادة الدفع، أو `null` عندما لا توجد محاولة قابلة للإعادة.
- `pendingCheckout`: بيانات محاولة الدفع المعلقة الصالحة، أو `null` بعد انتهاء الحجز. تتضمن `paymentUrl` و`batchId` و`batchVersion` و`paymentAnchorSubscriptionId` و`holdExpiresAtUtc` و`finalPrice` و`familyGroupId`.

تتوفر نفس البيانات في `GET /api/v1/customer/family-subscription-flow/current` عبر `pendingCheckout` و`retryPaymentEndpoint`.

اعرض زر «إعادة محاولة الدفع» للبطاقة العائلية عندما يكون `actions.retryPaymentEndpoint` غير فارغ. مفتاح الترجمة المقترح للموبايل: `subscriptions.retryPayment`، بالعربية «إعادة محاولة الدفع» وبالإنجليزية `Retry payment`. أضف النصين إلى ملفات ترجمة Flutter الفعلية، واعرضهما حسب اللغة النشطة.

## حدث الضغط: إلى الدفع مباشرة

```http
POST /api/v1/customer/family-subscription-flow/retry-payment
Content-Type: application/json
Authorization: Bearer <current session token>
```

أرسل عقد الدفع الموجود بالفعل في المشروع، بنفس روابط العودة المعتمدة. لا تخترع روابط `callbackUrl` أو `errorUrl`، ولا تستخدم معرف العائلة كمعرف اشتراك فردي. مثال ربط يعتمد على إعدادات التطبيق الفعلية:

```dart
final response = await dio.post(
  subscription.actions.retryPaymentEndpoint,
  data: {
    'callbackUrl': paymentConfig.successUrl,
    'errorUrl': paymentConfig.errorUrl,
    'paymentMethodId': selectedPaymentMethodId,
    'clientContractVersion': supportedInvoiceContractVersion,
  },
);
await openPaymentWebView(response.data['paymentUrl']);
```

الأسماء `paymentConfig` و`openPaymentWebView` توضيحية؛ اربطها بخدمات الموبايل القائمة. لا تفتح شاشة إنشاء العائلة أو ترسل `POST .../group` من الزر. عطّل الزر أثناء الطلب، ولا ترسل ضغطات متزامنة.

## رد النجاح

الرد هو `FamilyCheckoutStartedDto`:

| الحقل | النوع | الاستخدام |
|---|---|---|
| `familyGroupId` | UUID | العائلة صاحبة المحاولة |
| `paymentAnchorSubscriptionId` | UUID | مرجع اشتراك الدفع للتحقق، وليس كل أفراد العائلة |
| `paymentUrl` | string | رابط البوابة الحقيقي؛ افتحه مباشرة |
| `holdExpiresAtUtc` | UTC timestamp | انتهاء حجز الفترة |
| `finalPrice` | number | إجمالي المحاولة المعادة بعد التحقق |
| `batchId` | UUID | دفعة الدفع |
| `batchVersion` | integer | إصدار الدفعة |

الباك يعيد استخدام المحاولة المعلقة ورابطها، دون فاتورة ثانية. إذا انتهت أو فشلت، يعيد استخدام الاختيارات الحالية الصريحة إن وجدت؛ وإلا يستعيد الأفراد المختارين في آخر محاولة منتهية/فاشلة فقط، ثم يعيد فحص البيانات والسعر والوجبات والتوفر قبل إنشاء محاولة جديدة. يحتفظ بالسجل السابق؛ لا يفعّل الاشتراك بمجرد إنشاء رابط الدفع.

## التعارض والاستكمال

إذا كان الدفع معلقًا، تظل تعديلات المجموعة والأفراد مقفلة. `409 family.checkout_pending` يحتوي رسالة عربية/إنجليزية واضحة وبيانات توجيه: `familyGroupId` و`resumeEndpoint` و`retryPaymentEndpoint` و`nextScreen="family_payment"`.

الاستكمال:

```http
GET /api/v1/customer/family-subscription-flow/current
```

ثم استخدم زر إعادة الدفع، بدل إنشاء المجموعة مرة ثانية. محاولات `ReviewRequired` لا تبدأ دفعًا جديدًا تلقائيًا. اعرض سبب الرفض عند فشل التوفر أو السعر أو بيانات الفرد، ولا تعتبر إنشاء/فتح رابط الدفع تأكيدًا للدفع. استخدم التحقق الحالي من البوابة عند العودة للتطبيق ثم أعد تحميل الاشتراكات.

تتم معالجة الحجوزات المنتهية قبل قراءة أو تعديل الفلو عبر خدمة الانتهاء القائمة، حتى لو كانت مهام الخلفية متوقفة. لا يتم فك قفل محاولة ما زالت صالحة.

## الاختبارات وحدود العمل

نجح 157 اختبارًا متعلقًا بفلو العائلة والاختيار وإعادة الدفع وانتهاء الحجوزات وصفحة الاشتراكات. تشمل إعادة الرابط نفسه، عدم إنشاء محاولة إضافية عند تكرار إعادة الدفع، الحفاظ على الأفراد المؤجلين، منع الوصول لعائلة مدير آخر، منع إعادة الدفع أثناء المراجعة، ورفض روابط عودة غير معتمدة قبل أي تغيير.

المستودع المتاح هو الباك فقط. هذه الوثيقة وعقد API جاهزان لربط الزر؛ إنشاء زر Flutter واختبار النقر وتبديل اللغة داخل الموبايل يتطلبان مشروع الموبايل.
