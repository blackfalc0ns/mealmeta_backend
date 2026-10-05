# حالة تشغيل السائق — عقد API للموبايل

> **الجمهور:** فريق تطبيق السائق (Flutter). التاريخ: 2026-10-04.
> الحالة: معتمد للتنفيذ على الباك. لا تُستخدم الأسماء والمعرفات والتواريخ الموجودة في الأمثلة كبيانات إنتاج — كلها قيم توثيقية.

## الفكرة باختصار

حالة تشغيل السائق (Active / Inactive) يديرها **مسؤول التوصيل** فقط من لوحته، والسائق يقرأها من `GET /api/v1/driver/home`. السائق لا يبدأ وردية بنفسه، ولا يغيّر حالته من التطبيق. فتح التطبيق أو جلب home أو الاتصال بـ SignalR لا يفعّل السائق ولا يفتح جلسة عمل.

## القرارات المعتمدة (2026-10-04)

1. مسؤول التوصيل (دور `DeliveryManager` وأدوار الإدارة) هو صاحب تغيير حالة التشغيل عبر `PATCH /api/v1/dispatcher/drivers/{driverId}/shift-status`، ونطاقه سائقو مطعمه.
2. قيم `shiftStatus` المعتمدة: **Active / Inactive** فقط. القيمة القديمة `Offline` **لم تعد تُرجع أبدًا** — انظر «انتقال العملاء من Offline» أدناه.
3. الجلسة النشطة (`DriverWorkSession` بحالة `Active`) هي مصدر حالة التشغيل. `isAvailable` حقل منفصل يعكس جاهزية استلام الطلب ولا يشتق منه `shiftStatus`.
4. إيقاف سائق عليه طلبات نشطة يُرفض بـ **409** حتى تُستكمل الطلبات أو يُعاد إسنادها. لا يُسقط أي طلب ولا يُغلق ضمنيًا.
5. في حالة Inactive تُرجع `home` القيمة الثابتة `unreadNotificationsCount: 0`. العدد الحقيقي غير المقروء يظهر تلقائيًا فور تشغيل السائق (قرار معتمد: لا تبنِ منطق إشعارات على هذا العدّاد في شاشة «غير متصل»).
6. `POST /api/v1/driver/shift/start` و`POST /api/v1/driver/shift/end` مغلقة أمام دور Driver بـ **403**.

## GET /api/v1/driver/home

- المصادقة: Bearer JWT بدور `Driver`.
- الاستجابة: كائن JSON مباشر (بلا غلاف إضافي) بأسماء حقول camelCase، وقيم `null` تُضمَّن كمفاتيح موجودة بقيمة null.

### مثال Inactive (شاشة «غير متصل»)

```json
{
  "driverId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "driverName": "أحمد إبراهيم",
  "driverCode": "DRV-1256",
  "profileImageUrl": null,
  "shiftStatus": "Inactive",
  "isAvailable": false,
  "currentStatusText": "غير متصل",
  "nextLocationText": null,
  "targetProgress": null,
  "currentDeliveryTask": null,
  "todaySummary": null,
  "dailyPerformance": null,
  "activeSession": null,
  "unreadNotificationsCount": 0
}
```

ملاحظات:
- بيانات الهوية (`driverId`, `driverName`, `driverCode`, `profileImageUrl`) حقيقية دائمًا ولا تصبح null في Inactive.
- الحقول التشغيلية الستة في Inactive: `nextLocationText`, `targetProgress`, `currentDeliveryTask`, `todaySummary`, `dailyPerformance`, `activeSession` — كلها `null`.
- عند `Inactive` تكون `isAvailable=false` في الاستجابة حتى لو كان علم التوفر القديم في قاعدة البيانات true. القراءة لا تفتح جلسة ولا تعدّل البيانات القديمة. التشغيل يتطلب أمرًا صريحًا من مسؤول التوصيل عبر `shift-status`.

### مثال Active متاح

```json
{
  "driverId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "driverName": "أحمد إبراهيم",
  "driverCode": "DRV-1256",
  "profileImageUrl": null,
  "shiftStatus": "Active",
  "isAvailable": true,
  "currentStatusText": "متصل ومتاح لاستلام الطلبات",
  "nextLocationText": null,
  "targetProgress": { "completedCount": 0, "targetCount": 8, "remainingCount": 8, "progressPercentage": 0, "performanceLevel": "جيد", "summaryText": "أتممت 0 من إجمالي 8 طلبات", "remainingText": "تبقى 8 طلبات لإتمام الهدف" },
  "currentDeliveryTask": null,
  "todaySummary": { "totalOrders": 0, "deliveredOrders": 0, "inDeliveryOrders": 0, "incompleteOrders": 0 },
  "dailyPerformance": { "onTimeRatePercentage": 0, "distanceCoveredKm": 0, "averageDeliveryMinutes": 0 },
  "activeSession": { "sessionId": "5bc9d5e1-...", "startedAtUtc": "2026-10-03T15:00:00Z", "workDurationText": "1 س 0 د" },
  "unreadNotificationsCount": 3
}
```

### مثال Active مشغول (أثناء توصيل طلب)

نفس بنية «Active متاح» مع:

```json
{
  "shiftStatus": "Active",
  "isAvailable": false,
  "currentStatusText": "خارج التوصيل (توصيل الطلب)",
  "nextLocationText": "الموقع التالي: السالمية",
  "currentDeliveryTask": {
    "boxId": "9f0c...",
    "boxCode": "#BX-1256",
    "customerName": "محمد علي",
    "customerPhone": "+965 50123456",
    "deliveryAddress": "السالمية",
    "destinationLatitude": 29.3375,
    "destinationLongitude": 48.0225,
    "mealsCount": 3,
    "mealsSummary": "3 وجبات",
    "deliveryTimeSlot": "09:20 ص",
    "status": "PickedUp",
    "deliveryNotes": null
  }
}
```

**قاعدة أساسية:** السائق المشغول يبقى `Active` مع `isAvailable=false` ويظهر على الشاشة الرئيسية **مع طلبه الحالي**. لا تستخدم `isAvailable` وحدها لاختيار شاشة «غير متصل» — اختَر بالشاشة من `shiftStatus` فقط.

### قواعد اختيار الشاشة (للموبايل)

| `shiftStatus` | الشاشة |
| --- | --- |
| `"Active"` | الرئيسية (متاح أو مشغول حسب `isAvailable` ووجود `currentDeliveryTask`) |
| `"Inactive"` | «غير متصل» |
| أي قيمة أخرى / غياب الحقل | تعامل معها كـ Inactive (أمان للتوافق أثناء الانتقال) |

### انتقال العملاء من Offline

القيمة `Offline` حُذفت من العقد. إن كان الكود الحالي يفحص `shiftStatus == "Offline"` لعرض شاشة «غير متصل» فاستبدله بالفحص `shiftStatus == "Active"` للشاشة الرئيسية، وكل ما عدا ذلك → «غير متصل».

## PATCH /api/v1/dispatcher/drivers/{driverId}/shift-status (لوحة مسؤول التوصيل)

- المصادقة: Bearer JWT بدور `DeliveryManager` (أو RestaurantOwner / SuperAdmin / OperationsAdmin أو صلاحية إدارة السائقين)، وضمن نطاق مطعم صاحب الطلب.
- الطلب:

```json
{ "shiftStatus": "Active", "reason": "بدء الوردية بواسطة مسؤول التوصيل" }
```

- `shiftStatus` يقبل `"Active"` أو `"Inactive"` (غير حساس لحالة الأحرف). `reason` اختياري ويُسجَّل في سجل التدقيق.
- التشغيل `Active` يفتح جلسة عمل واحدة بعد التحقق من أهلية السائق (اعتماد الحساب، عدم الإيقاف، صلاحيات الرخصة/الهوية/الاستمارة). تكرار الطلب آمن: لا ينشئ جلسة ثانية ولا يغيّر وقت البدء.
- الإيقاف `Inactive` يغلق الجلسة ويجعل `isAvailable=false`، ويُرفض بـ 409 إن كان للسائق طلبات نشطة (Pending/PickedUp).

### الاستجابة الناجحة (200)

```json
{
  "driverId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "shiftStatus": "Active",
  "isAvailable": true,
  "currentStatusText": "متصل ومتاح لاستلام الطلبات",
  "updatedAtUtc": "2026-10-03T18:00:00Z"
}
```

### الأخطاء

| HTTP | code | متى |
| --- | --- | --- |
| 400 | `DRIVER_SHIFT_STATUS_INVALID` | قيمة غير Active/Inactive |
| 401 | — | بدون مصادقة |
| 403 | `Driver.NotApproved` / `Driver.AccountSuspended` / `Driver.LicenseExpired` … | أهلية السائق مرفوضة عند التشغيل |
| 403 | `DRIVER_ACCESS_DENIED` | مستخدم بلا صلاحية |
| 404 | `DRIVER_NOT_FOUND` | سائق خارج نطاق المطعم أو غير موجود |
| 409 | `DRIVER_SHIFT_ACTIVE_ORDERS` | إيقاف أثناء وجود طلبات نشطة (الوصف يذكر عددها) |

## PATCH /api/v1/dispatcher/drivers/{driverId}/availability (سلوك محدَّث)

- `availability=false` يعمل كالسابق (جاهزية فقط).
- `availability=true` على سائق حالته التشغيلية Inactive (لا جلسة نشطة) تُرفض بـ **409** بكود `DRIVER_NOT_OPERATIONAL` — يجب تشغيل السائق أولًا عبر `shift-status`. زر التوفر لا يفتح جلسة ولا يفعّل السائق.

## SignalR — حدث تغيير الحالة

- Hub: `DriverHub` على `/hubs/driver-hub` (والاسم البديل `/hubs/driver`).
- المصادقة: **إلزامية** — مرر التوكن في `access_token` query أثناء الاتصال. الهوية تُحسم من الـJWT فقط (لا `driverId` من query/header).
- المجموعة: السائق يُضاف تلقائيًا إلى `driver:{driverProfileId}` حسب هويته الموثّقة. لا يمكن لاتصال الانضمام لمجموعة سائق آخر (`JoinDriverGroup` لمعرف غيرك مرفوض).

### الحدث `driver-status-updated`

يُرسل إلى مجموعة السائق المعني بعد نجاح حفظ التغيير فقط:

```json
{
  "driverId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "shiftStatus": "Active",
  "isAvailable": true,
  "currentStatusText": "متصل ومتاح لاستلام الطلبات",
  "updatedAtUtc": "2026-10-03T18:00:00Z"
}
```

- الحدث لا يضمن الاستلام إذا كان الاتصال مفصولًا لحظة الإرسال؛ لذلك أعد التحميل عند إعادة الاتصال.

### سياسة إعادة التحميل (للموبايل)

1. عند استقبال `driver-status-updated`: أعد استدعاء `GET home` وتحدّث الشاشة من استجابته (الحدث إشعار، وليس مصدر بيانات العرض).
2. عند إعادة اتصال SignalR بعد انقطاع: أعد استدعاء `home`.
3. عند العودة من الخلفية (resume): أعد استدعاء `home`.
4. **ابقَ اتصال استقبال الحالة قائمًا في شاشة «غير متصل» ما دام المستخدم مسجل الدخول** — هذا هو طريق استلام قرار التشغيل. لا تربط استمرار الاتصال بـ `isAvailable`.
5. لا تستخدم `POST /driver/shift/start` أو `shift/end` في أي تدفق — تُعيد 403 لدور Driver.

## حدود الضمان

- إرسال SignalR وحده لا يضمن وصول الحدث لجهاز مفصول؛ استرجاع أضمن تحديث فائت يكون دائمًا عبر `GET home` عند reconnect/resume.
- أسماء الحقول في هذا الدليل هي العقد المعتمد؛ راجع OpenAPI عند أي شك، وأي تعديل لاحق يمر عبر تحديث هذا الدليل أولًا.


## إصلاح اتساق الاتصال — 2026-10-05

- `GET /api/v1/dispatcher/drivers`: كل item يحتوي `shiftStatus` و`currentStatusText` من نفس مصدر home.
- `GET /api/v1/dispatcher/drivers/{driverId}`: الحقل `driver` يحتوي القيمتين نفسيهما.
- عرض «متصل / غير متصل» في لوحة المسؤول وتطبيق السائق يعتمد على `shiftStatus` فقط؛ `operationalStatus` يصف التوفر/التوصيل وليس اتصال السائق.
- زر تشغيل السائق في لوحة المسؤول يستخدم `PATCH .../shift-status` مع `Active` أو `Inactive`. لا يستخدم `availability` كبديل لإدارة الجلسة.
- `availability` يغيّر جاهزية إسناد طلب دون إغلاق جلسة؛ true دون جلسة يرجع 409 `DRIVER_NOT_OPERATIONAL` حتى في الطلب المتكرر.
- إعادة إسناد مهمة إلى سائق بديل لا تغيّر توفر السائق القديم أو جلسة عمله. عدم التوفر أو الإيقاف إجراء منفصل وصريح من المسؤول.
- فقد اتصال الشبكة/SignalR يعرض مؤشر مشكلة اتصال مستقلًا، ولا يُترجم إلى `shiftStatus=Inactive`. بعد عودة الشبكة، أعد قراءة home.
- لم يتم إنشاء جلسات تلقائيًا للسائقين المتاحين قديمًا دون جلسة. يجب تشغيلهم صراحة بواسطة المسؤول.
