# الدليل الكامل لدعم السائق ومسؤول التوصيل ونقل العهدة

تاريخ المراجعة: 20 أكتوبر 2026. أصل فلو العهدة: commit `bda25c968c192644c100186fd50e9719d7d40ab2`، مع إضافة قراءة طلب إعادة الإسناد والقرار الصريح الموضحة أدناه. تحقق من نشر migration `AddDriverReassignmentDecisions` قبل ربط الإضافة. رفع Git وحده لا يؤكد نسخة السيرفر المنشورة.

هذا الملف لفريق الموبايل والداشبورد وQA. يغطي بلاغ العنوان غير الواضح، التأخير الشديد، مشاكل الطريق والمركبة، اختيار البديل، واستلام البوكسات ومتابعة الناقص والمتضرر. تفاصيل الحسابات والرواتب وإدارة الأسطول العامة خارج نطاقه.

أمثلة JSON توضيحية لعقود الاتصال وليست سجلات إنتاج. استبدل القيم بين `<...>` بمعرّفات وبيانات حقيقية. جميع المسارات أدناه تبدأ بـ `/api/v1` ما عدا مسارات SignalR.

## 1. قواعد الاتصال والمعرّفات

```http
Authorization: Bearer <access-token>
Accept-Language: ar
Content-Type: application/json
```

استخدم `Accept-Language: en` للإنجليزية. لا ترسل أسماء عربية بدل القيم البرمجية مثل `VehicleBreakdown` و`Received`. رد النجاح يعيد القيمة مباشرة؛ لا يوجد غلاف عام باسم `data` أو `result`. التاريخ بصيغة UTC عند وجود اللاحقة `Utc`؛ حوّله للعرض فقط.

| المعرّف | المقصود والاستخدام |
|---|---|
| `boxId` في مسارات السائق | `TripStop.Id` من خريطة/طلبات السائق، وليس كود العرض ولا `DailyOrderId` |
| `tripStopId` | نفس معرّف بند الرحلة المستخدم في قائمة نقل العهدة |
| `driverId` و`replacementDriverId` | معرّف `DriverProfile`، وليس معرّف حساب المستخدم |
| `issueId` | معرّف البلاغ الذي تعيده قائمة الدعم؛ المسارات القديمة قد تعيد معرّف توقف متعذر |
| `requestId` | معرّف البلاغ المحفوظ الناتج عن طلب الاستبدال؛ يصلح لفتح تفاصيل الدعم |
| `handoverId` | معرّف عملية نقل العهدة المحفوظة |
| `tripId` في نقل العهدة | الرحلة الأصلية، ويظل محفوظًا بعد الاكتمال |
| `replacementTripId` | رحلة جديدة للمستلم فقط؛ قد يكون `null` حتى بعد الاكتمال إذا لم يُستلم أي بوكس |
| `manifestCode` | كود القائمة المحفوظ للبوكس؛ لا تختصره ولا تستخرج لاحقته |

السيرفر يستخرج الهوية من التوكن والحساب والملف المحفوظ. `driverId` في query للتوافق حيث يوجد؛ تغييره لا يمنح ملكية. مسؤول التوصيل يعمل في نطاق المطعم الموجود بهويته. الصلاحيات الإدارية الأوسع يحكمها دور `Admin`.

## 2. المسؤوليات والشاشات

| الطرف/الشاشة | يعرض | الإجراء |
|---|---|---|
| السائق الأصلي: الإبلاغ | البوكس، نوع المشكلة، الوصف، الخطورة، الصور | `report-issue` |
| السائق الأصلي: التأخير | دقائق التأخير والسبب | `report-delay` |
| السائق الأصلي: طلب بديل | السبب، الملاحظة، موقع السائق الفعلي | `request-reassignment` |
| مسؤول التوصيل: مركز الدعم | العدادات، المناطق، الحالات، البحث، التاريخ | قائمة البلاغات |
| مسؤول التوصيل: تفاصيل البلاغ | السائق، المركبة، المشكلة، الصور، بيانات الرحلة والحل | تفاصيل البلاغ، فتح المرشحين أو الحل |
| مسؤول التوصيل: اختيار البديل | المرشحون والبيانات المتاحة فعليًا | إنشاء نقل عهدة |
| السائق البديل: المهمة | موقع الاستلام ومصدره ووقته، الطرف الآخر، قائمة البوكسات | الوصول، تسجيل النتائج، التأكيد |
| السائق الأصلي: متابعة العهدة | حالة العملية ونتائج البوكسات | قراءة التحديثات، إلغاء آمن قبل تسجيل النتائج |
| مسؤول التوصيل: متابعة الاستلام | المستلم والناقص والمتضرر وحالات المتابعة | إرجاع مؤكد للمطبخ ثم حل البلاغ |

تعطيل الأزرار في الواجهة لتحسين الاستخدام؛ السيرفر يعيد التحقق من الصلاحية والحالة عند كل طلب.

## 3. الفلو الكامل

1. السائق يفتح البوكس من رحلته الفعلية، ويبلغ عن المشكلة المناسبة.
2. في مشكلة العنوان يمكن لمسؤول التوصيل مراجعة البيانات والتواصل ثم حل البلاغ دون بديل إن لم يحتج الأمر لنقل عهدة.
3. التأخير يسجّل بلاغًا للتوزيع؛ لا يعني تلقائيًا أن البوكس فشل أو أن السائق استُبدل.
4. عند الحاجة لبديل يرسل السائق طلب الاستبدال وموقعه الحقيقي إن توفر.
5. مسؤول التوصيل يفتح البلاغ والمرشحين ثم يختار بديلًا. التعيين ينشئ عملية `Assigned` فقط، وتبقى العهدة لدى الأصلي.
6. البديل يفتح تفاصيل المهمة، ويتجه إلى إحداثيات الاستلام، وليس إلى المطبخ أو عنوان العميل تلقائيًا.
7. البديل يسجل الوصول ثم كل بوكس كـ `Received` أو `Missing` أو `Damaged`.
8. بعد تسجيل جميع البنود يؤكد البديل العملية. تنتقل البوكسات `Received` فقط إلى رحلة البديل، وتُحفظ متابعة حقيقية للناقص والمتضرر.
9. يعرض التطبيق ملخص السيرفر ويعيد تحميل الرحلة الجديدة. لا يضع البوكسات الناقصة أو المتضررة في قائمة التوصيل.
10. مسؤول التوصيل يتابع البوكسات المحجوزة، ويسجل الإرجاع للمطبخ فقط عند التأكد الفعلي، ثم يحل البلاغ عند زوال الموانع.

## 4. واجهات السائق الأصلي

### 4.1 الإبلاغ عن مشكلة

`POST /driver/orders/{boxId}/report-issue`

```json
{
  "issueType": "AddressIssue",
  "description": "العنوان غير واضح ويحتاج مراجعة",
  "severity": "High",
  "evidencePhotoStorageKey": null,
  "evidencePhotoStorageKeys": []
}
```

النجاح `200` وقيمته JSON string مباشرة:

```json
"<issue-guid>"
```

| الحقل | القاعدة |
|---|---|
| `issueType` | قيمة من الجدول التالي؛ تُطبّع دون حساسية لحالة الأحرف |
| `description` | اختياري؛ أقصى طول 250 حرفًا |
| `severity` | `Low` أو `Medium` أو `High`؛ الافتراضي `Medium` |
| مفاتيح الصور | مفاتيح ملفات مرفوعة فعلًا، وليست روابط أو base64؛ تُدمج القائمتان وتُزال التكرارات، بحد أقصى 3 صور مميزة |

هذا الطلب لا يقبل إحداثيات في جسمه الحالي. لا تضف حقول GPS متوقعة من الواجهة دون تغيير العقد. مسار طلب الاستبدال التالي يقبل الإحداثيات.

| `issueType` | المعنى |
|---|---|
| `AddressIssue` | عنوان خاطئ أو غير واضح |
| `SevereDelay` | تأخير شديد |
| `RoadIssue` | مشكلة في الطريق |
| `VehicleBreakdown` | عطل بالمركبة |
| `TrafficAccident` | حادث سير/طارئ |
| `SevereWeather` | سوء الأحوال الجوية |
| `CustomerUnavailable` | العميل غير متاح |
| `CustomerRefusal` | رفض الاستلام |
| `DamagedBox` | بوكس تالف |
| `Other` | مشكلة أخرى |

### 4.2 تسجيل التأخير

`POST /driver/orders/{boxId}/report-delay`

```json
{ "delayMinutes": 30, "reason": "ازدحام شديد في الطريق" }
```

`delayMinutes` بين 1 و1440. السبب اختياري، لكن التنفيذ يحسب حد 250 حرفًا على الوصف المركب كله: `Delay reported: {minutes} minutes. Reason: {reason}`؛ لذلك المساحة المتاحة للسبب أقل من 250. يتطلب تعيينًا نشطًا للسائق على الرحلة. لا يقبل البوكس الذي سُلّم أو أُعيد للمطبخ. النتيجة `200` وقيمة JSON مباشرة `true`.

### 4.3 طلب سائق بديل

`POST /driver/orders/{boxId}/request-reassignment`

```json
{
  "reason": "VehicleBreakdown",
  "notes": "المركبة متوقفة والبوكسات ما زالت في العهدة",
  "latitude": 29.3,
  "longitude": 47.9
}
```

الأسباب: `VehicleBreakdown`، `TrafficAccident`، `MedicalOrPersonalEmergency`، `DeviceFailure`، `OtherOperationalReason`. الملاحظات اختيارية بحد 250 حرفًا. GPS اختياري، لكن يجب إرسال الزوج كاملًا أو حذفه كاملًا: خط العرض بين -90 و90 والطول بين -180 و180.

```json
{
  "requestId": "<issue-guid>",
  "boxId": "<trip-stop-guid>",
  "boxCode": "<display-code>",
  "status": "ReassignmentRequested",
  "message": "<localized-message>",
  "requestedAtUtc": "2026-10-20T10:00:00Z"
}
```

لا يحرر هذا الطلب العهدة ولا ينشئ تعيينًا للبديل. الطلب النشط المكرر له خطأ تعارض؛ اقرأ الحالة قبل إنشاء طلب جديد. احتفظ بمعرّف البلاغ الأصلي ولا تستبدله بمعرّف السائق البديل.

## 5. مركز دعم مسؤول التوصيل

### مرحلة انتظار القرار واسترجاع طلب الإرسال

هذه المرحلة متاحة قبل وجود أي `handoverId`. طلب الإرسال الحالي لا يتغير:
`POST /driver/orders/{boxId}/request-reassignment` يعيد `requestId` و`ReassignmentRequested`.

عند فقد الرد، افتح التطبيق أو شاشة البوكس واستدعِ:

`GET /driver/reassignment-requests?boxId=<trip-stop-guid>&page=1&pageSize=20`

يرجع مصفوفة طلبات السائق فقط مرتبة من الأحدث للأقدم. `boxId` اختياري؛ حذفه يسترجع طلبات السائق عبر البوكسات، بما فيها الحالات المنتهية، بحد 100 عنصر للصفحة. لا تعتمد على معرف مفقود في رد POST: ابحث بمعرف البوكس المعروف. القائمة الفارغة تعني عدم وجود طلب ظاهر في هذه القراءة؛ إذا أعدت POST والطلب الأول كان قد حُفظ فستحصل على تعارض الطلب النشط، ثم أعد GET. لا تحوّل هذا التعارض إلى نجاح محلي دون قراءة الطلب المحفوظ.

تفاصيل طلب محدد: `GET /driver/reassignment-requests/{requestId}`. بعد نقل العهدة تظل القراءة متاحة للسائق المبلّغ الأصلي، حتى لو لم يعد يملك تعيين الرحلة. لا تتيح هذه الواجهة بيانات الطلب للسائق البديل؛ البديل يستخدم واجهات `support-handovers` الخاصة به.

مثال عنصر القراءة:

```json
{
  "requestId": "<complaint-guid>", "boxId": "<trip-stop-guid>", "boxCode": "<persisted-code>",
  "status": "ReassignmentRequested", "reason": "VehicleBreakdown", "notes": "المركبة متوقفة",
  "requestedAtUtc": "2026-10-20T10:00:00Z", "decision": null, "decisionNotes": null,
  "decidedByUserId": null, "decidedAtUtc": null, "handoverId": null, "handoverStatus": null,
  "replacementDriverId": null, "replacementTripId": null, "latitude": 29.3, "longitude": 47.9
}
```

البلاغات القديمة قد لا تحتوي `reason` أو `notes` المنفصلين أو ربط بوكس غير ملتبس؛ ترجع هذه الحقول `null` بدل اختلاقها. يبقى الوصف الأصلي في تفاصيل البلاغ. `boxId` و`boxCode` قابلان لـnull في هذه الحالة.

| `status` | ما تعرضه شاشة السائق |
|---|---|
| `ReassignmentRequested` | في انتظار قرار مسؤول التوصيل، لا يوجد نقل عهدة بعد |
| `Accepted` | وافق المسؤول؛ `handoverId` قد يظل null حتى اختيار البديل |
| `Rejected` | رفض المسؤول؛ اعرض `decisionNotes` |
| `Closed` | طلب أُغلق دون handover؛ اقرأ `decision` لمعرفة القرار إن كان محفوظًا، ولا تختلق قرارًا للطلبات القديمة |

`decision` مستقلة عن دورة حياة نقل العهدة: بعد إنشاء العملية اعرض `handoverStatus` (`Assigned/Arrived/Receiving/Completed/Cancelled`) أيضًا. الإلغاء لا يمحو قرار القبول ولا يعيد الطلب تلقائيًا إلى انتظار القرار.

مسؤول التوصيل يقرأ نفس الكائن عبر `GET /dispatcher/reassignment-requests/{requestId}` ضمن نطاق المطعم، ثم يقرر:

- `POST /dispatcher/reassignment-requests/{requestId}/accept` مع `{ "notes": "تمت مراجعة الطلب" }`؛ الملاحظة اختيارية حتى 500 حرف.
- `POST /dispatcher/reassignment-requests/{requestId}/reject` مع `{ "notes": "يمكن استكمال التوصيل بعد معالجة المشكلة" }`؛ سبب الرفض مطلوب من 3 إلى 500 حرف.

كلاهما يعيد `200` بكائن القراءة أعلاه بعد تحديث القرار والمنفّذ والتوقيت. القبول لا ينشئ رحلة أو handover ولا ينقل العهدة. يلي القبول اختيار البديل بالمسارات المعتادة. لحفظ التوافق، التعيين الناجح مباشرة بالـassign/reassign يقبل الطلب تلقائيًا في نفس المعاملة إذا لم يوجد قرار سابق، ويسجل التدقيق والإشعار.

القرار نهائي: تكرار نفس القرار ونفس الملاحظة المنظفة يرجع النتيجة الحالية دون إشعار/تدقيق مكرر؛ تغيير أي منهما يعيد `REASSIGNMENT_ALREADY_DECIDED`. الرفض يغلق البلاغ ويمنع إنشاء handover له، ولا يغيّر عهدة السائق. يمكن إنشاء طلب جديد لاحقًا إن استوفت الحالة شروط الإرسال. لا تستخدم `/resolve` بدل قرار طلب ما زال معلّقًا بلا handover؛ يرجع `REASSIGNMENT_DECISION_REQUIRED`. بعد القبول يمكن حل البلاغ دون handover إذا عولجت المشكلة ولم يعد البديل مطلوبًا؛ تصبح `status` مساوية `Closed` مع بقاء `decision` مساوية `Accepted`. لا يمكن إنشاء handover لبلاغ مغلق.

أخطاء الإضافة: `REASSIGNMENT_FORBIDDEN` (403)، `REASSIGNMENT_NOT_FOUND` (404)، `REASSIGNMENT_DECISION_INVALID` (400)، و`REASSIGNMENT_ALREADY_DECIDED`، `REASSIGNMENT_ALREADY_CLOSED`، `REASSIGNMENT_HANDOVER_EXISTS`، `REASSIGNMENT_STOP_UNAVAILABLE`، `REASSIGNMENT_CUSTODY_CHANGED`، `REASSIGNMENT_DECISION_REQUIRED` (409). إنشاء handover لطلب مرفوض يعيد `REASSIGNMENT_REJECTED` أو تعارض البلاغ المغلق للبلاغات القديمة.

القرار يرسل إشعارًا محفوظًا للسائق وحدث البلاغ المعتاد `driver-support-issue-updated` / `driver-issue-updated` بقيمة `eventName` تساوي `reassignment-accepted` أو `reassignment-rejected`. عنده أعد قراءة الطلب. حدث الإرسال `reassignment-requested` نفسه كافٍ لتحديث مركز الدعم، ولا يلزم انتظار إنشاء handover.

شروط الإرسال: `PickedUp` فقط، تعيين نشط، هوية وحساب صحيحان، لا handover نشط ولا متابعة مفتوحة، والرحلة غير Completed/Cancelled. شروط البلاغ العام لا تفرض `PickedUp` تحديدًا لكنها تتطلب تعيينًا نشطًا والحمايات المشتركة. `RoadIssue` موجود في قائمة أنواع البلاغ النهائية، لكنه ليس سببًا إضافيًا في قائمة أسباب طلب الاستبدال؛ استخدم السبب التشغيلي الملائم من قائمة القسم 4.3.

### 5.1 القائمة والفلاتر

`GET /dispatcher/support/issues`

| Query | المعنى |
|---|---|
| `restaurantId` | اختياري؛ لا يتجاوز نطاق الهوية |
| `status` | الافتراضي `Open`؛ استخدم `InProgress` أو مرادفه `InResolution` أو `Resolved` أو `All` |
| `area` | `areaKey` القادم من السيرفر؛ `all` لكل المناطق |
| `search` | يبحث في كود البوكس واسم/كود السائق والتصنيف والعنوان |
| `datePreset` | `Today`، `Last7Days`، `Last30Days`، `Custom` |
| `fromDateUtc`, `toDateUtc` | نطاق مخصص؛ أرسل ISO UTC واضحًا |
| `pageNumber` | يبدأ من 1 |
| `pageSize` | الافتراضي 20، الأقصى 50 |

العدادات تتأثر بالتاريخ ولا تتأثر بفلتر الحالة. شرائح المناطق لا تتأثر بالمنطقة المختارة. القائمة تتأثر بالحالة والمنطقة والبحث والتاريخ؛ لذلك لا تساوِ عدد نتائج الصفحة بعدادات المركز.

```json
{
  "counters": { "openCount": 1, "inProgressCount": 0, "resolvedCount": 0, "inResolutionCount": 0 },
  "areaChips": [{ "areaKey": "all", "name": "كل المناطق", "count": 1, "isSelected": true, "areaName": "كل المناطق", "issueCount": 1 }],
  "issues": [{
    "id": "<issue-guid>", "issueId": "<issue-guid>", "boxCode": "<box-code>", "issueCode": "<issue-code>",
    "title": "<persisted-title>", "category": "AddressIssue", "issueCategory": "<localized-category>",
    "badgeText": "<localized-badge>", "badgeColor": "<badge-color>", "issueCategoryColor": "<color>",
    "reportedTimeText": "<relative-time>", "timeAgo": "<relative-time>", "createdAtUtc": "2026-10-20T10:00:00Z",
    "driverId": "<driver-profile-guid>", "driverCode": null, "driverName": "<persisted-name>", "driverAvatarUrl": null,
    "area": null, "vehicleText": null, "vehicleInfo": null,
    "priority": "High", "priorityLabel": "<localized-priority>", "priorityColor": "<color>", "status": "Open", "statusLabel": "<localized-status>"
  }],
  "pagination": { "pageNumber": 1, "pageSize": 20, "totalItems": 1, "totalPages": 1, "hasPreviousPage": false, "hasNextPage": false },
  "selectedArea": null, "areas": [{ "areaKey": "all", "name": "كل المناطق", "count": 1, "isSelected": true, "areaName": "كل المناطق", "issueCount": 1 }],
  "selectedStatus": "Open"
}
```

### 5.2 تفاصيل البلاغ

`GET /dispatcher/support/issues/{issueId}`

```json
{
  "issueId": "<issue-guid>", "title": "<title>", "category": "VehicleBreakdown", "categoryLabel": "<label>", "categoryColor": null,
  "createdAtUtc": "2026-10-20T10:00:00Z", "reportedTimeText": "<relative-time>",
  "metadata": { "boxCode": null, "area": null, "affectedBoxesCount": null, "affectedBoxesText": null, "priority": null, "priorityText": "<label>", "priorityColor": null },
  "driver": {
    "driverId": "<profile-guid>", "driverCode": null, "fullName": "<persisted-name>", "avatarUrl": null,
    "status": "<status>", "statusText": "<label>", "statusColor": "<color>", "phoneNumber": null, "vehicleInfo": null,
    "vehicle": { "type": null, "model": null, "color": null, "plateNumber": null }
  },
  "description": "<persisted-description>", "evidencePhotos": [],
  "tripInfo": {
    "customerName": null, "mealsCount": null, "mealsCountText": "<label>", "expectedDeliveryFromUtc": null,
    "expectedDeliveryToUtc": null, "expectedDeliveryTimeText": null, "pickupLocation": null, "deliveryAddress": null, "mealSummary": null
  },
  "status": "Open", "statusLabel": "<label>", "resolution": null, "vehicleInfo": null
}
```

كل صورة في `evidencePhotos`: `id`, `url?`, `thumbnailUrl?`, `uploadedAtUtc`. عند الحل، `resolution` يحتوي `notes`, `resolvedAtUtc?`, `resolvedById?`, `resolvedByName?`. الاسم من المستخدم المحفوظ أو `null`؛ لا تعرض اسم موظف ثابتًا.

### 5.3 المرشحون

`GET /dispatcher/support/issues/{issueId}/candidates?pageNumber=1&pageSize=20`

الجذر: `summary`, `issue` (نفس نوع ملخص البلاغ)، `currentDriver` وقد يكون `null`، `candidates`، `pagination` بنفس حقول ترقيم قائمة الدعم.

حقول `summary` و`issue`: `issueId`, `title`, `category`, `categoryLabel`, `categoryColor`, `createdAtUtc?`, `reportedTimeText`, `boxCode?`, `area?`, `affectedBoxesCount?`, `affectedBoxesText`, `priority?`, `priorityText`, `priorityColor?`, `status`. علامة `?` تعني nullable؛ و`categoryColor` قد يرجع `null` فعليًا عند غياب تصنيف الخطورة رغم تعريفه النصي غير القابل لـnull في DTO الحالي.

حقول `currentDriver`: `driverId?`, `driverCode?`, `fullName?`, `avatarUrl?`, `status`, `statusText`, `statusColor`, `unavailabilityReason?`, `unavailabilityReasonText?`.

مثال عنصر في `candidates`:

```json
{
  "driverId": "<profile-guid>", "driverCode": null, "fullName": "<persisted-name>", "avatarUrl": null,
  "status": "Available", "statusText": "متاح", "statusColor": "<color>", "rating": null,
  "activeOrdersCount": 0, "area": null, "isSameArea": null, "distanceKm": null, "distanceText": null,
  "estimatedArrivalMinutes": null, "estimatedArrivalText": null, "vehicleInfo": null,
  "lastLocationUpdatedAtUtc": null, "recommendationRank": 1
}
```

البديل يجب أن يكون معتمدًا ومتاحًا، حسابه نشط، مرتبطًا بالمطعم، لديه جلسة عمل نشطة، بلا تعيين عمل حالي أو حجز بديل نشط، ووثائقه غير منتهية عندما توجد تواريخ انتهاء. السيرفر يعيد الفحص عند التعيين؛ ظهور المرشح لا يضمن بقاءه متاحًا.

المسافة حساب من نقاط محفوظة عند توفرها. لا تستنتج وقت وصول أو تقييمًا أو منطقة من قيم ناقصة. عرض `null` يعني أن البيانات غير متاحة. `recommendationRank` ترتيب عرض وليس قبولًا تلقائيًا.

## 6. إنشاء ومتابعة نقل العهدة

### 6.1 إنشاء العملية الموصى به

`POST /dispatcher/support-handovers/issues/{issueId}/assign`

```json
{ "replacementDriverId": "<profile-guid>", "idempotencyKey": "<stable-client-operation-key>", "notes": "<optional-note>" }
```

المفتاح مطلوب، أقصى طوله 100 بعد التنظيف؛ الملاحظة اختيارية بحد 500 حرف. خزّن نفس المفتاح لإعادة محاولة نفس العملية. استعماله مع بلاغ أو بديل أو ملاحظات مختلفة يسبب `IDEMPOTENCY_KEY_REUSED`. الرد `200` بكائن نقل العهدة في القسم 8.

التوافق القديم: `POST /dispatcher/support/issues/{issueId}/reassign` يقبل `replacementDriverId`, `notes?` دون مفتاح من العميل. الرد يحتوي:

`success`, `issueId`, `previousDriverId`, `issueStatus`, `replacementDriver` (بنفس نوع سائق تفاصيل البلاغ)، `reassignedAtUtc`, `message`, `replacementDriverId`, `replacementDriverCode?`, `driverName?`, `handoverId?`, `handoverStatus?`.

اسم `reassign` أو `reassignedAtUtc` لا يعني انتقال العهدة فورًا؛ اعتمد `handoverStatus` وتأكيد العملية.

### 6.2 القراءة والاسترجاع

| الطرف | المسار |
|---|---|
| مسؤول التوصيل | `GET /dispatcher/support-handovers?status=Assigned&restaurantId=<guid>&page=1&pageSize=20` |
| مسؤول التوصيل | `GET /dispatcher/support-handovers/{handoverId}` |
| السائق | `GET /driver/support-handovers?status=active` |
| السائق | `GET /driver/support-handovers/{handoverId}` |

قائمة مسؤول التوصيل تعيد `{ items, totalCount, pageNumber, pageSize }`؛ حد الصفحة الأقصى 100. فلتر `status` فيها حالة محددة، وليس كلمة `active`. قائمة السائق مصفوفة مباشرة، و`active` يعرض العمليات غير المنتهية. السائق يقرأ العملية فقط إذا كان الأصلي أو البديل وبحساب صالح.

### 6.3 موقع الاستلام

`pickupLatitude`, `pickupLongitude` من زوج صحيح كامل في البلاغ أو نقطة تتبع فعلية واحدة للسائق الأصلي. `pickupLocationSource` يوضح `Complaint` أو `DriverTracking` ومعه `pickupLocationRecordedAtUtc`.

لا تستخدم إحداثيات `TripStop` الخاصة بوجهة العميل كمكان استلام البوكسات، ولا ترسل البديل إلى المطبخ بدل السائق المتعطل. عند غياب مصدر حقيقي يرجع `PICKUP_LOCATION_UNAVAILABLE`. اعرض وقت تسجيل الموقع؛ لا تصفه بأنه موقع حي بلا دليل أحدث.

## 7. إجراءات السائق البديل

### 7.1 الوصول

`POST /driver/support-handovers/{handoverId}/arrival`

```json
{ "latitude": 29.3, "longitude": 47.9 }
```

يسجل الوصول البديل وحده، بإحداثيات صحيحة. الحالة تتحول من `Assigned` إلى `Arrived`. لا يوجد حد مسافة مفروض حاليًا لإثبات القرب؛ لا تعرض للمستخدم أن السيرفر تحقق من نصف قطر معين.

### 7.2 تسجيل البوكسات

`POST /driver/support-handovers/{handoverId}/boxes/scan`

```json
{ "code": "<actual-barcode>", "status": "Received", "method": "Barcode", "followUpNote": null }
```

```json
{ "code": "<exact-manifest-code>", "status": "Missing", "method": "ManualCode", "followUpNote": "البوكس غير موجود عند المطابقة" }
```

| الحقل | القاعدة |
|---|---|
| `code` | الرمز الكامل للبوكس الموجود في قائمة هذه العملية فقط |
| `method` | `Barcode` أو `ManualCode`؛ الافتراضي `Barcode` |
| `status` | `Received` أو `Missing` أو `Damaged`؛ الافتراضي `Received` |
| `followUpNote` | إلزامي للناقص والمتضرر؛ أقصى 500 حرف، واختياري للمستلم |

الباركود يجب أن يكون محفوظًا وغير ملغى أو مؤرشف، ومخصصًا لبوكس واحد في القائمة. الإدخال اليدوي يطابق الكود بعد تطبيع السيرفر؛ الكود الملتبس بين بوكسين يُرفض ويحتاج باركودًا فريدًا. تكرار نفس النتيجة أثناء `Arrived` أو `Receiving` آمن، لكن تغيير النتيجة بعد تسجيلها يُرفض. بعد `Completed` اقرأ التفاصيل بدل إعادة المسح؛ شرط مرحلة الاستلام يرفض المسح في الحالة المنتهية. لا تعرض إمكانية تعديل نتيجة مسجلة محليًا.

### 7.3 التأكيد النهائي

`POST /driver/support-handovers/{handoverId}/complete` بلا جسم.

فعّل زر التأكيد عندما يكون الوصول مسجلًا و`pendingCount = 0` والحالة تسمح بالتأكيد. جميع البوكسات يجب أن تحمل نتيجة؛ `Missing` و`Damaged` نتائج مسجلة وليستا استلامًا صالحًا للتوصيل.

النتيجة `200` بكائن العملية النهائي. الحفظ ذري: البوكسات المستلمة وتعيين البديل والمتابعات والإشعارات تُحفظ معًا. التكرار يرجع نفس النتيجة المحفوظة، ولا ينشئ رحلة أو تعيينًا ثانيًا. لا تنقل أي بوكس في مخزن التطبيق قبل نجاح التأكيد أو قراءة نتيجة اكتمال محفوظة.

## 8. نموذج الاستجابة المشترك

```json
{
  "handoverId": "<handover-guid>", "issueId": "<complaint-guid>", "tripId": "<original-trip-guid>",
  "originalDriverId": "<original-profile-guid>", "replacementDriverId": "<replacement-profile-guid>", "restaurantId": "<restaurant-guid>",
  "status": "Completed", "pickupLatitude": 29.3, "pickupLongitude": 47.9,
  "assignedAtUtc": "2026-10-20T10:00:00Z", "arrivedAtUtc": "2026-10-20T10:10:00Z", "completedAtUtc": "2026-10-20T10:15:00Z",
  "replacementTripId": "<replacement-trip-guid>", "receivedCount": 1, "missingCount": 1, "damagedCount": 0, "pendingCount": 0,
  "pickupLocationSource": "Complaint", "pickupLocationRecordedAtUtc": "2026-10-20T09:55:00Z",
  "issueTitle": "<persisted-title>", "issueCategory": "VehicleBreakdown", "issueDescription": "<persisted-description>",
  "tripCode": "<original-trip-code>", "originalDriverName": "<persisted-name>", "originalDriverPhone": null,
  "replacementDriverName": "<persisted-name>", "replacementDriverPhone": null,
  "boxes": [
    { "tripStopId": "<received-stop-guid>", "manifestCode": "<received-code>", "status": "Received", "receivedAtUtc": "2026-10-20T10:12:00Z", "recordedAtUtc": "2026-10-20T10:12:00Z", "followUpNote": null, "followUpId": null, "followUpStatus": null },
    { "tripStopId": "<missing-stop-guid>", "manifestCode": "<missing-code>", "status": "Missing", "receivedAtUtc": null, "recordedAtUtc": "2026-10-20T10:13:00Z", "followUpNote": "غير موجود", "followUpId": "<hold-guid>", "followUpStatus": "Active" }
  ]
}
```

`recordedAtUtc` لأي نتيجة مسجلة؛ `receivedAtUtc` للمستلم فقط. أسماء وأرقام الأطراف والبيانات الوصفية قد تكون `null`. لا تولّد أسماء أو هواتف أو مواعيد بديلة. مجموع العدادات يساوي عدد عناصر `boxes`.

في البلاغ القديم قد يُرقّى التوقف إلى Complaint حقيقي؛ لذلك احتفظ بمعرّف البطاقة الأصلي وبـ`issueId` الذي تعيده العملية، ولا تفترض أنهما متساويان دائمًا.

## 9. الحالات والإلغاء والعهدة

| الحالة | المعنى | الإجراءات المعتادة |
|---|---|---|
| `Assigned` | البديل مختار والعهدة لدى الأصلي | وصول البديل، إلغاء قبل وجود دليل |
| `Arrived` | الوصول محفوظ ولم تسجل نتائج بعد | مسح/إدخال النتائج، إلغاء إن لم توجد نتائج |
| `Receiving` | نتيجة بوكس واحدة على الأقل محفوظة | استكمال الباقي ثم التأكيد؛ الإلغاء ممنوع |
| `Completed` | التسوية محفوظة | قراءة الملخص، متابعة الناقص والمتضرر |
| `Cancelled` | ألغيت قبل نتائج الاستلام | قراءة التاريخ؛ لا تؤكدها |

`POST /dispatcher/support-handovers/{handoverId}/cancel` أو `POST /driver/support-handovers/{handoverId}/cancel`:

```json
{ "reason": "<optional-reason>" }
```

السبب اختياري وحده 500 حرف. يسمح لمسؤول النطاق أو أحد الطرفين وفق الحساب والصلاحية. وجود أي نتيجة استلام يمنع الإلغاء، حتى لو كانت `Missing`. تبقى الملاحظة الأصلية ودليل العملية محفوظين. لا تستعمل إلغاءً محليًا بديلًا عن نجاح السيرفر.

لا يعاد فتح `Delivered` أو `ReturnedToKitchen`. المسارات القديمة للتعيين والاستلام والرحلة والمزامنة لا يجوز أن تتجاوز نقل عهدة نشطًا أو حجز متابعة.

## 10. متابعة الناقص والمتضرر وحل البلاغ

بعد الاكتمال تظل هذه البوكسات على الرحلة الأصلية وتُربط بـ`DeliveryHold`. لا تصبح قابلة للتوصيل مع البديل. يظهر لمسؤول التوصيل إشعار متابعة فعلي.

عند التأكد من رجوع بوكس إلى المطبخ:

`POST /dispatcher/support-handovers/{handoverId}/boxes/{tripStopId}/follow-up/return-to-kitchen`

```json
{ "manifestCode": "<exact-manifest-code-or-active-barcode>", "resolutionNote": "تم التحقق من استلام المطبخ للبوكس فعليًا" }
```

مطلوب عملية مكتملة، نتيجة `Missing` أو `Damaged`، متابعة محفوظة، وبوكس ما زال في الرحلة الأصلية. الملاحظة من 5 إلى 500 حرف. الكود اليدوي يجب أن يكون فريدًا في القائمة أو استخدم الباركود الحقيقي. يرجع كائن العملية بعد التسوية.

يحفظ السيرفر التدقيق وهوية المنفذ والتوقيت، يجعل حالة التوقف `ReturnedToKitchen` والمتابعة `Resolved`، ويترك نتيجة `Missing`/`Damaged` الأصلية دون تعديل. تكرار الدليل والملاحظة نفسيهما آمن، واختلافهما بعد التسوية يسبب تعارضًا. لا يوجد في هذا الفلو زر لتحويل الناقص تلقائيًا إلى `Received` أو وعد بتعويض مالي.

حل البلاغ:

`POST /dispatcher/support/issues/{issueId}/resolve`

```json
{ "resolutionNotes": "تمت معالجة المشكلة وإغلاق جميع المتابعات" }
```

الملاحظات مطلوبة بين 3 و500 حرف. يمنع الحل عند نقل عهدة نشط أو متابعة مفتوحة مرتبطة بالبلاغ. الرد:

```json
{
  "issueId": "<issue-guid>", "status": "Resolved", "statusLabel": "<localized-status>",
  "resolution": { "notes": "<saved-notes>", "resolvedAtUtc": "2026-10-20T11:00:00Z", "resolvedById": "<user-guid>", "resolvedByName": null },
  "resolvedAtUtc": "2026-10-20T11:00:00Z", "message": "<localized-message>", "driverId": "<original-profile-guid>"
}
```

## 11. الأخطاء واستراتيجية الواجهة

```json
{
  "type": "about:blank", "title": "Conflict", "status": 409,
  "detail": "<localized-description>",
  "instance": "/api/v1/driver/support-handovers/<handover-guid>/complete",
  "code": "HANDOVER_RECEIPTS_INCOMPLETE"
}
```

تعامل مع `code` كمعرّف برمجي ومع `detail` كنص مترجم؛ قد تختلف `type` و`title` بحسب مُنشئ ProblemDetails. `400` إدخال غير صالح، `401` تسجيل الدخول، `403` صلاحية، `404` مورد غير متاح، `409` تعارض حالة. أخطاء ربط/تحقق ASP.NET قد تحتوي `errors` أيضًا.

| الكود | تصرف الواجهة |
|---|---|
| `DISPATCHER_SCOPE_REQUIRED`, `DISPATCHER_SCOPE_FORBIDDEN`, `HANDOVER_FORBIDDEN` | منع الإجراء؛ تحقق من الدور والنطاق |
| `HANDOVER_NOT_FOUND` | إغلاق التفاصيل القديمة وتحديث القائمة |
| `PICKUP_LOCATION_UNAVAILABLE` | بيان غياب موقع موثوق؛ لا تستبدله بموقع العميل |
| `REPLACEMENT_DRIVER_UNAVAILABLE`, `REPLACEMENT_DRIVER_BUSY`, `REPLACEMENT_ACCOUNT_INACTIVE` | تحديث المرشحين واختيار بديل صالح |
| `HANDOVER_ALREADY_ACTIVE`, `HANDOVER_PENDING` | افتح العملية القائمة بدل إنشاء أخرى |
| `IDEMPOTENCY_KEY_REUSED` | لا تعدّل حمولة عملية تحمل نفس المفتاح |
| `HANDOVER_ARRIVAL_REQUIRED` | تسجيل الوصول أولًا |
| `MANIFEST_BOX_NOT_FOUND`, `BARCODE_INVALID`, `BARCODE_NOT_ASSIGNED` | رفض الرمز وإظهار بند القائمة الصحيح |
| `MANIFEST_CODE_AMBIGUOUS`, `MANIFEST_BARCODE_AMBIGUOUS` | استخدم هوية بوكس فريدة؛ لا تختر أول تطابق |
| `BOX_ALREADY_RECORDED` | تحديث التفاصيل؛ لا تستبدل النتيجة |
| `FOLLOW_UP_REQUIRED` | طلب ملاحظة للناقص أو المتضرر |
| `HANDOVER_RECEIPTS_INCOMPLETE` | عرض البنود التي ما زالت `Pending` |
| `HANDOVER_RECEIPT_EXISTS` | منع الإلغاء مع وجود دليل محفوظ |
| `TERMINAL_BOX_CANNOT_TRANSFER`, `TRIP_TERMINAL` | تحديث الرحلة وعدم إعادة فتح الحالات النهائية |
| `SUPPORT_FOLLOWUP_PENDING` | فتح المتابعات قبل حل البلاغ |
| `FOLLOW_UP_CODE_MISMATCH` | إعادة التحقق من رمز البوكس |
| `FOLLOW_UP_CUSTODY_CHANGED`, `FOLLOW_UP_STOP_TERMINAL` | منع الإرجاع ومراجعة الحالة الفعلية |
| `FOLLOW_UP_ALREADY_RESOLVED` | قراءة التسوية المحفوظة؛ لا تعِد حفظ دليل مختلف |

هذه أبرز الأخطاء وليست بديلًا عن التعامل العام مع أي `code` جديد. عند انقطاع الرد لا تفترض فشل العملية؛ اقرأ التفاصيل أولًا ثم أعد نفس الطلب عند الحاجة. اعمل pending/loading لكل زر لمنع النقر المكرر.

## 12. SignalR والاسترجاع بعد الاتصال

| الطرف | مسار Hub | الحدث |
|---|---|---|
| السائق | `/hubs/driver-hub` أو `/hubs/driver` | `support-handover-updated`, `driver-support-issue-updated` |
| مسؤول التوصيل | `/hubs/dispatcher-hub` أو `/hubs/dispatcher` | `driver-support-handover-updated`, `driver-issue-updated` |

نفس توكن الدور المطلوب للـAPI. عضوية المطعم/السائق تأتي من الهوية، ولا يمكن الانضمام لبيانات طرف آخر بمعرّف يرسله العميل.

حمولة تحديث نقل العهدة:

```json
{
  "eventId": "<event-guid>", "occurredAtUtc": "2026-10-20T10:15:00Z", "handoverId": "<guid>",
  "tripId": "<original-trip-guid>", "replacementTripId": "<replacement-trip-guid>", "restaurantId": "<guid>",
  "originalDriverId": "<profile-guid>", "replacementDriverId": "<profile-guid>", "status": "Completed", "eventName": "completed"
}
```

حمولة تحديث البلاغ: `eventId`, `occurredAtUtc`, `issueId`, `restaurantId`, `driverId?`, `category`, `eventName`. الأحداث تنطلق عبر outbox محفوظ ثم نقل PostgreSQL إلى API وSignalR، وليست بثًا عامًا لكل المستخدمين. لا تفترض وصول الحدث مرة واحدة أو وصول كل حدث أثناء الانقطاع.

عند حدث أو reconnect: أزل تكرار `eventId` عند الحاجة، حدّث القائمة والتفاصيل من GET، ثم حدّث شاشة الرحلة. عند cold start استدعِ قائمة نقل العهدة أيضًا. الحدث إشارة لتحديث البيانات؛ كائن GET هو مصدر عرض التفاصيل.

## 13. التوافق مع الاستلام الحالي

فلو استلام المطبخ الموجود مثل point/arrive/manifest/validate-barcode/confirm/pickup-summary يظل خاصًا بالمطبخ. لا تستخدمه بدل عملية استلام من سائق متعطل. البديل يبدأ بـ`support-handovers`، وبعد الاكتمال يعتمد الرحلة الجديدة والبوكسات المستلمة فقط في فلو التوصيل الحالي.

الاسم أو الهاتف أو ETA أو التقييم المفقود يعرض كغير متاح؛ لا يوجد fallback تجاري مصطنع. لا تفترض اكتمال بلاغ الدعم عند اكتمال الاستلام إذا كانت متابعة الناقص أو المتضرر ما زالت مفتوحة.

## 14. قائمة قبول فريق الموبايل والداشبورد وQA

- بلاغ عنوان ووصف وصور صحيحة يظهر في نطاق المطعم الصحيح، وبهوية السائق الأصلي.
- التأخير ينشئ بلاغًا ولا يحرر العهدة أو يُسلم البوكس.
- رفض نوع مشكلة أو خطورة غير مسموحة، ورفض النصوص والصور الزائدة.
- إرسال معرّف حساب بدل DriverProfile أو كود عرض بدل TripStop لا يُستخدم كبديل صحيح.
- مستخدم غير مسجل/دور آخر/سائق ثالث/مطعم آخر لا يقرأ أو يعدّل العملية.
- تعيين البديل لا يغير عهدة الأصلي قبل تأكيد الاستلام.
- غياب موقع فعلي يمنع الإنشاء؛ موقع العميل لا يظهر كموقع الاستلام.
- الوصول ثم Barcode صحيح أو ManualCode فريد يسجل النتيجة مرة واحدة.
- إعادة المسح بنفس النتيجة آمنة؛ نتيجة مختلفة أو رمز خارج القائمة يُرفض.
- لا يمكن تأكيد قائمة بها Pending أو إلغاء عملية بها أي دليل.
- تأكيدان متزامنان ينتجان نفس رحلة البديل دون تكرار التعيين.
- فشل الحفظ في منتصف التأكيد لا يترك نقلًا جزئيًا.
- المستلم فقط ينتقل للبديل؛ الناقص والمتضرر لهما متابعة، ولا يعاد فتح Delivered/ReturnedToKitchen.
- الإرجاع المؤكد للمطبخ يحفظ التدقيق، وإعادته لا تضاعف الآثار ولا تغير نتيجة الاستلام الأصلية.
- حل البلاغ يظل ممنوعًا مع عملية أو متابعة مفتوحة.
- reconnect وcold start يعيدان الحالة من GET دون الاعتماد على وصول SignalR.
- فلو استلام المطبخ والتوصيل الحالي يعملان مع الحمايات الجديدة.

التحقق المنفذ على مرجع الكود المذكور: 407 اختبارًا في DriverDelivery، و3 اختبارات HTTP للصلاحيات، واختبار PostgreSQL يضم التزامن والتراجع وإعادة المحاولة والمتابعة، وبناء API وفحص تطابق EF مع migration. هذه نتائج backend؛ قائمة القبول أعلاه تحتاج تنفيذًا على واجهات الموبايل والداشبورد بعد ربطها.

## 15. مصادر العقد

- `src/MealMate.Api/Controllers/V1/DriverHomeController.cs`
- `src/MealMate.Api/Controllers/V1/DispatcherDashboardController.cs`
- `src/MealMate.Api/Controllers/V1/DriverSupportHandoversController.cs`
- `src/MealMate.Api/Controllers/V1/DispatcherSupportHandoversController.cs`
- `src/MealMate.Api/Common/ApiControllerBase.cs`
- `src/MealMate.Application/Features/DriverDelivery/DriverApp/DriverAppDtos.cs`
- `src/MealMate.Application/Features/DriverDelivery/DriverApp/DriverIssueCatalog.cs`
- `src/MealMate.Application/Features/DriverDelivery/DriverApp/DriverDeliveryOrderCommands.cs`
- `src/MealMate.Application/Features/DriverDelivery/DriverApp/DriverSupportHandoverDtos.cs`
- `src/MealMate.Application/Features/DriverDelivery/DriverApp/DriverSupportHandoverHandlers.cs`
- `src/MealMate.Application/Features/DriverDelivery/Dispatcher/DispatcherSupportDtos.cs`
- استعلامات الدعم والمرشحين وأمر حل البلاغ في مجلد `Dispatcher` نفسه.
- `src/MealMate.Api/Realtime/DriverSupportHandoverRealtimeNotifier.cs`
- المرجع المختصر: [DRIVER_SUPPORT_HANDOVER_CONTRACT.md](DRIVER_SUPPORT_HANDOVER_CONTRACT.md).
# وصول إشعارات الدعم لمسؤول التوصيل

## واجهة الجرس المطلوبة في الموبايل

وجود API أو Push لا ينشئ واجهة الجرس تلقائيًا. المطلوب من تطبيق مسؤول التوصيل إضافة أيقونة جرس وشاشة قائمة الإشعارات بحالات التحميل والفشل وإعادة المحاولة والقائمة الفارغة، وفق تصميم التطبيق وترجماته العربية والإنجليزية.

العقد الموجود حاليًا:

- `GET /api/v1/delivery-manager/notifications` يرجع مصفوفة أحدث 50 إشعارًا للمستخدم الحالي، مرتبة بالأحدث أولًا. كل عنصر يحتوي `notificationId`, `type`, `title`, `body`, `sentTimeAgo`, `isRead`, `section`, `subjectId`, `deepLink`, `sentAtUtc`. استخدم لغة الطلب الحالية للحصول على النص المناسب.
- `PUT /api/v1/delivery-manager/notifications/{notificationId}/read` بلا جسم طلب يحدد الإشعار كمقروء. تكرار الطلب آمن. حدّث القائمة بعد نجاحه؛ عند فشله احتفظ بحالة غير المقروء وأتح إعادة المحاولة.
- لا يوجد في هذا العقد endpoint مستقل لإجمالي غير المقروء أو pagination. إذا استُخدمت شارة عدد من القائمة، فهي تخص أحدث 50 إشعارًا المحملة فقط؛ لا تعرضها باعتبارها إجمالي كل السجل.

عند الضغط على إشعار افتح وجهته المعروفة من `deepLink` داخل التطبيق وأعد جلب تفاصيلها من الـAPI بصلاحيات مسؤول التوصيل. لا تستنتج صلاحية الوصول من وصول الإشعار، ولا تفتح روابط خارجية عشوائية. عند الرجوع للتطبيق أو استقبال Push حدّث قائمة الجرس، وامنع تكرار العناصر باستخدام `notificationId`. لا تنشئ إشعارات محلية تجريبية لتعويض قائمة فارغة. اختبر اختلاف حساب المستخدم وتغيير اللغة، وظهور بلاغ حقيقي، وتحديده كمقروء، وفشل الشبكة، والتنقل لتفاصيل البلاغ.

بلاغ السائق وطلب إعادة الإسناد وحل البلاغ، وكذلك متابعة نقل العهدة عند وجود بوكس ناقص أو متضرر، تحفظ إشعار الجرس وطلب إرسال Firebase في نفس معاملة قاعدة البيانات. يعالج عامل الـOutbox إرسال الـPush مستقلًا عن اتصال SignalR.

على تطبيق مسؤول التوصيل تسجيل توكن Firebase الحقيقي بعد الدخول وعند تحديث التوكن:

`POST /api/v1/delivery-manager/device-token`

```json
{
  "token": "<FCM token from the device>",
  "platform": "Android"
}
```

استخدم توكن دخول مسؤول التوصيل نفسه. يدعم `platform` أيضًا `iOS`، ويمكن إرسال `deviceId` و`restaurantId` وفق عقد تسجيل الجهاز. عند الخروج عطّل التوكن عبر `POST /api/v1/delivery-manager/device-token/deactivate` بجسم يحتوي `token`.

الإرسال يستهدف الأجهزة النشطة لمسؤول معتمد في مطعم البلاغ. الجهاز المسجل لمطعم محدد لا يستقبل بلاغات مطعم آخر. عدم تسجيل جهاز لا يمنع حفظ إشعار الجرس، ويمكن استرجاعه عبر `GET /api/v1/delivery-manager/notifications`.

بيانات الـPush تتضمن `eventId` و`notificationId` و`restaurantId` و`type` و`eventKey` و`section` و`subjectId` و`deepLink` و`action`، مع `titleAr/titleEn/bodyAr/bodyEn` من الإشعار المحفوظ. الأنواع هي `driver-support-issue` و`driver-support-issue-resolved` و`driver-support-handover`. يفتح التطبيق شاشة البلاغ أو نقل العهدة من `deepLink` ويعيد جلب التفاصيل بصلاحيات المستخدم. نص تنبيه النظام بالعربية مثل مسار إشعارات التوصيل الحالي، وتتوافر اللغتان داخل البيانات لعرض التطبيق.

قد يعاد إرسال الـPush بعد انقطاع اتصال أو فشل جزئي؛ استخدم `notificationId` لمنع تكرار العنصر داخل التطبيق. التوكن الذي يرفضه Firebase باعتباره منتهيًا يعطّل في قاعدة البيانات، ويلزم تسجيل التوكن الجديد من الجهاز.

للتشخيص: إذا ظهر الإشعار في قائمة الجرس ولم يظهر تنبيه الجهاز، راجع تسجيل التوكن، إذن الإشعارات، قناة Android المسماة `mealmate_alerts_channel`، إعداد Firebase على السيرفر، وتشغيل عامل الـOutbox. فشل الإرسال المؤقت أو غياب إعداد Firebase يبقى فشلًا ويخضع لسياسة إعادة المحاولة الحالية؛ بعد خمس محاولات فاشلة تصبح الرسالة poisoned وتحتاج متابعة تشغيلية. لا تسجل التوكن أو مفاتيح Firebase في السجلات.

هذا التعديل يشمل الإشعارات الجديدة بعد نشر النسخة؛ لا يعيد إرسال إشعارات الجرس القديمة تلقائيًا. رفع الكود إلى Git لا يؤكد نشر السيرفر أو وصول Push إلى جهاز فعلي.
