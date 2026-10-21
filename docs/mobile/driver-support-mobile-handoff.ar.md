# تسليم تكامل دعم السائق لتطبيق الهاتف

هذا الدليل يصف عقود الـAPI الموجودة في الشيفرة. أمثلة JSON أدناه **توضيحية فقط وليست بيانات تشغيلية**. استخدم عنوان API النسبي الذي يحدده إعداد التطبيق؛ لا يوجد عنوان مضيف ثابت في هذا العقد. تتطلب طلبات السائق رمز Bearer وهوية سائق مصادقاً عليها.

## شاشة قائمة البلاغات

`GET /api/v1/driver/support/issues?status=All&search=&pageNumber=1&pageSize=20`

القيم الدقيقة لـ`status` هي `All` و`UnderReview` و`AwaitingReply` و`Resolved`. الحد الافتراضي للصفحة 20 والحد الأقصى 100؛ رقم الصفحة يبدأ من 1، والبحث حتى 200 حرف. رد JSON يستخدم camelCase:

```json
{
  "allCount": 1,
  "underReviewCount": 0,
  "awaitingReplyCount": 1,
  "resolvedCount": 0,
  "totalCount": 1,
  "pageNumber": 1,
  "pageSize": 20,
  "issues": [
    {
      "issueId": "00000000-0000-0000-0000-000000000000",
      "category": "VehicleBreakdown",
      "categoryLabel": "مثال توضيحي",
      "title": "مثال توضيحي",
      "status": "AwaitingReply",
      "statusLabel": "مثال توضيحي",
      "submittedAtUtc": "2026-10-21T08:00:00Z",
      "orderCode": null
    }
  ]
}
```

الحقول `allCount` و`underReviewCount` و`awaitingReplyCount` و`resolvedCount` تحصي كل بلاغات الدعم ضمن نطاق السائق، قبل البحث والصفحات. `totalCount` هو عدد النتائج بعد تطبيق الحالة والبحث. `orderCode` قد يكون `null` عندما لا يمكن ربط البلاغ بطلب واحد بشكل موثوق. اعرض تسميات `categoryLabel` و`statusLabel` كما يعيدها الخادم.

## شاشة تفاصيل البلاغ

`GET /api/v1/driver/support/issues/{issueId}`

الرد الكامل (كل القيم أدناه مثال توضيحي):

```json
{
  "issueId": "00000000-0000-0000-0000-000000000000",
  "category": "VehicleBreakdown",
  "categoryLabel": "مثال توضيحي",
  "title": "مثال توضيحي",
  "description": "مثال توضيحي",
  "status": "AwaitingReply",
  "rawStatus": "Submitted",
  "statusLabel": "مثال توضيحي",
  "submittedAtUtc": "2026-10-21T08:00:00Z",
  "updatedAtUtc": null,
  "orderCode": null,
  "address": null,
  "area": null,
  "orderCreatedAtUtc": null,
  "timeline": [
    { "stage": "Submitted", "label": "مثال توضيحي", "state": "Current", "occurredAtUtc": "2026-10-21T08:00:00Z" },
    { "stage": "UnderReview", "label": "مثال توضيحي", "state": "Unrecorded", "occurredAtUtc": null },
    { "stage": "ContactingCustomer", "label": "مثال توضيحي", "state": "Unrecorded", "occurredAtUtc": null },
    { "stage": "Resolved", "label": "مثال توضيحي", "state": "Unrecorded", "occurredAtUtc": null }
  ],
  "evidenceUrls": [],
  "resolutionNotes": null,
  "resolvedAtUtc": null,
  "canContactSupport": false,
  "contactChannel": null
}
```

ترتيب `timeline` وقيم `stage` ثابتة: `Submitted` ثم `UnderReview` ثم `ContactingCustomer` ثم `Resolved`. قيمة `state` تكون `Current` أو `Completed` أو `Unrecorded`. أوقات المراحل التي لا يثبتها سجل محفوظ تبقى `null`؛ لا تستنتج حدوث مراجعة أو اتصال من حالة البلاغ أو `updatedAtUtc`. `status` هو التجميع المناسب للواجهة، أما `rawStatus` فيحفظ حالة الشكوى الأصلية مثل `Submitted` أو `UnderReview` أو `Resolved`. الأوقات بصيغة UTC. بيانات الطلب اختيارية وقد تكون كلها `null` عندما يكون الربط مفقوداً أو ملتبساً. `canContactSupport` يساوي `false` و`contactChannel` يساوي `null` لأنه لا توجد خدمة محادثة دعم مهيأة في التنفيذ الحالي.

## إنشاء بلاغ وربط الصور

إنشاء البلاغ الحالي:

`POST /api/v1/driver/orders/{boxId}/report-issue`

`boxId` هو GUID نقطة التوقف/التسليم (TripStop)، وليس رمز الطلب المعروض. الجسم:

```json
{
  "issueType": "VehicleBreakdown",
  "description": "نص يكتبه السائق",
  "evidencePhotoStorageKey": null,
  "evidencePhotoStorageKeys": [],
  "severity": "Medium"
}
```

القيم المعروفة لـ`issueType`: `CustomerUnavailable`, `AddressIssue`, `DamagedBox`, `SevereDelay`, `CustomerRefusal`, `VehicleBreakdown`, `TrafficAccident`, `RoadIssue`, `SevereWeather`, `Other`. الوصف اختياري؛ يُزال الفراغ من بدايته ونهايته ثم يجب ألا يتجاوز 250 حرفاً. الشدة `Low` أو `Medium` أو `High` والافتراضي `Medium`. يقبل العقد المفتاح المفرد القديم `evidencePhotoStorageKey` ومصفوفة المفاتيح `evidencePhotoStorageKeys`؛ المكرر يُزال، والحد 3 صور. النجاح يعيد GUID البلاغ كسلسلة JSON. هذا الإنشاء ليس idempotent؛ إذا انتهت مهلة الطلب، أعد تحميل القائمة قبل أي إعادة إرسال حتى لا تنشئ بلاغاً مكرراً.

لرفع صورة أولاً استخدم `POST /api/v1/uploads` بطلب `multipart/form-data` وبالحقول `file` (ملف الصورة)، `scope=drivers`، و`folder=issue-evidence`. أنواع الصور المقبولة هنا JPEG وPNG وWebP. النجاح `201` ويعيد `UploadedFileDto` بحقول `storageKey`, `fileName`, `contentType`, `sizeBytes`, `checksum`, `readUrl`, `folder`, `scope`. أرسل قيمة `storageKey` كما أعادها رد الرفع بالضبط لربطها بالبلاغ، حتى لو كانت رابط HTTPS كاملاً من ImageKit. لا تحذف المضيف أو بادئة المسار، ولا تستبدلها بقيمة مشتقة من `readUrl` أو برابط أنشأه العميل. يتحقق الباك من سجل الرفع الأصلي وملكية السائق ونوع الصورة ووجودها.

إضافة صور إلى بلاغ قائم:

`POST /api/v1/driver/support/issues/{issueId}/photos`

```json
{ "storageKeys": ["https://cdn.example/account/uploads/drivers/issue-evidence/evidence.jpg"] }
```

الرد `issueId`, `status`, و`evidenceUrls`. قيمة `status` تستخدم نفس التجميع المعروض في القائمة (`AwaitingReply` أو `UnderReview` أو `Resolved`) وليست الحالة الخام للشكوى. المفاتيح يجب أن تكون مرفوعة من المستخدم الحالي، لصورة JPEG/PNG/WebP موجودة فعلياً في التخزين، ومن مجلد أدلة السائق المقبول. التكرار آمن ولا يضيف الصورة مرتين، والحد الإجمالي ثلاث صور. لا يمكن الإضافة إلى بلاغ منتهٍ.

| رمز المجال | HTTP | التعامل في التطبيق |
|---|---:|---|
| `DRIVER_SUPPORT_ISSUE_NOT_FOUND` | 404 | البلاغ غير موجود أو خارج نطاق السائق؛ اعرض حالة غير متاح. |
| `DRIVER_SUPPORT_PHOTOS_INVALID` | 400 | افحص قائمة المفاتيح ثم أعد المحاولة بعد تصحيحها. |
| `DRIVER_SUPPORT_PHOTO_UPLOAD_INVALID` | 400 | ارفع ملفاً من المستخدم الحالي وبصيغة/مجلد مقبولين. |
| `DRIVER_SUPPORT_PHOTO_MISSING` | 400 | أعد رفع الملف ثم استخدم `storageKey` الجديد. |
| `DRIVER_SUPPORT_PHOTO_LIMIT` | 400 | الحد الأقصى ثلاث صور إجمالاً. |
| `DRIVER_SUPPORT_ISSUE_TERMINAL` | 409 | البلاغ منتهٍ ولا يقبل صوراً جديدة. |
| `DRIVER_SUPPORT_ISSUE_INCOMPLETE` | 409 | لا يمكن إتمام الربط من البيانات المحفوظة؛ أعد تحميل البلاغ. |

وقد يعيد تحديث التقدم `DRIVER_SUPPORT_PROGRESS_INVALID` (400) أو `DRIVER_SUPPORT_PROGRESS_ORDER_INVALID` (409) عند قيمة غير صالحة أو محاولة عكس/تخطي المرحلة. تعامل أيضاً مع 401/403 وسياسة إعادة المحاولة العامة دون الاعتماد على نص مترجم ثابت.

روابط الصور المحفوظة كمسارات محلية والتي تمر عبر `/api/v1/uploads/content` تتضمن `readToken` مشفراً خاصاً بملف واحد وصالحاً لمدة 15 دقيقة. أما مفاتيح الرفع المطلقة عبر HTTPS مثل ImageKit فيعيدها API كما هي كروابط CDN؛ لا تحتاج هذه الروابط إلى رمز قراءة API أو Bearer ولا يضيف الباك إليها توقيعاً. يمكن عرض الرابط كاملاً مباشرةً في `Image.network` دون هيدر تسجيل دخول؛ لا تحذف أو تعيد تكوين معاملاته. هذا رمز قراءة محدود للصورة وليس JWT. يبقى رابط `GET /api/v1/uploads/content?key={storageKey}` دون هذا الرمز محمياً ويتطلب جلسة المالك أو موظفي التوصيل. بعد انتهاء صلاحية رابط الصورة، أعد طلب تفاصيل البلاغ للحصول على رابط جديد؛ لا تحفظه كرابط دائم. يتم إصدار الروابط من ردود ناجحة لمستخدم مصادق عليه فقط، وتبقى صلاحيات قراءة البلاغ كما هي.

## تشخيص الصور التي تختفي بعد الرجوع للبلاغ

رفع الملف وربطه بالبلاغ خطوتان منفصلتان: احتفظ بقيمة `storageKey` كاملة من رد الرفع (مساراً نسبياً أو رابط HTTPS مطلقاً)، وأرسله في `evidencePhotoStorageKeys` عند إنشاء البلاغ أو في `storageKeys` لمسار إضافة الصور. لا تعتبر المعاينة المحلية أو نجاح الرفع وحده دليلاً على ارتباط الصورة بالبلاغ. بعد نجاح الربط، أعد قراءة التفاصيل من الخادم.

رد تفاصيل السائق يحتوي `evidenceUrls` كمصفوفة روابط. رد تفاصيل مسئول التوصيل يحتوي `evidencePhotos` كمصفوفة كائنات وبداخل كل كائن `url`؛ لا تتوقع نفس اسم الحقل في الردين. إذا كانت المصفوفة فارغة فراجع طلب الربط و`issueId`؛ وإذا كانت ممتلئة والصورة لا تفتح فراجع طلب الصورة نفسه ورمز HTTP. `401` للرابط القديم يعني ضرورة إعادة تحميل التفاصيل أو إرسال Bearer في طلب الصورة القديم.

عند الرابط النسبي `/api/v1/uploads/content?...` اربطه بأصل عنوان API (`scheme` و`host` و`port`) دون تكرار `/api/v1`. استخدم الرابط الموقع كما أعاده الخادم دون وضع JWT في الـquery، ولا ترسل Bearer إلى مضيف CDN خارجي. التعامل مع الرابط الموقع لا يحتاج تغيير أسماء حقول DTO أو مسارات الربط.

الرابط الموقع يمنح قراءة الملف طوال مدة صلاحيته، لذا لا تسجله أو تسجل `readToken` في التحليلات أو سجلات الوصول لدى الـproxy. ردود الصور والتفاصيل الموقعة تحمل `Cache-Control: no-store` و`Referrer-Policy: no-referrer`. تشغيلياً يجب الاحتفاظ بمفاتيح ASP.NET DataProtection، ومشاركتها بين نسخ API عند تشغيل أكثر من نسخة حتى تقرأ كل نسخة الروابط الصادرة عن الأخرى. هذا إصلاح في الباك لطريقة قراءة الصور؛ لا يثبت بمفرده أن نسخة الموبايل الحالية ترسل مفاتيح الربط، ويحتاج التحقق من طلباتها الفعلية عند استمرار مصفوفة صور فارغة.

## إجراءات الموظفين فقط

تقدم المعالجة للموظفين فقط:

`POST /api/v1/dispatcher/support/issues/{issueId}/progress`

```json
{ "stage": "UnderReview", "notes": "ملاحظة اختيارية" }
```

`stage` يقبل `UnderReview` أو `ContactingCustomer`، و`notes` اختياري حتى 500 حرف. هذا المسار لا يمنح السائق صلاحية التحديث. ومسار الإغلاق/الحل الموجود للموظفين فقط هو `POST /api/v1/dispatcher/support/issues/{issueId}/resolve` وجسمه `{ "resolutionNotes": "ملاحظة من 3 إلى 500 حرف" }`؛ ملاحظات الحل مطلوبة من 3 إلى 500 حرف. لا تستدع هذه المسارات من تطبيق السائق.

## اللغة والتحديثات الفورية

أرسل `Accept-Language: ar` أو `Accept-Language: en` للحصول على تسميات مناسبة؛ تبقى قيم الآلة الإنجليزية كما هي. خزّن وفسّر الأوقات كـUTC ثم حوّلها لمنطقة الجهاز عند العرض. لا تحوّل تاريخاً `null` إلى وقت افتراضي.

أحداث SignalR مختلفة بحسب الجمهور: DriverHub على `/hubs/driver-hub` (أو `/hubs/driver`) يرسل `driver-support-issue-updated`، وDispatcherHub على `/hubs/dispatcher-hub` (أو `/hubs/dispatcher`) يرسل `driver-issue-updated`. حمولة تحديث البلاغ تتضمن `eventId`, `occurredAtUtc`, `issueId`, `restaurantId`, `driverId`, `category`, و`eventName`. يسجل تقدم الموظف أيضاً إشعار `DriverNotification` بنوع `driver-support-progress`. حدث الـoutbox الداخلي اسمه `driver-support-issue-changed.v1` وليس اسم SignalR للواجهة. بعد إعادة الاتصال أو فقد حدث، أعد قراءة القائمة أو التفاصيل من API؛ لا تعتمد على بث محفوظ ولا تخلط اسمي الحدثين.

## قائمة تحقق Flutter

- نفّذ حالات التحميل، القائمة الفارغة، خطأ الشبكة، 401/403، 404، والتحقق من معاملات البحث والصفحات.
- اعرض العدادات العامة منفصلة عن `totalCount`، وحافظ على الحالة/البحث عند الانتقال بين الصفحات.
- اعرض نص الوصف كما أدخله السائق، واترك حقول الطلب والمراحل غير المسجلة فارغة بوضوح.
- ارفع الصورة ثم أرسل `storageKey`؛ لا تعاود إنشاء بلاغ تلقائياً بعد timeout، وأعد قراءة بيانات الخادم بعد reconnect.
- اعرض روابط الصور الموقعة كما أعادها API، وأعد تحميل التفاصيل بعد انتهاء صلاحيتها؛ الرابط القديم دون توقيع يحتاج Bearer في طلب الصورة نفسه.
- لا تعرض زر محادثة دعم؛ واجهة الخادم تعيد `canContactSupport=false` و`contactChannel=null`.

## مراجع التنفيذ

- بلاغ السائق: `src/MealMate.Api/Controllers/V1/DriverHomeController.cs` و`src/MealMate.Application/Features/DriverDelivery/DriverApp/DriverAppDtos.cs`.
- رفع الملفات وقراءة الدليل الخاص: `src/MealMate.Api/Controllers/V1/UploadsController.cs` و`src/MealMate.Application/Features/Uploads/UploadFiles.cs`.
- القائمة والتفاصيل وربط الصور: `src/MealMate.Api/Controllers/V1/DriverSupportIssuesController.cs` وملفات `DriverSupportIssue*` في `src/MealMate.Application/Features/DriverDelivery/DriverApp/`.
- تقدم الموظف والحل الموجود: `src/MealMate.Api/Controllers/V1/DispatcherSupportProgressController.cs` و`src/MealMate.Api/Controllers/V1/DispatcherDashboardController.cs`.
- SignalR والأحداث: `src/MealMate.Api/Realtime/DriverHub.cs`, `DispatcherHub.cs`, و`DriverSupportHandoverRealtimeNotifier.cs`.

## حالة التحقق والنشر

في إصلاح روابط الصور نجحت اختبارات الباك ذات الصلة 101/101 بعد تفريغ تتبع EF وإعادة قراءة الصور للسائق ومسئول التوصيل. نجحت اختبارات الفلتر وقراءة الصور المباشرة 26/26، بما فيها القراءة دون هيدر باستخدام رابط موقع، ورفض التوقيع المعدل أو المنتهي أو المنقول لملف آخر. نجح بناء API دون أخطاء أو تحذيرات. لا تتطلب هذه الاختبارات المباشرة Docker، ولا تؤكد نشر الإصلاح على الخادم الفعلي.

تم تشغيل `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter FullyQualifiedName~DriverSupportIssuesTests` في شجرة النشر النظيفة: 11/11 ناجحاً. كما نجح اختبار الانحدار الأوسع، 101/101، بالأمر `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriverSupportIssuesTests|FullyQualifiedName~DriverIssueReassignmentContractTests|FullyQualifiedName~DispatcherDashboardTests"`. نجح `dotnet build src/MealMate.Api/MealMate.Api.csproj --no-restore` في الشجرة النظيفة دون أخطاء أو تحذيرات. جُمعت اختبارات HTTP الجديدة، لكن الاختبارين تعذّر تشغيلهما لأن Testcontainers لم يجد Docker endpoint (Docker Desktop غير متاح). لم يتم النشر.
