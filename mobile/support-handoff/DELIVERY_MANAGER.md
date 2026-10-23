# تنفيذ الدعم لمسؤول التوصيل

يعرض التطبيق مسارين منفصلين: مهام الحالة العامة المسندة للمسؤول، وبلاغات السائق التشغيلية القديمة التي تتبع للمطعم. لا تعرض واجهة إدارة التوصيل تفاصيل الشكوى العامة أو هوية مقدمها لمجرد وجود مهمة.

## مهام الحالة العامة

- طلب مهام المستخدم من `GET /api/v1/support/cases`. الخادم يعيد الحالات التي أنشأها المستخدم أو المهام المسندة النشطة ضمن `RestaurantId` وعضوية مسؤول التوصيل الحالية.
- افتح `GET /api/v1/support/cases/{caseId}`؛ إسقاط المهمة فقط يخفي عنوان/وصف الحالة، طالبها، الروابط، الرسائل الأخرى ومهام الأطراف. أظهر `SupportCaseTaskDto` الخاص بالمهمة المرئية فقط.
- أرسل جواب المهمة عبر `POST /api/v1/support/cases/{caseId}/messages` مع `taskId` الذي أعاده الخادم، `expectedVersion`, و`idempotencyKey`. لإكمال المهمة استخدم `POST /api/v1/support/cases/{caseId}/actions/complete-party-task` بجسم `{taskId, taskResult, expectedVersion, idempotencyKey}`. الإلغاء وإعادة الإسناد صلاحية إدارة، وليسا إجراءً لمسؤول التوصيل.
- بعد إلغاء المهمة أو تغير عضوية المستخدم، حدّث القائمة والتفاصيل فوراً. لا تحتفظ محلياً بنص خاص بعد `403/404`.

## البلاغ التشغيلي وتسليم العهدة

- قائمة/تفاصيل البلاغ: `GET /api/v1/support/operational-issues`, `GET /api/v1/support/operational-issues/{issueId}`، مع أدلة عبر المسار الفرعي `/{issueId}/evidence/{evidenceId}`. صلاحية القراءة تتحدد بنطاق المطعم المحفوظ.
- تحديث التقدم: `POST /api/v1/support/operational-issues/{issueId}/progress` بجسم `{stage, notes?}`. لا يوجد idempotency key لهذا الأمر؛ بعد نتيجة غير معروفة، اجلب التفاصيل قبل إعادة التغيير.
- التصعيد: `POST /api/v1/support/operational-issues/{issueId}/escalate` بالـ `{reason, idempotencyKey}`. أعد نفس الطلب والمفتاح عند timeout فقط؛ لا تنشئ تصعيداً جديداً عن طريق توليد مفتاح بديل دون قراءة.
- الحل: `POST /api/v1/support/operational-issues/{issueId}/resolve` بالـ `{resolutionNotes}`. لا يوجد idempotency key؛ اقرأ الحالة أولاً عند انقطاع الاتصال. الخادم يرفض الحل إذا بقي handover أو follow-up مفتوحاً.
- عمليات عهدة الصناديق لها endpoints منفصلة `/api/v1/dispatcher/support-handovers/...`; التعيين فقط يملك idempotencyKey؛ الوصول والتسليم والإلغاء لهما قواعد وقيود مختلفة موضحة في [عقد العهدة](../DRIVER_SUPPORT_HANDOVER_CONTRACT.md).

## إشعارات الخصوصية

صندوق المسؤول: `GET /api/v1/delivery-manager/notifications`; القراءة: `PUT /api/v1/delivery-manager/notifications/{id}/read`; تسجيل وإلغاء FCM: `POST/DELETE /api/v1/delivery-manager/device-token`. إشعارات الحالة العامة والمهام لهذا الدور تكتب inbox وoutbox دفع مستدام. الحمولة تحمل بيانات توجيه عامة؛ لا تضف نص المهمة أو وصف الشكوى في push. عند فتح deep link، حمّل التفاصيل الحالية من الخادم.

## قبول التسليم

- مسؤول المطعم A لا يرى مهمة مطعم B؛ بعد إزالة عضويته أو إلغاء المهمة لا يستطيع فتح الرابط المحفوظ.
- إسقاط task-only لا يعرض case subject/description/requester/other tasks، والرد مربوط بـ `taskId` الصحيح.
- إعادة المحاولة في `progress` و`resolve` لا تتم قبل GET؛ التصعيد وإسناد handover يعيدان الطلب بالمفتاح الثابت فقط عند دعم ذلك صراحة.
- فشل حل قضية بسبب handover/follow-up يعرض الإجراء المتبقي من الحالة التشغيلية دون تعديل مالي تلقائي.
- push عام ثم cold/warm open يعيد جلب API؛ جرّب حذف token، refresh، انقطاع الشبكة، صلاحية مرفوضة، وإشعار مكرر.
