# الأخطاء والـJSON الخاص والـpartial patch

## Headers والـserialization

Authorization: Bearer <customer access token>، Content-Type: application/json للـbody، Accept-Language: ar أو en. Success عبارة عن DTO مباشر أو array مباشر، وليس envelope عام. Enum أسماء PascalCase أصلية؛ لا تترجم enum/permission/action/status. DateOnly تاريخ YYYY-MM-DD، والتوقيت UTC بصيغة ISO-8601. طبّق لغة التطبيق على النصوص والـRTL؛ لا تغيّر KWD أو timezone الكويت بسبب لغة الجهاز.

## اختيار الشراء الصريح

PUT /api/v1/customer/family-subscription-flow/members/{memberId}/checkout-selection

```json
{"subscribeNow": true}
```

Response shape:

```json
{"batchId":"<UUID>","batchVersion":1,"memberId":"<UUID>","checkoutSelected":true,"selectedNewSubscriberCount":1}
```

false يؤجل الفرد دون مسح خطة التجهيز أو تفعيل الخدمة. الأرقام في المثال توضيحية؛ استخدم نسخة وعدد السيرفر.

## تجهيز الخطة partial patch

PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan

```json
{"programId":"<UUID>","bundleId":"<UUID>","tier":"<RestaurantTier من الكتالوج>","durationDays":0,"includeFriday":false,"startDate":"<YYYY-MM-DD>"}
```

durationDays يجب استبداله بالمدة المسموحة من السيرفر؛ 0 ليس طلبًا صالحًا. الحذف يعني الاحتفاظ، وnull يعني المسح. مثال تغيير تاريخ فقط:

```json
{"startDate":"<YYYY-MM-DD>"}
```

مثال مسح اختيار bundle:

```json
{"bundleId":null}
```

الاستجابة FamilyGroupScreenDto في ملف 02. هذه العملية للتجهيز؛ ليست ميزة تعديل برنامج اشتراك مدفوع.

## شكل ProblemDetails

```json
{"type":"<problem type URI>","title":"<localized title>","status":409,"detail":"<localized detail>","instance":"<request path>","code":"family.permissions_changed"}
```

metadata مثل currentVersion يظهر كحقل إضافي على نفس المستوى عند إرجاعه من العملية، وليس metadata envelope مضمونًا:

```json
{"status":409,"code":"family.delivery_version_changed","currentVersion":0,"title":"<localized title>","detail":"<localized detail>","instance":"<request path>"}
```

قد تستعمل بعض المسارات legacy error بدل code:

```json
{"error":"subscription.not_found"}
```

```json
{"error":"auto_renewal.price_changed","amount":0,"currency":"<currency from server>"}
```

تحمل code أو error بحسب المسار، ولا تعامل status HTTP وحده كمعنى الدومين. title/detail نصوص محلية وليست identifiers. عند تبديل اللغة أعد عرض رسالة التطبيق من code وparameters، ولا تستخدم diagnostics خامًا.

| الحالة | تعامل الموبايل |
|---|---|
| 401 | تجديد session وفق عقد auth، ثم إعادة آمنة؛ لا تكرر payment intent جديدًا |
| 403 أو invoice 404 | منع الوصول؛ لا كشف تفاصيل أو retry loop |
| 409 permissions/delivery version | إعادة القراءة ثم إعادة تطبيق نية المستخدم على النسخة الحالية |
| family.quote_changed | إعادة summary ومراجعة السعر قبل موافقة جديدة |
| family.idempotency_key_used | المفتاح استُخدم لنية مختلفة؛ لا تكرر نية معدلة بنفسه |
| family.client_update_required | منع إنشاء فاتورة جديدة وإظهار تحديث التطبيق |
| family.invoice_creation_stopped | إيقاف فواتير جديدة؛ متابعة invoice موجودة تبقى منفصلة |
| family.member_account_required | إكمال الربط بالحساب قبل اختيار شراء صالح |
| family.payment_method_unavailable / family.payment_currency_unsupported | إعادة method discovery؛ لا تحوّل العملة محليًا |
| family.provider_refund_required | لا تعرض refund completed؛ تحتاج معالجة مزود/عمليات |
| family.delivery_snapshot_review_required | حالة مراجعة؛ لا profile-address fallback |
| family.action_not_available | تعطيل القدرة وعدم تقديم نجاح محلي |
| 502 payment.initialization_failed | لا تفترض إنشاء/عدم إنشاء فاتورة؛ استعلم ثم أعد نفس النية بشكل آمن |

## موافقة auto renewal

PUT /api/v1/customer/subscriptions/{id}/auto-renewal/consent:

```json
{"accepted":true,"consentVersion":"<نسخة نص الموافقة>","amount":0,"currency":"<currency from paid subscription>"}
```

استبدل amount بمبلغ الاشتراك الحالي المثبت؛ 0 غير صالح. المبلغ يجب أن يطابق المصدر وأن تكون دقته حتى 3 منازل، currency ثلاث حروف. الاستجابة 202:

```json
{"subscriptionId":"<UUID>","consentActive":true,"consentVersion":"<version>","consentAmount":0,"currency":"<currency>","consentedAtUtc":"<ISO-8601 UTC>","billingStatus":"NotConnected","requiresPaymentSetup":true,"autoChargeEnabled":false}
```

GET auto-renewal:

```json
{"subscriptionId":"<UUID>","consentActive":false,"consentVersion":null,"consentAmount":null,"currency":null,"consentActorUserId":null,"withdrawnByUserId":null,"consentedAtUtc":null,"withdrawnAtUtc":null,"billingStatus":"NotConnected","requiresPaymentSetup":false,"autoChargeEnabled":false}
```

القيم قد تتغير حسب الموافقة؛ requiresPaymentSetup في GET يساوي consentActive. DELETE consent يعيد:

```json
{"subscriptionId":"<UUID>","consentActive":false,"withdrawnAtUtc":"<ISO-8601 UTC>","billingStatus":"NotConnected","autoChargeEnabled":false}
```

## خصائص computed في التجهيز

FamilyMemberCardDto يتضمن أيضًا selected/selectedCodes/hasNoAllergies/hasNoDietaryRestriction المشتقة من قوائم وتقدم إكمال التفضيلات؛ هي خصائص استجابة وليست إضافات مطلوبة للـrequest:

```json
{"selected":[],"selectedCodes":[],"hasNoAllergies":true,"hasNoDietaryRestriction":true}
```

FamilyMemberAllergiesDto يضيف selectedCodes وhasNoAllergies، وFamilyMemberDislikedFoodsDto يضيف eatsEverything، وFamilyMemberDietaryDto يضيف hasNoDietaryRestriction. قيمة true هنا توضح حالة completed مع قائمة فارغة، ولا تعني كل استجابة.

## التغييرات المطلوب تنفيذها في Flutter

1. أضف حقول explicit selection/batch version/contract version.
2. افصل roster عن subscribed population، واشتراك الحالي عن القادم.
3. افصل access pause عن service freeze.
4. أضف صلاحيات القراءة الذاتية والعائلية المستقلة وexpectedVersion، واعتمد capabilities لكل فعل.
5. افصل payment evidence/ReviewRequired عن service activation.
6. استخدم family checkout/renewal بدل generic individual resume.
7. أضف delivery amendments/version/affected/skipped بدل إعادة كتابة العنوان المدفوع محليًا.
8. لا تبنِ autoCharge من consent؛ الـprovider غير متصل في هذا العقد.
9. اعرض invoice scope والرفض 404 دون تسريب.
10. حدّث models للتقويم nullable/arrays/status/deadlines وحالات fulfillment غير الجاهزة.

العقد مبني على المصدر المعزول، ولم يُختبر ضد موبايل أو staging. ملف 01 يتضمن acceptance؛ املأ أدلة التنفيذ بعد الربط.
