# مراجعة إضافية لمسارات التقويم الطرفية — 2026-10-10

تم تنفيذ الإصلاحات بتاريخ 2026-10-11؛ راجع [تقرير الإصلاح والتحقق](2026-10-11-calendar-edge-fixes-verification.md). التفاصيل التالية تسجل سلوك ما قبل الإصلاح وأدلة إعادة إنتاجه.

المراجعة بعد الإصلاح `2dec69e64c3ace4b183fa8d58299c796c9314d20` على `dev`. هذه نتائج مراجعة فقط؛ لا تغييرات في سلوك التطبيق أو رفع إلى Git في هذه الجولة.

## 1. P1 — نافذة الاستبدال لا تتسق مع صلاحية تعديل اليوم ومدتها

- `OpenReplacementWindowCommandHandler` في `AdminOrderCommands.cs:150` يغيّر الحالة إلى `ReplacementWindowOpen` ويعيد `WindowClosesAt = now + 24h`، دون حفظ موعد فتح/انتهاء مستقل أو تغيير صلاحية التعديل.
- `CustomerCalendarEngine.ResolveEditBlockReason` و`EnsureEditable` يستمران في رفض الاختيار عندما انتهت `SelectionDeadlineUtc` الأصلية. هذا هو السيناريو المعتاد عند فتح استبدال بعد قفل اليوم.
- `GetReplacementWindowsQueryHandler` في `AdminOrderCommands.cs:245` يحسب المدة والحالة من تاريخ إنشاء طلب المطعم، لا وقت فتح نافذة الاستبدال. طلب قديم يظهر `expired` رغم أن استدعاء الفتح أعاد مهلة جديدة 24 ساعة.
- التجربة `Audit_Reproduces_AdminReplacementWindowClosedForCustomerAndAlreadyExpiredInAdminList` نفّذت معالج الفتح فعليًا: الرد يحدد 24 ساعة جديدة، GET العميل يرجع `canEdit=false` و`selection_deadline_passed`، اختيار المطعم يُرفض، وقائمة الأدمن تعرض `expired`.
- من مراجعة الكود: مهمة القفل تستهدف `Open/AutoSelected/CustomerSelected/ReplacementRequired` فقط مع `LockAtUtc=null`؛ لا يمكن الاعتماد عليها لإنهاء نافذة `ReplacementWindowOpen` بمجرد مرور وقتها.
- المطلوب: حفظ مواعيد نافذة الاستبدال والتحقق منها في القراءة والكتابة وقائمة الأدمن، وتحديد مسار انتهاء النافذة. النافذة التشغيلية تحتاج قاعدة مستقلة واضحة؛ لا تمدّد مهلة الأيام العادية أو تغيّر قاعدة 72 ساعة صامتًا.

## 2. P2 — حالة فشل البوكس المحفوظ تتراجع في المعاملة الحقيقية

- عند فشل إصلاح تعارض الحساسية، `ConfirmAndSendToRestaurantAsync` في `CustomerCalendarEngine.cs:500` يكتب `ReplacementRequired` و`FailureReasonCode` ويستدعي SaveChanges ثم يعيد Result فاشلًا. الفرع المماثل موجود في الاختيار التلقائي للبوكس المحفوظ.
- `WithContext` يلف الكتابة في `PostgresApplicationTransactionExecutor.ExecuteResultAsync`؛ في `PostgresApplicationTransactionExecutor.cs:28` تُلغى المعاملة عند Result فاشل. لذلك SaveChanges داخلها لا يثبت انتقال حالة الفشل.
- التجربة `Audit_Reproduces_UnsafeSavedBoxFailureRollsBackReplacementStateInPostgreSQL` استخدمت PostgreSQL ومعاملات وقفل التطبيق الفعليين. الرد `calendar.no_safe_box`، ولكن بعد فتح DbContext جديد بقي اليوم `CustomerSelected` وسبب الفشل `null`.
- لم يُنشأ طلب مطعم غير آمن؛ الخلل في الحالة التي يعرضها التقويم بعد الفشل. مهمة القفل عند حلول المهلة لها تصعيد مستقل، فلا ينطبق هذا الاستنتاج على كل حالات الاستبدال أو التصعيد.
- اختبارات الذاكرة التي تتوقع حفظ `ReplacementRequired` في نفس الفرع لا تكشف rollback PostgreSQL؛ كانت ضمن الاختبارات القائمة التي نجحت في الجولة السابقة.
- المطلوب: فصل تسجيل انتقال الفشل الدائم عن معاملة الاختيار التي يجب إلغاؤها، مع عدم تثبيت نقل سعة أو وجبات جزئي. اختبار PostgreSQL يجب أن يثبت GET الصحيح بعد الفشل وعدم وجود طلب غير آمن أو إشعار مكرر.

## التحقق وحدوده

- أُعيد إنتاج الفجوتين باختبارين تشخيصيين ناجحين في رصد السلوك المعيب الحالي: اختبار نافذة الاستبدال في InMemory، واختبار rollback في PostgreSQL عبر Testcontainers. نجاحهما لا يعني إصلاح العيوب.
- الأمر: `dotnet test F:/MealMate/outputs/calendar-edge-audit/CalendarEdgeAudit.csproj --nologo --filter FullyQualifiedName~Audit_Reproduces`.
- ملفات التجارب معزولة في `F:/MealMate/outputs/calendar-edge-audit/` وتعتمد على مخرجات build الإصلاح الأخير. لم تُضف إلى مشاريع الإنتاج.
- لا تشغيل للموبايل أو تبديل لغة حي أو بيئة منشورة. نتائج 2048 Unit و244 API و16 PostgreSQL من جولة الإصلاح السابقة صحيحة ضمن تغطيتها، ولا تشمل هذه التجارب الطرفية الجديدة.

Mock Data Check:
- No mock business data introduced.
- No fake fallback responses introduced.
- No hardcoded production business values introduced.
- All runtime data comes from real application sources.

لم تُعدل مصادر الإنتاج؛ البيانات المصطنعة محصورة في تجارب الاختبار.
