# 📱 دليل مبرمج الموبايل: شاشات البلاغ وإعادة الإسناد (Screens 08.01 / 08.02)
### Mobile Integration Guide — Driver Report Issue & Request Reassignment APIs

> **الحالة:** عقد مُثبّت ومطابق للتنفيذ على الباك إند (تم إنشاء سجلات حقيقية والتحقق منها باختبارات وحدات + تزامن على قاعدة بيانات حقيقية). النشر على السيرفر لا يزال معلّقًا — لا تبدأ الربط النهائي قبل نشر النسخة المطابقة.

---

## 📑 المحتويات
1. [ما الذي تغيّر ولماذا](#1-ما-الذي-تغيّر-ولماذا)
2. [دلالة المعرّفات: boxId](#2-دلالة-المعرّفات-boxid)
3. [رفع صور البلاغ أولاً (POST /api/v1/uploads)](#3-رفع-صور-البلاغ-أولاً)
4. [شاشة 08.01: إرسال بلاغ مشكلة](#4-شاشة-0801-إرسال-بلاغ-مشكلة)
5. [شاشة 08.02: طلب إعادة إسناد](#5-شاشة-0802-طلب-إعادة-إسناد)
6. [أخطاء ProblemDetails المشتركة](#6-أخطاء-problemdetails-المشتركة)
7. [أحداث البث المباشر (SignalR)](#7-أحداث-البث-المباشر-signalr)
8. [قائمة تحقق الربط وأخطاء شائعة](#8-قائمة-تحقق-الربط-وأخطاء-شائعة)

---

## 1. ما الذي تغيّر ولماذا

| قبل | بعد |
| :--- | :--- |
| البلاغ كان يعيد GUID عشوائيًا **بدون إنشاء أي سجل**؛ الصور والخطورة لا تُحفظ. | البلاغ ينشئ **سجل دعم حقيقي** يظهر فورًا لمسؤول التوصيل في مركز الدعم (04.11/04.12)، مع الخطورة والصور محفوظة. |
| `description` مطلوب بلا حد. | **اختياري** وبحد أقصى **250 حرفًا** (مطابق لعدّاد التصميم). |
| صورة واحدة فقط عبر `evidencePhotoStorageKey`. | حتى **3 صور** عبر `evidencePhotoStorageKeys` مع بقاء الحقل القديم للتوافق. |
| `reason` في إعادة الإسناد نص حر. | **قيم ثابتة** من قائمة التصميم؛ النص الحر يُرفض (400). |
| الإحداثيات تُتجاهل تمامًا. | تُتحقق وتُحفظ مع الطلب (زوج مكتمل أو لا شيء). |
| لا يوجد منع لتكرار طلب إعادة الإسناد. | **منع كامل** بما يشمل الطلبات المتزامنة (حماية على مستوى قاعدة البيانات). |

---

## 2. دلالة المعرّفات: boxId

| المعرف | ما هو | الاستخدام |
| :--- | :--- | :--- |
| **`boxId`** في المسار | **`TripStop.Id` بصيغة UUID** | يُؤخذ حرفيًا من حقل `BoxId` في استجابة `GET /api/v1/driver/map/route` (أو `DriverCurrentDeliveryTask.BoxId` من `GET /api/v1/driver/home`). |
| رقم البوكس الظاهر | `#BX-####` (حقل `BoxCode` / `OrderCode`) | **للعرض فقط** — لا يصلح في المسار ولا في أي بحث. |
| `DailyOrderId` | معرّف سجل الطلب اليومي للمطعم | معرف داخلي منفصل تمامًا؛ لا يُستخدم في هاتين الشاشتين. |

> ⚠️ إذا استخدم التطبيق `BoxCode` أو `DailyOrderId` مكان `BoxId` فستحصل على **404** دائمًا.

---

## 3. رفع صور البلاغ أولاً

قبل إرسال البلاغ بالصور، ارفع كل صورة عبر مسار الرفع العام:

```http
POST /api/v1/uploads HTTP/1.1
Authorization: Bearer <driver_token>
Content-Type: multipart/form-data; boundary=...

------boundary
Content-Disposition: form-data; name="file"; filename="evidence1.jpg"
Content-Type: image/jpeg

<binary>
------boundary
Content-Disposition: form-data; name="folder"
issue-evidence
------boundary
Content-Disposition: form-data; name="scope"
drivers
------boundary--
```

**الاستجابة: `201 Created`**

```json
{
  "storageKey": "uploads/drivers/issue-evidence/9f2c....jpg",
  "fileName": "evidence1.jpg",
  "contentType": "image/jpeg",
  "sizeBytes": 204800,
  "checksum": null,
  "readUrl": "/api/v1/uploads/content?key=uploads%2Fdrivers%2Fissue-evidence%2F9f2c....jpg",
  "folder": "issue-evidence",
  "scope": "drivers"
}
```

| الإعداد | القيمة الثابتة |
| :--- | :--- |
| `scope` | `drivers` |
| `folder` | `issue-evidence` |
| الصيغ المقبولة | `image/jpeg` أو `image/png` أو `image/webp` |
| الحد الأقصى للملف الواحد | 25MB (حد المسار العام) |
| الحد الأقصى لعدد الصور في البلاغ | **3** |
| الخصوصية | قراءة الصور محمية: المالك نفسه أو طاقم التوصيل فقط |

**قواعد إلزامية سيتحقق منها السيرفر عند إرسال البلاغ:**
1. `storageKey` يجب أن يشير إلى ملف **مرفوع فعلًا** عبر هذا المسار (أي نص عشوائي → 400 `driver.issue.photo_not_found`).
2. الملف يجب أن يكون **مرفوعًا من حساب السائق نفسه** (رفع من حساب آخر → 403 `driver.issue.photo_forbidden`).
3. نوع الملف يجب أن يكون JPEG/PNG/WebP (PDF أو غيره → 400 `driver.issue.photo_invalid_type`).

احتفظ بقيمة `storageKey` من كل رفع؛ ستستخدمها في طلب البلاغ.

---

## 4. شاشة 08.01: إرسال بلاغ مشكلة

```http
POST /api/v1/driver/orders/{boxId}/report-issue HTTP/1.1
Authorization: Bearer <driver_token>
Content-Type: application/json

{
  "issueType": "VehicleBreakdown",
  "description": "انفجار إطار الدراجة على طريق المطار وتعذر إكمال الرحلة",
  "evidencePhotoStorageKeys": [
    "uploads/drivers/issue-evidence/9f2c....jpg",
    "uploads/drivers/issue-evidence/4ab1....png"
  ],
  "severity": "High"
}
```

### الحقول

| الحقل | مطلوب/اختياري | الحد | القواعد |
| :--- | :--- | :--- | :--- |
| `issueType` | **مطلوب** | — | قيمة ثابتة من الجدول أدناه. تُقبل بأي حالة أحرف (`damagedbox` = `DamagedBox`) وتُحفظ بالشكل القياسي. |
| `description` | اختياري | **250 حرفًا** بعد إزالة الفراط | الفراغ الكامل (`"   "`) يُعامل كغير مُرسل. |
| `evidencePhotoStorageKeys` | اختياري | حتى **3** مفاتيح | مصفوفة مفاتيح من خطوة الرفع. تُتجاهل العناصر الفارغة وتُحذف المكررات بالترتيب. |
| `evidencePhotoStorageKey` | اختياري (**توافق مع الإصدارات القديمة**) | مفتاح واحد | يُدمج **أولًا** مع المصفوفة: القائمة النهائية = المفتاح المفرد ثم عناصر المصفوفة بعد إزالة الفراغ/التكرار. إذا تجاوز الدمج 3 مفاتيح فريدة → 400. |
| `severity` | اختياري | — | `Low` / `Medium` / `High` — أي حالة أحرف. **الافتراضي `Medium`** عند عدم الإرسال. |

### القيم المسموحة لـ `issueType`

| القيمة الآلية (تُرسل في JSON) | التسمية العربية (للواجهة) | ملاحظة |
| :--- | :--- | :--- |
| `CustomerUnavailable` | العميل لا يرد / غير متاح | تصنيف قائم بمركز الدعم |
| `AddressIssue` | عنوان خاطئ أو غير واضح | تصنيف قائم |
| `DamagedBox` | بوكس تالف | تصنيف قائم |
| `SevereDelay` | تأخير شديد | تصنيف قائم |
| `CustomerRefusal` | العميل رفض الاستلام | قيمة جديدة — بذرة مصممة |
| `VehicleBreakdown` | عطل بالمركبة | تصنيف قائم |
| `TrafficAccident` | حادث سير / طارئ | قيمة جديدة |
| `SevereWeather` | سوء الأحوال الجوية | قيمة جديدة |
| `Other` | مشكلة أخرى | قيمة جديدة |

> 💡 ربط الواجهة: عرض التسمية العربية في الكروت، وأرسل القيمة الآلية كما هي. لا تُبنِ الترجمات محليًا من القيم الآلية.

### الاستجابة الناجحة

```http
HTTP/1.1 200 OK
```

```json
"e3333333-3333-3333-3333-333333333333"
```

> ⚠️ **الجسم سلسلة JSON مجردة** (المعرّف المحفوظ للبلاغ بين علامتي تنصيص) — **ليس** الكائن `{"isSuccess":true,"value":"..."}` الذي ورد في وثائق قديمة. حدّث محلل JSON.

ماذا يعني هذا المعرّف؟ هو `ComplaintId` للسجل الداعم المحفوظ — نفس المعرّف الذي يراه مسؤول التوصيل في مركز الدعم (04.11/04.12) وفي حدث `driver-issue-updated`.

---

## 5. شاشة 08.02: طلب إعادة إسناد

```http
POST /api/v1/driver/orders/{boxId}/request-reassignment HTTP/1.1
Authorization: Bearer <driver_token>
Content-Type: application/json

{
  "reason": "VehicleBreakdown",
  "notes": "المركبة متوقفة أمام مجمع الأفنيوز، البوكس جاهز للتسليم لأي سائق قريب",
  "latitude": 29.3000,
  "longitude": 47.9300
}
```

### الحقول

| الحقل | مطلوب/اختياري | الحد | القواعد |
| :--- | :--- | :--- | :--- |
| `reason` | **مطلوب** | — | قيمة ثابتة من الجدول أدناه (أي حالة أحرف). **النص الحر يُرفض بـ 400.** |
| `notes` | اختياري | **250 حرفًا** | التفاصيل الحرة تُكتب هنا (الموقع المقترح، مكان الصندوق...). |
| `latitude` + `longitude` | اختياري (زوج) | WGS-84: العرض ‎±90 والطول ‎±180 | **الاثنان معًا أو لا شيء**: إرسال أحدهما فقط → 400 `Orders.CoordinatesPairRequired`. خارج النطاق → 400 `Orders.CoordinatesOutOfRange`. عند الإرسال يُحفظ الموقع مع الطلب ليستفيد منه السائق البديل. |

### القيم المسموحة لـ `reason` (قائمة التصميم الثابتة)

| القيمة الآلية (تُرسل في JSON) | التسمية العربية (للقائمة المنسدلة) |
| :--- | :--- |
| `VehicleBreakdown` | عطل بالمركبة |
| `TrafficAccident` | حادث سير مروري |
| `MedicalOrPersonalEmergency` | ظرف صحي أو طارئ شخصي |
| `DeviceFailure` | نفاد بطارية الهاتف أو عطل في التطبيق |
| `OtherOperationalReason` | سبب تشغيلي آخر |

### الاستجابة الناجحة

```http
HTTP/1.1 200 OK
```

```json
{
  "requestId": "f4444444-4444-4444-4444-444444444444",
  "boxId": "c2222222-2222-2222-2222-222222222222",
  "boxCode": "#BX-1256",
  "status": "ReassignmentRequested",
  "message": "تم إرسال طلب إعادة الإسناد للبوكس #BX-1256 بنجاح. سيتم إشعارك فور قبول الطلب من سائق آخر.",
  "requestedAtUtc": "2026-10-07T08:45:00Z"
}
```

`requestId` = معرّف سجل الدعم المحفوظ (`ComplaintId`) — نفس المعرّف الذي يعالجه مسؤول التوصيل في شاشة تعيين سائق بديل (04.13).

### قواعد الحالة والتكرار (مهم جدًا للـ UX)

| السيناريو | استجابة السيرفر | ما يعرضه التطبيق |
| :--- | :--- | :--- |
| أول طلب لبوكس حالته `PickedUp` | `200` مع النموذج أعلاه | شاشة 08.03 "تم إرسال الطلب" |
| تكرار الإرسال لنفس البوكس **أثناء وجود طلب نشط** (يشمل ضغط مزدوج أو إعادة إرسال بعد انقطاع الشبكة) | **`409`** كود `driver.reassignment.request_already_active` | رسالة "يوجد طلب نشط بالفعل، انتظر معالجة فريق التشغيل" — ليست نجاحًا صامتًا |
| طلب جديد بعد حل الطلب السابق (Resolved/Rejected/Closed) | `200` — طبيعي | التدفق العادي |
| حالة البوكس غير `PickedUp` (مثلًا Delivered/Failed/Pending) | **`409`** كود `driver.reassignment.stop_state_not_allowed` | إخفاء الزر مسبقًا للبوكسات غير `PickedUp` |
| البوكس ليس ضمن توصيلات السائق النشطة | **`403`** كود `driver.reassignment.stop_forbidden` | رسالة خطأ عامة |

> 💡 **لمنع الـ 409 في الواجهة:** بعد نجاح الطلب الأول عطّل زر الإرسال محليًا حتى يصل حدث تغيّر حالة البوكس (إسناد سائق بديل). الـ 409 يبقى المعالج الاحتياطي الصحيح.

> ℹ️ **أثر العملية:** عند قبول الطلب تُعاد محطة البوكس إلى `Pending` وتُعلَّم إسنادات الرحلة النشطة `Reassigned` — هذا مقصود (الإسناد على مستوى الرحلة، والسائق البديل يتولى الرحلة كاملة). التطبيق سيرى تحديث الحالة عبر أحداث البث/التحديث الدوري.

---

## 6. أخطاء ProblemDetails المشتركة

كل الأخطاء تأتي بـ `Content-Type: application/problem+json` والرسائل مترجمة تلقائيًا حسب `Accept-Language` (`ar` / `en`):

```json
{
  "title": "Validation Failed",
  "status": 400,
  "detail": "نوع المشكلة غير مسموح. القيم المسموحة: ...",
  "instance": "/api/v1/driver/orders/.../report-issue",
  "code": "driver.issue.issue_type_invalid"
}
```

> 🔑 اعتمد على حقل **`code`** في منطق التطبيق (قيم ثابتة)، واستخدم `detail` للعرض فقط — لا تطابق على نص الرسالة.

### أخطاء 08.01

| HTTP | `code` | متى |
| :--- | :--- | :--- |
| 400 | `driver.issue.issue_type_invalid` | قيمة `issueType` خارج القائمة (يشمل `null` والفراغ). |
| 400 | `driver.issue.severity_invalid` | قيمة `severity` خارج `Low/Medium/High`. |
| 400 | `driver.issue.description_too_long` | `description` أطول من 250 حرفًا. |
| 400 | `driver.issue.photo_keys_limit` | أكثر من 3 صور فريدة بعد الدمج. |
| 400 | `driver.issue.photo_not_found` | المفتاح ليس ملفًا مرفوعًا فعليًا أو حُذف من التخزين. |
| 400 | `driver.issue.photo_invalid_type` | الصورة ليست JPEG/PNG/WebP. |
| 403 | `driver.issue.photo_forbidden` | الصورة مرفوعة من حساب آخر. |
| 403 | `driver.issue.stop_forbidden` | البوكس ليس ضمن التوصيلات النشطة للسائق. |
| 404 | `order.not_found` | لا يوجد سجل توصيل بهذا `boxId`، أو المتصل ليس سائقًا (لا ملف سائق لحسابه). |

### أخطاء 08.02

| HTTP | `code` | متى |
| :--- | :--- | :--- |
| 400 | `driver.reassignment.reason_invalid` | قيمة `reason` خارج القائمة (يشمل النص الحر). |
| 400 | `driver.reassignment.notes_too_long` | `notes` أطول من 250 حرفًا. |
| 400 | `Orders.CoordinatesPairRequired` | إرسال أحد الإحداثيين دون الآخر. |
| 400 | `Orders.CoordinatesOutOfRange` | إحداثيات خارج نطاق WGS-84. |
| 403 | `driver.reassignment.stop_forbidden` | البوكس ليس ضمن التوصيلات النشطة للسائق. |
| 404 | `order.not_found` | لا يوجد سجل توصيل بهذا `boxId`، أو المتصل ليس سائقًا. |
| 409 | `driver.reassignment.request_already_active` | يوجد طلب إعادة إسناد نشط لنفس البوكس (يشمل الطلبات المتزامنة). |
| 409 | `driver.reassignment.stop_state_not_allowed` | حالة البوكس تسمح فقط بـ `PickedUp`. |

---

## 7. أحداث البث المباشر (SignalR)

على غرفة `DispatcherHub` (للمشرفين فقط — تطبيق السائق لا يستهلكها):

| الحدث | متى | الحمولة |
| :--- | :--- | :--- |
| `driver-issue-updated` | بعد نجاح البلاغ (08.01) | `issueId` (المعرّف المحفوظ)، `boxId`، `driverId`، `issueType`، `description`، `severity`، `reportedAtUtc` |
| `driver-requested-reassignment` + `driver-issue-updated` | بعد نجاح إعادة الإسناد (08.02) | `requestId`، `boxId`، `driverId`، `reason`، `notes`، `status`، `requestedAtUtc` |

فشل البث لا يُبطل الحفظ: نجاح HTTP يعني أن السجل محفوظ حتى لو فاتك الحدث.

---

## 8. قائمة تحقق الربط وأخطاء شائعة

- [ ] **`boxId` من حقل `BoxId` فقط** (UUID) — استخدام `BoxCode` يعطي 404 دائمًا.
- [ ] **الصور تُرفع أولاً** بـ `scope=drivers&folder=issue-evidence`، ثم تُمرر مفاتيح `storageKey` في البلاغ.
- [ ] **استجابة 08.01 سلسلة مجردة** — لا تحللها ككائن `isSuccess/value`.
- [ ] **`reason` قيم ثابتة فقط** — انقل أي كتابة حرة إلى `notes`.
- [ ] **الإحداثيات كلاهما أو لا شيء** — عطّل زر الإرسال إذا اكتمل زوج واحد.
- [ ] **عطّل زر إعادة الإسناد بعد أول نجاح** حتى يتغير وضع البوكس؛ عالج الـ 409 كرسالة "طلب نشط".
- [ ] **عطّل أزرار الشاشتين** عندما تكون حالة البوكس غير `PickedUp` (بلاغ 08.01 مسموح لأي حالة، لكن إعادة الإسناد 08.02 لـ `PickedUp` فقط).
- [ ] حدّ العداد في الواجهة **250 حرفًا** لـ `description` و`notes` — السيرفر يرفض ما فوقه.
- [ ] اعتمد على `code` في الأخطاء، لا على نص `detail`.
- [ ] أرسل `Accept-Language: ar` (أو `en`) لتحصل على الرسائل بلغة التطبيق.

---

## 🔎 سيناريوهات اختبار سريعة (لضمان الربط)

1. **بلاغ كامل بالصور:** ارفع صورتين → أرسل البلاغ بـ `severity: "High"` → يجب أن يرجع سلسلة UUID، ثم أعد الإرسال: بلاغ جديد مستقل يُقبل (لا يوجد منع تكرار للبلاغات).
2. **بلاغ بـ 4 صور:** يجب أن يرجع `400 driver.issue.photo_keys_limit`.
3. **صورة من حساب آخر:** أنشئ مفتاحًا بحساب سائق آخر ثم أرسله → `403 driver.issue.photo_forbidden`.
4. **إعادة إسناد ثم إعادة إرسال:** نجاح أول (200) ثم إرسال ثانٍ مباشر → `409 driver.reassignment.request_already_active` (وليس 403).
5. **نص حر في reason:** `"عطل بالمركبة"` (عربي) → `400 driver.reassignment.reason_invalid` — القيم الآلية فقط.
6. **إحداثيات ناقصة:** `latitude` فقط بدون `longitude` → `400 Orders.CoordinatesPairRequired`.
7. **إعادة إسناد لبوكس Delivered** → `409 driver.reassignment.stop_state_not_allowed`.
