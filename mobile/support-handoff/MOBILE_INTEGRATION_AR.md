# دليل تكامل الدعم للجوال

هذا الدليل ملخص تنفيذي لعقد الدعم الموجود في هذا المستودع. راجع [العقد الكامل](../UNIFIED_SUPPORT_API_CONTRACT.md) وملف الدور المناسب قبل التنفيذ. جميع المسارات أدناه نسبية إلى `/api/v1` وتتطلب Bearer session. الخادم وحده يحدد المستخدم والدور والمطعم والصلاحيات والعضوية؛ لا ترسل معرفات العميل كدليل تفويض.

## مصفوفة المسارات حسب الدور

| الدور | القراءة والكتابة | الإشعارات والملفات |
|---|---|---|
| العميل | `GET /support/cases`; `POST /support/cases`؛ `GET /support/cases/{caseId}`؛ `POST /support/cases/{caseId}/messages`؛ `POST /support/cases/{caseId}/actions/{operation}` للإغلاق أو إعادة الفتح حسب الحالة | `GET /customers/me/notifications`؛ `PUT /customers/me/notifications/{id}/read` أو `/read-all`؛ `PUT/DELETE /customers/me/device-tokens`؛ رفع المرفق وقراءته عبر المسارات المشتركة أدناه |
| السائق | الحالات العامة الخاصة به عبر `GET /support/cases` و`GET /support/cases/{caseId}`؛ إنشاء سؤال عام عبر `POST /support/cases`؛ الرد عبر `/messages`. بلاغ الرحلة يظل `POST /driver/orders/{boxId}/report-issue`، وقائمة البلاغات القديمة `GET /driver/support/issues` | `GET /driver/notifications`؛ `PUT /driver/notifications/{id}/read`؛ `POST/DELETE /driver/device-token`؛ البلاغات التشغيلية من `GET /driver/support/issues` وقراءة الدليل من endpoint الخاص بالبلاغ |
| مسؤول التوصيل | الحالات التي أنشأها أو المهام النشطة ضمن نطاق عضويته عبر `GET /support/cases`; التفاصيل `GET /support/cases/{caseId}`؛ رد المهمة باستخدام `POST /support/cases/{caseId}/messages` مع `taskId`؛ إكمالها عبر `/actions/complete-party-task` | `GET /delivery-manager/notifications`؛ `PUT /delivery-manager/notifications/{id}/read`؛ `POST/DELETE /delivery-manager/device-token`؛ الأدلة الخاصة بالمهمة تقرأ من `readPath` فقط إذا أعاده API للمستلم الحالي |

`SupportCasesController` هو مصدر مسارات الحالات الفعلي. توجد كذلك مسارات إدارة/نقل الإسناد والمهام، لكنها إدارية ولا تدخل في واجهة العميل أو السائق أو مسؤول التوصيل. نطاق النتائج يتغير حسب الدور؛ لا تعرض سجلاً لمجرد معرفة `caseId`.

مسار محدد اختيار الطلبات المسلّمة للعميل هو `GET /api/v1/support/delivered-boxes?pageNumber=1&pageSize=20` (العميل فقط)، مع مرشحات اختيارية `fromDate`, `toDate` بصيغة `YYYY-MM-DD` و`subscriptionId`. الصفحة تعيد `{items,totalCount,pageNumber,pageSize}`؛ حجم الصفحة من 1 إلى 100. العنصر يحتوي `subscriptionId`, `subscriptionDayId`, `restaurantDailyOrderId?`, `serviceDate`, `restaurantId?`, `restaurantNameAr?`, `restaurantNameEn?`, `boxCount?`, `orderCode?`, `deliveredAtUtc?`, `relatedResourceType`, `relatedResourceId`. علامة `?` تعني أن القيمة قد تكون `null`. الأنواع المرتبطة هي `RestaurantDailyOrder` للطلب اليومي المسلّم و`SubscriptionDay` ليوم قديم مسلّم بلا أي طلب يومي. القائمة تستخدم تاريخ خدمة يوم الاشتراك، مرتبة تنازلياً ثم بمعرف المورد، و`totalCount` يعد العناصر. قد يعيد التاريخ الواحد عدة طلبات؛ جمّعها في الواجهة حسب `serviceDate`. يقتصر المسار على الطلبات اليومية ذات الحالة `Delivered` والأيام القديمة ذات الحالة `Delivered` التي لا تملك أي طلب يومي؛ لا تستنتج وقت تسليم عند `deliveredAtUtc: null`. هذا مرشح عرض فقط ولا يغير قواعد إنشاء الشكوى أو يمنع بلاغات التأخير وعدم الاستلام. مسار `GET /customer/delivery-widget` يبقى بيانات عامة لبطاقة التوصيل وليس قائمة مؤهلة للشكوى.

## الطلبات والاستجابات

إنشاء الحالة: `POST /support/cases` بجسم `{channel, kind, category?, subject, description, idempotencyKey, relatedResourceType?, relatedResourceId?}`. القيم المعروفة لـ `channel`: `Customer`, `Driver`, `Restaurant`, `DeliveryManager`. والقيم المعروفة لـ `kind`: `Complaint`, `GeneralRequest`, `OperationalReport`. البلاغ التشغيلي للسائق يستخدم endpoint البلاغ القديم، وليس إنشاء حالة عامة. الرد: كائن الحالة، ويتضمن `id`, `status`, `version`, `messages`, `tasks`, `links`, و`activeTimingCycle` عند توفر دورة توقيت.

القائمة هي صفحة `{items, pageNumber, pageSize, totalCount}` عبر `GET /support/cases?pageNumber=1&pageSize=20` ويمكن تمرير `status`, `kind`, `category`. التفاصيل ترجع إسقاط الحالة المسموح لهذا المستخدم. قد تكون حقول الحالة والطرف والمهام والروابط فارغة عمداً في إسقاط المهمة؛ لا تحفظ محتوى مخفي في cache أو إشعار.

الرسالة: `POST /support/cases/{caseId}/messages` بجسم `{body, expectedVersion, idempotencyKey, taskId?, attachmentKeys?}`. استخدم `taskId` الصادر من الخادم فقط للمحادثة الخاصة بالمهمة. الملاحظة الداخلية مسار منفصل للوحة الإدارة ولا تستعمله تطبيقات الجوال. كل إجراء يستخدم `POST /support/cases/{caseId}/actions/{operation}` وجسم يبدأ بـ `{expectedVersion, idempotencyKey}`؛ الحقول الاختيارية `reason`, `taskId`, `taskResult` وغيرها مطلوبة بحسب العملية. لا يوجد ضبط عام للحالة.

الحالات التي يعيدها العقد: `New`, `Acknowledged`, `InProgress`, `WaitingOnRequester`, `WaitingOnParty`, `Resolved`, `Closed`. أنواع المهام: `RestaurantEvidence`, `DeliveryAction`, `PlatformReview`. أنواع المستلم في العقد: `Requester`, `Restaurant`, `DeliveryManager`, `PlatformTeam`؛ واجهة الجوال الحالية تخص مهام المطعم ومسؤول التوصيل فقط. استخدم النصوص كما وصلت من الخادم ولا تستنتج انتقالاً محلياً.

## الأخطاء والتزامن وإعادة المحاولة

رموز Problem Details تشمل `support.authentication_required` (401)، `support.access_denied` (403)، `support.case_not_found` (404)، `support.request_invalid` (400)، `support.transition_invalid` و`support.version_conflict` و`support.idempotency_key_reused` (409)، و`support.operational_route_required` (400). في الرفع: `support.attachment_unsupported` و`support.attachment_rejected` يعيدان 400؛ `support.attachment_storage_unavailable` و`support.attachment_scan_unavailable` يعيدان 422. قد تظهر رموز المرفق كذلك عند القراءة إذا لم يعد الملف متاحاً أو مصرحاً.

عند timeout أو انقطاع أو `5xx`، أعد فقط الطلبات ذات المفتاح بنفس الجسم والمفتاح تماماً. لا تولد مفتاحاً جديداً قبل معرفة نتيجة الطلب. عند تعارض `version`، أعد الجلب واعرض المسودة والتغيير للمستخدم؛ الإرسال التالي يحتاج تأكيداً ومفتاحاً جديداً وإصدار الحالة الحالي. لا تعِد تلقائياً أوامر البلاغ التشغيلي `progress` و`resolve`؛ لا تحتوي على مفتاح idempotency.

## المرفقات الخاصة

ارفع الملف أولاً إلى `POST /support/attachments` كـ multipart باسم الحقل `file`. الحد الأقصى 25 MiB، والأنواع المدعومة PNG وJPEG وWebP وPDF. الرد `{storageKey, fileName, contentType, sizeBytes}`؛ `storageKey` ليس رابط قراءة. أرسل حتى خمسة مفاتيح مملوكة للرافع في `attachmentKeys` ضمن الرسالة. بعد قبول الرسالة استخدم `attachments[].readPath` فقط، مع Bearer token، لقراءة `GET /support/cases/{caseId}/messages/{messageId}/attachments/{index}`. يعاد فحص التفويض في كل قراءة، والاستجابة `no-store`؛ لا تنشئ رابطاً عاماً أو تعرض مفتاح التخزين.

رفع الملف نفسه لا يملك مفتاح idempotency عاماً. إذا كانت نتيجة الرفع مجهولة، لا تكرر الرفع تلقائياً؛ صالح الحالة قبل المحاولة التالية. scanner غير متاح أو رفض الفحص يعني أن الملف لا يصبح دليلاً قابلاً للقراءة.

## الإشعارات والتوقيت وإعادة الاتصال

قد يصل الإشعار أكثر من مرة. أزل التكرار باستخدام `eventId` و`notificationId` عندما يكونان موجودين، واستخدم `caseId` و`deepLink` للتوجيه فقط. حمّل التفاصيل الحالية من API عند فتح الرابط أو عند رجوع التطبيق من الخلفية أو reconnect. بعد `401` جدّد الجلسة بالطريقة المعتادة؛ عند `403/404` أزل التفاصيل الخاصة والملفات المؤقتة من الذاكرة.

تعرض واجهة الحالة فقط `activeTimingCycle` ومواعيد UTC التي أعادها الخادم (`firstResponseDeadlineUtc`, `resolutionDeadlineUtc`, و`escalationDeadlineUtc` إن أعيدت). قد لا توجد دورة فعالة أو سياسة إعداد، وقد تكون المواعيد `null`. حوّل القيم غير الفارغة إلى توقيت الجهاز للعرض فقط؛ لا تحسب SLA ولا تضع قيمة افتراضية ولا تنشر سياسة من الجوال.

## مسارات الشاشة وقبول التكامل

1. افتح قائمة الدعم ثم تفاصيل حالة مرئية، مع pagination من الخادم.
2. أنشئ حالة عامة بمفتاح فريد ثابت، ثم أرسل رسالة باستخدام `version` الحالي.
3. ارفع ملفاً، اربط المفتاح برسالة، ثم اختبر القراءة الموثقة بعد حفظ الرسالة.
4. افتح إشعاراً أو deep link بارداً ودافئاً وبعد reconnect؛ أعد جلب التفاصيل من API.
5. اختبر حسابين لكل دور، ومعرف حالة خارج النطاق، وإلغاء مهمة، وتبديل الحساب، وانتهاء الجلسة، وفشل scanner، و`409`، و`5xx`، ورفعاً ذا نتيجة مجهولة.

لا تعتبر هذه المواصفات إثباتاً على بناء أو نشر تطبيق جوال أو إعداد Firebase. لا يوجد في هذه الحزمة ملف collection آلي: لا نضيف collection إلا بعد توليده من Swagger الفعلي والتحقق من التوثيق والمسارات والـ schemas.
