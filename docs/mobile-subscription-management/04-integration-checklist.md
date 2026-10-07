> Payment integration updated 2026-10-06: use the [Payzah contract](<F:/MealMate/backend/docs/mobile/payzah-payments.md>). Any old-provider payment endpoints, method IDs, credential/webhook fields or refund instructions below are superseded; they are retained only as historical context for this mixed guide.

# Checklist اختبار ربط إدارة الاشتراك

## 1. الإعداد

- [ ] Base URL يضاف من Environment/Flavor وليس ثابتًا داخل Repository.
- [ ] `Authorization: Bearer` يضاف من Interceptor.
- [ ] `Accept-Language` يرسل لغة التطبيق.
- [ ] 401 ينفذ Refresh Token مرة واحدة فقط.
- [ ] Logs لا تطبع access token أو بيانات الدفع أو سبب الإلغاء.

## 2. تحميل الشاشة

- [ ] Loading Skeleton ظاهر أثناء أول طلب.
- [ ] `currentSubscription` هو مصدر الكارت الرئيسي.
- [ ] `currentSubscription=null` يعرض Empty State بدون Crash.
- [ ] الاشتراكات السابقة تظهر من `subscriptions` عند الحاجة.
- [ ] القيم Nullable مثل `imageUrl`, `restaurant`, `pendingFreeze`, `invoiceNumber`, `maskedCardNumber` لا تسبب Crash.
- [ ] النسبة تعمل مع 0 و100 والقيم العشرية.
- [ ] التاريخ UTC للفواتير يتحول إلى Local Time.
- [ ] العملة تعرض من `currencyCode`.

## 3. الأزرار

- [ ] كل زر يعتمد على `actions.can*` فقط.
- [ ] الزر يظهر Loading مستقل أثناء الطلب.
- [ ] Double tap لا يرسل طلبين متزامنين.
- [ ] 409 يؤدي إلى رسالة واضحة وRefresh.
- [ ] أسباب التعطيل لها رسائل عربية مناسبة.

## 4. التجديد

- [ ] `POST renewal-requests` يرسل بدون Body.
- [ ] استجابة 202 تُقبل كنجاح.
- [ ] بيانات التجديد تفتح فلو الاشتراك الموجود.
- [ ] المطاعم والسعر يعاد جلبهما ولا يستخدم التطبيق Snapshot قديمة ككتالوج جديد.
- [ ] `suggestedStartDate` قيمة أولية قابلة للمراجعة وليست تأكيدًا نهائيًا.
- [ ] لا يتم فتح MyFatoorah إلا بعد Checkout جديد ناجح.

## 5. التجميد

- [ ] أقل تاريخ بداية يحترم `freezeStartDelayDays`.
- [ ] عدد الأيام لا يتجاوز `maximumFreezeDays`.
- [ ] الحساب شامل يوم البداية والنهاية.
- [ ] رسالة تمديد النهاية تظهر عندما `freezeExtendsSubscriptionEnd=true`.
- [ ] بعد 200 تظهر رسالة «قيد المراجعة»، وليس «تم التجميد».
- [ ] Refresh يظهر `pendingFreeze` ويعطل زر التجميد.
- [ ] خطأ validation من الباك يظهر بدون فقد اختيار المستخدم.

## 6. الإلغاء

- [ ] Confirmation واضح أن الإلغاء طلب مراجعة.
- [ ] استجابة 202 تعرض «تم إرسال الطلب».
- [ ] الاشتراك لا يُحذف ولا تتغير حالته محليًا.
- [ ] Calendar لا يتوقف من الموبايل بعد إرسال الطلب.
- [ ] Refresh يعطل الزر بسبب `cancellation_request_pending`.
- [ ] تكرار الطلب لا ينشئ واجهة مكررة أو رسائل متعارضة.

## 7. الفواتير

- [ ] تعرض `PAID` كمدفوع.
- [ ] تعرض `PAID_PENDING_FULFILLMENT` كمدفوع وجارٍ التجهيز.
- [ ] تعرض `REVIEW_REQUIRED` كقيد المراجعة.
- [ ] القائمة الفارغة تعرض Empty State صغير داخل قسم الفواتير.
- [ ] `maskedCardNumber=null` يخفي بيانات البطاقة.
- [ ] `canDownload=false` يخفي التحميل.
- [ ] لا يتم اختراع رقم بطاقة أو PDF.

## 8. الأمان والعزل

- [ ] تغيير `subscriptionId` إلى اشتراك عميل آخر يعيد 404 ولا يعرض بيانات.
- [ ] Customer token فقط هو المستخدم.
- [ ] Restaurant/Admin token لا يفتح الشاشة.
- [ ] لا يوجد `customerId` مرسل من التطبيق.
- [ ] لا توجد بيانات بطاقة في Cache أو Secure Storage أو Logs.

## 9. حالات الشبكة

- [ ] Timeout يعرض Retry.
- [ ] Offline يعرض آخر Cache ناجح مع علامة أن البيانات قديمة، إن كان Cache مستخدمًا.
- [ ] استعادة الشبكة لا تكرر Command سابقًا تلقائيًا دون معرفة نتيجته.
- [ ] 500 يعرض رسالة عامة ويحفظ `traceId` للمتابعة.
- [ ] JSON بحقل إضافي غير معروف لا يكسر Parser.
- [ ] Enum نصي أو رقمي لا يكسر Parser أثناء فترة الانتقال.

## 10. معايير القبول النهائية

- [ ] الشاشة تعمل بطلب تحميل رئيسي واحد.
- [ ] لا توجد حسابات Business Rules داخل Widgets.
- [ ] كل Command يتبعه Refresh من الباك.
- [ ] التجديد يمر على التسعير والأهلية والدفع من جديد.
- [ ] التجميد والإلغاء يظهران كطلبات مراجعة حتى قرار الباك.
- [ ] Restaurant Pool للاشتراك القديم لا تتغير عند إضافة مطعم جديد.
- [ ] تم اختبار العربية والإنجليزية وRTL/LTR.
- [ ] تم اختبار Android وiOS والعودة من MyFatoorah.

