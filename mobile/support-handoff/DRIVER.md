# تنفيذ الدعم في تطبيق السائق

## اختيار المسار

يوجد نوعان منفصلان. لا تنشئ `OperationalReport` عبر الحالة العامة؛ الخادم يرفض ذلك ويطلب المسار التشغيلي.

| الحاجة | المسار الموجود | السلوك |
|---|---|---|
| سؤال/طلب عام غير مرتبط ببلاغ رحلة | `POST /api/v1/support/cases` بـ `channel: "Driver"`, `kind: "GeneralRequest"` | احتفظ بـ `idempotencyKey`; قائمة وتفاصيل الحالة من `/api/v1/support/cases`. |
| رد على محادثة عامة | `POST /api/v1/support/cases/{caseId}/messages` | يتطلب `expectedVersion` و`idempotencyKey`. |
| بلاغ عطل/حادث/مشكلة مرتبطة بالتوصيل | `POST /api/v1/driver/orders/{boxId}/report-issue` | المسار التشغيلي القديم؛ `{boxId}` هو TripStop ID (ليس رقم العرض أو DailyOrderId). يحتفظ بالتصنيف وسجل البلاغ. راجع عقد العهدة لحقول الطلب. |
| طلب إعادة تعيين سائق/صندوق | `POST /api/v1/driver/orders/{boxId}/request-reassignment` | طلب عهدة مستقل. ضياع الرد يعالج بقراءة `GET /api/v1/driver/reassignment-requests?boxId={tripStopId}&page=1&pageSize=20`; التكرار قد يرجع conflict ويجب بعدها إعادة القراءة. |
| استعادة طلب إعادة التعيين | `GET /api/v1/driver/reassignment-requests?boxId={tripStopId}&page=1&pageSize=20`, `GET /api/v1/driver/reassignment-requests/{requestId}` | للطالب الأصلي فقط؛ استخدمها للمصالحة بعد نتيجة POST غير مؤكدة. |
| قائمة وتفاصيل البلاغ التشغيلي | `GET /api/v1/driver/support/issues`, `GET /api/v1/driver/support/issues/{issueId}` | لا تعتبره حالة الدعم العامة. |
| صور إضافية لبلاغ قائم | `POST /api/v1/driver/support/issues/{issueId}/photos` | يقبل مفاتيح صور مملوكة ونظيفة؛ ليس endpoint إنشاء البلاغ. |
| عهدة الاستبدال | `GET /api/v1/driver/support-handovers`, `GET /api/v1/driver/support-handovers/{handoverId}` | مسار custody مستقل؛ لا تغيّر الحالة محلياً بديلاً عن استجابة الخادم. |

تفاصيل `report-issue`, سجل إعادة التعيين، دليل الأصناف، ومسارات arrival/scan/complete/cancel موجودة في [عقد البلاغات والعهدة](../DRIVER_SUPPORT_HANDOVER_CONTRACT.md). لا تخترع endpoint للإنشاء العام من صفحة البلاغ التشغيلي.

## التنبيهات والتعافي

- صندوق السائق من `GET /api/v1/driver/notifications`; علّم القراءة عبر `PUT /api/v1/driver/notifications/{id}/read`. تسجيل FCM: `POST /api/v1/driver/device-token`, وإلغاءه عند logout عبر `DELETE /api/v1/driver/device-token`.
- تحديثات الدعم العامة تحفظ inbox وطلب push في المعاملة نفسها. قبل الإرسال يعيد الخادم التحقق من الحساب، وأن السائق ما زال صاحب الحالة ولديه ملف سائق حالي، ثم يختار tokens النشطة غير المحذوفة المرتبطة بالحساب/الملف. التسليم at-least-once؛ أزل التكرار بواسطة `eventId` و`notificationId`، وافتح `deepLink` وحمّل الحالة الحالية من API. وجود هذا المسار في المصدر لا يثبت إعداد Firebase أو تسليم staging أو قبول تطبيق السائق.
- بلاغات الرحلات والعهدة القديمة لها أحداثها التشغيلية المنفصلة؛ لا تخلط معرف `issueId` أو `handoverId` مع `caseId`.
- افتح الرابط `/support/cases/{caseId}` للحالة العامة و`/driver/support/issues/{issueId}` لبلاغ التشغيل. على cold start/warm open/reconnect، أعد الجلب من endpoint الخاص بنوع المعرف.

## الحفظ وإعادة المحاولة

- احفظ المسودة ومفتاح idempotency تحت actor وAPI base URL، وامسحهما عند logout أو تبديل الحساب.
- للحالة العامة: أعد فقط أوامر العقد التي تدعم replay بالمفتاح والـ payload الثابت نفسيهما. `409 version_conflict` يتطلب إعادة جلب ومراجعة السائق ثم مفتاحاً جديداً.
- للبلاغ التشغيلي legacy: `progress` و`resolve` لا يملكان idempotency key في عقودهما الحالية. عند نتيجة غامضة أعد GET أولاً، ثم دع المستخدم يختار الإجراء التالي؛ لا تعِد POST تلقائياً.
- لإعادة التعيين، استخدم GET للتحقق بعد timeout/conflict كما يحدد عقد العهدة. لا تستنتج انتقال custody من طلب مقبول؛ العهدة لا تنتقل حتى اكتمال handover.
- إذا فشل رفع صورة بنتيجة مجهولة، لا تكرر الرفع تلقائياً ولا تفترض أن الملف انحذف؛ احتفظ بالمفتاح إن وصل، وصالح الرفع قبل ربطه بالبلاغ.

## قبول التسليم

- سائق لا يستطيع قراءة بلاغ سائق آخر أو حالة عامة ليست له؛ اختبر تبديل الحساب بعد بقاء التطبيق في الخلفية.
- بلاغ operational report من الحالة العامة يفشل بخطأ المسار المطلوب، بينما يعمل تقرير الرحلة عبر route الحالي.
- إعادة الاتصال تعيد القائمة/التفاصيل، ولا تكرر إسناد أو تنفيذ resolve/progress دون قراءة حديثة.
- صورة نظيفة مملوكة يمكن ربطها بالبلاغ؛ ملف غير مملوك، نوع غير مدعوم، فحص مرفوض، أو scanner unavailable لا يصبح دليلاً.
- تأكيد العهدة لا يحدث حتى يتطابق عدد الصناديق/نتائجها ويقبل الخادم completion؛ حافظ على missing/damaged follow-up كما هو.
- اختبر revoke للجلسة، صلاحية منتهية، مهمة ملغاة، deep link بارد، وغياب نصوص البلاغ الخاص في push payload.
