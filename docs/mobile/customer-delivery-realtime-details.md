# ربط كارت حالة التوصيل وصفحة التفاصيل — تطبيق العميل

هذا الدليل يخص باك MealMate. تنفيذ شاشات الموبايل وربطها مسؤولية تطبيق العميل. وجود الكود محليًا لا يعني أنه منشور؛ تأكد من نسخة البيئة قبل الربط.

## 1. التدفق المطلوب من التصميم

1. الرئيسية تجلب بوكسات يوم العمل وتعرض كارتًا لكل بوكس متاح للمستخدم.
2. نص زر الكارت **«التفاصيل» / Details**؛ يفتح صفحة تفاصيل اليوم، حتى قبل تعيين المندوب.
3. الكارت والتفاصيل يستقبلان invalidation لحظيًا ثم يعيدان قراءة أحدث بيانات الخادم.
4. بعد التسليم تتحول التفاصيل لنفس صفحة النجاح في المرجع، باستخدام الحالة ووقت التسليم الحقيقيين.
5. لا يحتاج المستخدم تحديث الصفحة يدويًا. بعد انقطاع الشبكة أو العودة من الخلفية، التطبيق يعيد القراءة.

المعرّف الثابت هو `subscriptionDayId`. لا تستخدم `tripStopId` أو `orderId` كمفتاح وحيد للتنقل؛ قد يكونان null قبل الإسناد وتتغير المحاولة لاحقًا.

## 2. المصادقة واللغة

كل طلبات HTTP التالية تحتاج access token لعميل مصادق:

```http
Authorization: Bearer <customer-access-token>
Accept-Language: ar
X-Bilingual: true
```

استبدل `ar` بـ `en` حسب اللغة النشطة. أرسل `X-Bilingual: true` على طلبَي الكارت والتفاصيل حتى تبقى حقول `*Ar` و`*En` متاحة لتغيير اللغة محليًا. النظام العام قد يحذف حقل اللغة الأخرى عند غياب هذا الهيدر؛ وجود الحقل في DTO وحده لا يضمن وصوله عبر JSON.

اختر `deliveryStatusTextAr` أو `deliveryStatusTextEn` عند العرض، وكذلك `driver.nameAr/nameEn` و`vehicle.typeLabelAr/typeLabelEn`. الحقول المختصرة `deliveryStatusText` و`name` و`typeLabel` تعكس لغة الطلب وقت الجلب، فلا تخزنها كنص دائم عند تبديل اللغة.

ترجم نصوص الأزرار والتحميل والأخطاء والوحدات وعناوين الصفحة محليًا في ملفات ترجمة الموبايل بالعربي والإنجليزي. أسماء المندوبين بيانات أعمال ثنائية اللغة؛ لا تغير السجل الأصلي. استخدم اللغة الأخرى عند غياب الاسم المطلوب ثم رسالة محلية إن غاب الاثنان.

## 3. الرئيسية: قراءة الكروت

```http
GET /api/v1/customer/delivery-widget
GET /api/v1/customer/delivery-widget?serviceDate=YYYY-MM-DD
GET /api/v1/customer/delivery-widget?serviceDate=YYYY-MM-DD&subscriptionId=<subscription-guid>
```

المعاملان اختياريان. غياب `serviceDate` يعني يوم العمل الحالي بتوقيت `Asia/Kuwait` على الخادم، وليس التاريخ المحلي للجهاز.

النجاح HTTP 200 يعيد كائن الاستجابة مباشرة؛ لا تضف افتراضيًا غلاف `data` أو `success`:

```text
CustomerDeliveryWidgetResponse
  serviceDate: date "YYYY-MM-DD"
  serverTimeUtc: UTC timestamp
  refreshAfterSeconds: integer (حاليًا 30)
  items: CustomerDeliveryItem[]
```

`items: []` نتيجة سليمة تعني عدم وجود بوكسات مرئية لهذا التاريخ/الفلتر. ليست خطأ اتصال. يمكن وجود عدة بوكسات لاشتراكات فردية وعائلية؛ لا تعرض `items[0]` وحده.

## 4. زر التفاصيل: API مستقل عن رحلة المندوب

```http
GET /api/v1/customer/subscription-days/{subscriptionDayId}/delivery-details
```

استجابة النجاح المباشرة:

```text
CustomerDeliveryDetailsResponse
  serviceDate: date
  serverTimeUtc: UTC timestamp
  refreshAfterSeconds: integer
  delivery: CustomerDeliveryItem
```

`delivery` يستخدم نفس عقد عنصر الكارت، ومصدر الحالة واحد. الواجهة لا تحتاج دمج live-tracking القديم كي تفتح هذه الصفحة.

في كل عنصر يوجد رابطان مختلفان:

| الحقل | الاستخدام |
|---|---|
| `deliveryDetailsUrl` | رابط التفاصيل الجديد؛ مبني على يوم الاشتراك، ويعمل قبل إنشاء رحلة وبعد التسليم |
| `detailsUrl` | الرابط القديم إلى live-tracking؛ قد يكون null قبل وجود محاولة، واحتُفظ بمعناه للتوافق |

يمكنك استخدام الرابط الجديد بعد حله على نفس أصل API المعتمد، أو بناء المسار الثابت بمعرّف اليوم. لا تستخدم رابط API باعتباره route موبايل؛ افتح صفحة التطبيق ثم اجلب بياناتها.

## 5. عقد عنصر الكارت والتفاصيل

| الحقول | النوع والمعنى |
|---|---|
| `subscriptionId`, `subscriptionDayId` | GUIDs للاشتراك واليوم |
| `orderId`, `tripStopId` | GUID أو null؛ لا يشترط وجودهما لفتح التفاصيل |
| `orderCode` | رقم الطلب التجاري أو null؛ لا تعرض GUID بديلًا عنه |
| `deliveryStatus` | كود حالة ثابت مستقل عن اللغة؛ راجع الجدول التالي |
| `deliveryStatusTextAr`, `deliveryStatusTextEn` | نص الحالة باللغتين عند طلب X-Bilingual |
| `deliveryStatusText` | نص الحالة بلغة طلب HTTP |
| `isTerminal` | هل الحالة الحالية نهائية؛ لا تعني أن التسليم نجح |
| `isTrackable` | هل التتبع الحي متاح لهذه الحالة؛ لا تتحكم في السماح بفتح التفاصيل |
| `unavailableReasonCode` | سبب عدم توفر التتبع، أو null |
| `readyAtUtc` | وقت جاهزية حقيقي أو null |
| `pickedUpAtUtc` | وقت استلام المندوب من handover موثّق وغير ملغى، أو null |
| `arrivedAtUtc` | وقت تأكيد وصول المندوب، أو null |
| `deliveredAtUtc` | وقت التسليم الفعلي، أو null؛ لا تستبدله بوقت الجهاز |
| `driver` | كائن بيانات المندوب المسموح بها، أو null |
| `vehicle` | بيانات المركبة، أو null |
| `location` | إحداثيات موقع مع capturedAtUtc/receivedAtUtc/isLive، أو null |
| `etaMinutes` | عدد دقائق أو null؛ صفر موثّق للوصول يختلف عن غياب التقدير |
| `etaSource` | `road_route` أو `arrival` أو null |
| `destinationAddress` | عنوان التنفيذ المتاح أو null؛ ليس بديلًا من عنوان ملف العميل الحالي |

`driver` يحتوي: `id`, `nameAr`, `nameEn`, `name`, `maskedPhone`, `avatarUrl`, `rating`. الهاتف والصورة والتقييم قد تكون null. التقييم من متوسط السجلات الحقيقية؛ لا تضع قيمة الصورة المرجعية أو صفرًا مكان غياب التقييم. رقم الهاتف المقنّع ليس إذنًا للمكالمة المباشرة.

`vehicle` يحتوي: `typeCode`, `typeLabelAr`, `typeLabelEn`, `typeLabel`, `plateNumber`. حافظ على `typeCode` كمعرّف تقني واختر النص المترجم للعرض.

## 6. الحالات والمراحل

| `deliveryStatus` | ما تعرضه الواجهة |
|---|---|
| `NotStarted` | لم يبدأ التجهيز؛ لا مراحل نجاح مكتملة |
| `Preparing` | جارٍ التجهيز |
| `ReadyForPickup` | تم التجهيز؛ لا تعني أن العميل سيستلم من الفرع |
| `OutForDelivery` | خرج للتوصيل؛ يعرض الوقت المتوقع عندما يتوفر |
| `Delivered` | تم التسليم؛ تعرض صفحة النجاح ووقت التسليم الحقيقي المتاح |
| `Failed` | تعذر التسليم؛ لا تكمل المسار الأخضر |
| `ReturnedToKitchen` | أُعيد للمطعم |
| `Frozen` | اليوم مجمّد |
| `Cancelled` | ملغى |
| `tracking_unavailable` | التتبع غير واضح؛ لا تختَر مندوبًا أو محاولة عشوائيًا |

الشريط الأساسي في المرجع: **تم التجهيز ← خرج للتوصيل ← تم التسليم**. حدّد إكمال كل مرحلة صراحة من الحالة والأدلة، وليس بمقارنة ترتيب enum عددي. لا تختلق أوقات مراحل مفقودة، حتى لو الحالة الحالية `Delivered`.

إذا كان `deliveredAtUtc` متاحًا، اعرض «تم التسليم {date} الساعة {time}». إذا غاب، اعرض نجاح التسليم دون ساعة. لا تعرض وقت وصول متوقع تحت رسالة تسليم مكتمل.

الحالات النهائية لا تعرض موقعًا حيًا أو ETA. قد تبقى هوية المندوب الموثّق. انتهاء عداد ETA لا يغيّر الحالة إلى Delivered؛ يصل قرار التسليم من الخادم.

## 7. الاتصال اللحظي SignalR

```text
Hub URL: <API_ORIGIN>/hubs/customer-delivery
Event: customer-delivery-changed
```

استخدم عميل **SignalR متوافقًا مع ASP.NET Core**؛ الاتصال WebSocket الخام وحده لا ينفذ handshake/framing الخاص بـ SignalR.

زوّد access token عبر آلية `accessTokenFactory`/نظيرها في مكتبة الموبايل، مع تجديده من جلسة المستخدم الحالية. لا ترسل customerId في query ولا تحاول الانضمام لمجموعة تختار اسمها؛ الخادم يحدد المجموعة من هوية العميل المصادق.

الحدث كائن JSON واحد:

```text
{
  "eventId": GUID,
  "subscriptionDayId": GUID,
  "occurredAtUtc": UTC timestamp,
  "schemaVersion": 1
}
```

الأشكال `GUID` و`UTC timestamp` أعلاه توصيف للأنواع وليست JSON literal يرسل للخادم. لا يحتوي الحدث نص حالة أو اسمًا أو عنوانًا أو موقعًا. هو طلب لإعادة القراءة، وليس snapshot.

عند وصوله:

1. تجاهل نسخة schema غير المدعومة وسجل تشخيصًا محليًا دون بيانات حساسة.
2. امنع معالجة eventId المكرر، بذاكرة محدودة.
3. أعد قراءة كروت اليوم/الفلتر المعروض إن كان الحدث مؤثرًا عليها، وتفاصيل اليوم إن كانت مفتوحة.
4. حدّث المصدر المشترك الذي تقرأ منه الشاشتان؛ لا تحتفظ بنسختين مستقلتين للحالة.
5. قد يتأخر حدث عن حدث آخر؛ اقرأ أحدث REST snapshot ولا تطبّق ترتيب حالات من `occurredAtUtc`.

لا يرسل تطبيق الموبايل أحداث تغيير حالة أو إشعارات تسليم. مصدرها أوامر التشغيل الفعلية، وبعد الحفظ فقط.

## 8. البداية والانقطاع والتزامن

```text
open visible screen:
  register event callback
  start/reuse authenticated SignalR connection
  load current REST snapshot
  after initial connection completes, refetch if a load raced with connection
  schedule visible-screen reconciliation using refreshAfterSeconds

delivery event:
  mark relevant resources dirty
  if no request in progress: refetch
  if request already in progress: finish it, then refetch once more

resume / reconnect:
  refresh token if required
  reconnect
  refetch visible resources, even if no event arrives

logout:
  stop connection and timers
  invalidate all request generations
  clear delivery snapshots and cached user data
```

استخدم single-flight لكل مورد وrequest generation لمنع رد قديم من استبدال رد أحدث، أو رد حساب سابق من الظهور بعد logout. cache key يشمل المستخدم وتاريخ الخدمة ومعرف اليوم.

احتفظ بآخر بيانات عند الانقطاع، مع رسالة مترجمة تفيد أنها لا تتحدث حاليًا. retry يتدرج مع backoff؛ لا تعِد طلب أمر تسليم بسبب فقد تحديث.

استمر في reconciliation وفق `refreshAfterSeconds` عندما تكون الشاشة مرئية، حتى مع اتصال سليم. أحداث هذه الميزة للحالة والتعيين وأدلة التسليم؛ تحرك الموقع وحساب ETA لا يمثلان بث خريطة مستمرًا في هذا العقد.

لا يوجد replay للأحداث للجهاز غير المتصل. الباك يستخدم outbox مع قناة PostgreSQL مشتركة بين Worker ونسخ API، وSignalR لكل الأجهزة المتصلة؛ بعد انقطاع API listener أو الجهاز يعالج refetch/polling الفجوة. لا تعرض مجرد اتصال Hub كدليل على حداثة الموقع.

عند التشغيل على عدة نسخ خلف load balancer، راجع إعدادات تثبيت جلسة SignalR أو النقل WebSockets-only مع skip negotiation حسب دعم مكتبة الموبايل. قناة PostgreSQL توزع invalidation بين نسخ API، ولا تغيّر متطلبات توجيه اتصال SignalR نفسه.

## 9. الأخطاء والأمان

| النتيجة | المطلوب من الموبايل |
|---|---|
| 401 | استخدم تدفق تجديد الجلسة، ولا تستمر برمز قديم بلا نهاية |
| 403 | نوع مستخدم/صلاحية غير مناسبة؛ لا تعِد المحاولة كخطأ شبكة |
| تفاصيل 404 | اليوم غير موجود أو غير مسموح؛ احذف بياناته الحساسة المحفوظة واعرض رسالة محلية مناسبة |
| widget filter غير مسموح | اتبع كود ProblemDetails الحالي؛ لا تعتبره items فارغة |
| timeout / 5xx / offline | آخر snapshot إن وجد مع retry؛ لا توهم المستخدم بتحديث ناجح |
| HTTP 200 + items=[] | empty state سليم |

الأخطاء تستخدم ProblemDetails و`code` عندما توفره استجابة المشروع. لا تعرض stack trace أو raw server diagnostics كرسالة منتج. صلاحيات عضو العائلة قد تتغير أثناء فتح الشاشة؛ إعادة القراءة تعيد التحقق.

## 10. الأزرار وحدود هذه الميزة

- **التفاصيل:** route الموبايل بمعرف اليوم ثم GET التفاصيل أعلاه.
- **العودة للرئيسية:** تنقل محلي، دون عملية كتابة في الباك.
- **تحتاج مساعدة؟ / الإبلاغ عن مشكلة:** اربطهما بتدفق الدعم الفعلي المعتمد. هذا التغيير لا يضيف endpoint كتابة شكوى.
- **تقييم التجربة:** التقييم المعروض للمندوب قراءة بيانات فعلية. هذا التغيير لا ينشئ endpoint لإرسال تقييم جديد ولا يحدد هل المقصود تقييم وجبات أو مندوب. أظهر الإجراء بعد التسليم فقط إذا كان تدفق التقييم الحقيقي متاحًا ومصرحًا به.
- **الخريطة والمكالمات:** ليستا مطلوبتين لإكمال صفحة التفاصيل في هذا العقد؛ لا توسّع صلاحيات الهاتف أو تتبع العميل اعتمادًا على المرجع.

## 11. قائمة قبول ربط الموبايل

- [ ] الكروت تعرض كل البوكسات المصرح بها؛ كل زر يفتح اليوم الصحيح.
- [ ] التفاصيل تفتح قبل TripStop وبعد التسليم، ولا تعتمد على isTrackable.
- [ ] الشاشة المفتوحة تتحول من OutForDelivery إلى Delivered بعد إعادة قراءة الحدث.
- [ ] جهازان لنفس العميل يتحدثان؛ عميل آخر لا يرى بيانات أو أحداث يوم ليس له.
- [ ] إعادة الاتصال وresume يعيدان القراءة؛ event مكرر أو REST response قديم لا يرجع الحالة للخلف.
- [ ] الفشل/الإرجاع/الإلغاء/التجميد لا تعرض نجاح تسليم.
- [ ] لا مندوب/صورة/تقييم/ETA بديل ثابت؛ ETA=null يختلف عن arrival=0.
- [ ] ar → en → ar يغيّر الحالة والاسم والأزرار والتواريخ أثناء فتح الشاشتين ورسالة انقطاع.
- [ ] X-Bilingual:true موجود على القراءتين؛ RTL/LTR ودعم تكبير الخط وsemantics سليمة.
- [ ] logout يمسح البيانات ويوقف الاشتراكات؛ سحب صلاحية العائلة يمنع استمرار عرض بيانات مرفوضة.
- [ ] لا يُدعى أن النسخة منشورة أو أن اختبارات الموبايل نجحت استنادًا إلى اختبارات الباك وحدها.

## مراجع النقل المستخدم في الباك

- [Npgsql: Waiting for Notifications](https://www.npgsql.org/doc/wait.html).
- [PostgreSQL: NOTIFY](https://www.postgresql.org/docs/current/sql-notify.html).
