# 08.01 - إبلاغ عن مشكلة أثناء التوصيل (Report Delivery Issue)

## 📌 نظرة عامة (Overview)
* **رقم الشاشة وتسميتها:** `08.01 - إبلاغ عن مشكلة أثناء التوصيل (Report Delivery Issue)`
* **الهدف التشغيلي:** تمكين السائق الميداني من الإبلاغ الفوري عن أي طارئ يعيق التوصيل، مع رفع صور توثيقية وشرح نصي اختياري.
* **المستخدم المستهدف:** السائق الميداني (Driver).
* **الحالة:** عقد مُثبّت ومطابق للتنفيذ (تم إنشاء سجل بلاغ حقيقي مع الصور والخطورة).

---

## 🔑 دلالة المعرّفات (Identifier Semantics)
| المعرّف | المعنى | ملاحظات |
| --- | --- | --- |
| `boxId` في المسار | **`TripStop.Id` بصيغة UUID** | يُؤخذ من حقل `BoxId` في استجابة خريطة/قائمة الطلبات (`GET /api/v1/driver/map/route` أو `GET /api/v1/driver/home`). |
| رقم البوكس الظاهر | `#BX-####` (`OrderCode`) | **للعرض فقط** — لا يُقبل في المسار ولا يُستخدم للبحث. |
| `DailyOrderId` | معرّف سجل الطلب اليومي للمطعم | معرّف داخلي منفصل؛ ليس `boxId`. |

## 📤 رفع صور البلاغ (Evidence Photo Upload)
تُرفع الصور أولًا عبر مسار الرفع العام، ثم تُمرَّر المفاتيح الناتجة في طلب البلاغ:

```http
POST /api/v1/uploads HTTP/1.1
Authorization: Bearer <driver_token>
Content-Type: multipart/form-data; boundary=...

------...
Content-Disposition: form-data; name="file"; filename="evidence1.jpg"
Content-Type: image/jpeg

<binary>
------...
Content-Disposition: form-data; name="folder"
issue-evidence
------...
Content-Disposition: form-data; name="scope"
drivers
------...
```

**الاستجابة:** `201 Created` مع `UploadedFileDto`:

```json
{
  "storageKey": "uploads/drivers/issue-evidence/<generated>.jpg",
  "fileName": "evidence1.jpg",
  "contentType": "image/jpeg",
  "sizeBytes": 204800,
  "checksum": null,
  "readUrl": "/api/v1/uploads/content?key=uploads%2Fdrivers%2Fissue-evidence%2F<generated>.jpg",
  "folder": "issue-evidence",
  "scope": "drivers"
}
```

| الإعداد | القيمة |
| --- | --- |
| `scope` | `drivers` (ثابت) |
| `folder` | `issue-evidence` (ثابت) |
| الصيغ المقبولة | `image/jpeg`، `image/png`، `image/webp` |
| الحد الأقصى للملف | 25MB (حد مسار الرفع العام) |
| الحد الأقصى للعدد | 3 صور للبلاغ الواحد |
| الخصوصية | قراءة `uploads/drivers/issue-evidence/*` محمية: المالك نفسه أو طاقم التوصيل فقط |

## 🔌 إرسال البلاغ
```http
POST /api/v1/driver/orders/{boxId}/report-issue HTTP/1.1
Authorization: Bearer <driver_token>
Content-Type: application/json

{
  "issueType": "VehicleBreakdown",
  "description": "انفجار إطار الدراجة على طريق المطار وتعذر إكمال الرحلة",
  "evidencePhotoStorageKeys": [
    "uploads/drivers/issue-evidence/<generated-1>.jpg",
    "uploads/drivers/issue-evidence/<generated-2>.jpg"
  ],
  "severity": "High"
}
```

### الحقول
| الحقل | مطلوب/اختياري | الحد | القواعد |
| --- | --- | --- | --- |
| `issueType` | **مطلوب** | — | قيمة ثابتة من الجدول أدناه (تُقبل بأي حالة أحرف وتُحفظ بالشكل القياسي). |
| `description` | اختياري | **250 حرفًا** (بعد إزالة الفراط) | مطابق لعدّاد التصميم؛ الفراغ الكامل يُعامَل كغير مُرسل. |
| `evidencePhotoStorageKeys` | اختياري | حتى 3 مفاتيح | مصفوفة مفاتيح من مسار الرفع أعلاه. تُتجاهل القيم الفارغة وتُحذف التكرارات. |
| `evidencePhotoStorageKey` | اختياري (توافق قديم) | مفتاح واحد | يُدمج **أولًا** مع المصفوفة: القائمة النهائية = المفتاح المفرد ثم عناصر المصفوفة بعد إزالة الفراغ/التكرار؛ أكثر من 3 مفاتيح فريدة → 400. |
| `severity` | اختياري | — | `Low` / `Medium` / `High` — الافتراضي `Medium` عند عدم الإرسال. |

### القيم المسموحة لـ `issueType`
| القيمة الآلية (machine value) | التسمية العربية | الأصل |
| --- | --- | --- |
| `CustomerUnavailable` | العميل لا يرد / غير متاح | خيار التصميم + تصنيف قائم بمركز الدعم |
| `AddressIssue` | عنوان خاطئ أو غير واضح | خيار التصميم + تصنيف قائم |
| `DamagedBox` | بوكس تالف | خيار التصميم + تصنيف قائم |
| `SevereDelay` | تأخير شديد | خيار التصميم + تصنيف قائم |
| `CustomerRefusal` | العميل رفض الاستلام | خيار التصميم (قيمة جديدة) |
| `VehicleBreakdown` | عطل بالمركبة | خيار التصميم + تصنيف قائم |
| `TrafficAccident` | حادث سير / طارئ | خيار التصميم (قيمة جديدة) |
| `SevereWeather` | سوء الأحوال الجوية | خيار التصميم (قيمة جديدة) |
| `Other` | مشكلة أخرى | خيار التصميم (قيمة جديدة) |

## ✅ الاستجابة الناجحة
الجسم هو **معرّف البلاغ المحفوظ فعليًا** (سجل الشكوى/الدعم) كسلسلة JSON مجردة — ليس كائن `isSuccess/value`:

```http
HTTP/1.1 200 OK
```

```json
"e3333333-3333-3333-3333-333333333333"
```

ما يُحفظ عند النجاح:
* سجل دعم حقيقي (Complaint) بحالة `Submitted` مرتبط بالبوكس والرحلة والمطعم والسائق — نفس المعرّف الذي يظهر لمسؤول التوصيل في مركز الدعم (04.11/04.12).
* تصنيف الخطورة (`severity`) على السجل.
* سجل `ComplaintEvidencePhoto` لكل صورة مقبولة.
* تحديث `FailureReason` على محطة التوصيل + سجل تدقيق `DriverReportedIssue`.

## 🔐 شروط القبول
* المتصل سائق مسجّل الدخول ويملك **إسناد نشط (Active)** على رحلة البوكس؛ وإلا يُرفض الطلب (404 عند غياب توصيل مطابق للسائق، 403 عند البوكس ليس ضمن توصيلاته النشطة).
* كل مفتاح صورة يجب أن يشير إلى ملف مرفوع فعليًا عبر `/api/v1/uploads`، **مملوكًا لنفس حساب السائق**، وبصيغة صورة مقبولة، وموجودًا في التخزين وقت الإرسال.

## ❌ الأخطاء (ProblemDetails)
الشكل العام: `application/problem+json` مع `title`, `detail` (مترجم حسب `Accept-Language`), `status`, `instance`, و`code` ثابت.

| HTTP | `code` | الحالة |
| --- | --- | --- |
| 400 | `driver.issue.issue_type_invalid` | قيمة `issueType` خارج القائمة. |
| 400 | `driver.issue.severity_invalid` | قيمة `severity` خارج `Low/Medium/High`. |
| 400 | `driver.issue.description_too_long` | `description` > 250 حرفًا. |
| 400 | `driver.issue.photo_keys_limit` | أكثر من 3 صور بعد الدمج وإزالة التكرار. |
| 400 | `driver.issue.photo_not_found` | المفتاح ليس ملفًا مرفوعًا فعليًا أو حُذف من التخزين. |
| 400 | `driver.issue.photo_invalid_type` | الصورة ليست JPEG/PNG/WebP. |
| 400 | `Orders.CoordinatesPairRequired` / `Orders.CoordinatesOutOfRange` | لا ينطبق على هذا الطلب (خاص بإعادة الإسناد). |
| 403 | `driver.issue.photo_forbidden` | الصورة مرفوعة من حساب آخر. |
| 403 | `driver.issue.stop_forbidden` | البوكس ليس ضمن التوصيلات النشطة للسائق. |
| 404 | `order.not_found` | لا يوجد سجل توصيل بهذا `boxId`، أو المتصل موقّع الدخول بلا ملف سائق. |

مثال:

```json
{
  "title": "Validation Failed",
  "status": 400,
  "detail": "نوع المشكلة غير مسموح. القيم المسموحة: ...",
  "instance": "/api/v1/driver/orders/.../report-issue",
  "code": "driver.issue.issue_type_invalid"
}
```

## 📡 أحداث البث المباشر (SignalR)
عند النجاح يبث الخادم حدث `driver-issue-updated` إلى `DispatcherHub` (مركز الدعم الميداني 04.11/04.12) بحمولة تشمل `issueId` (المعرّف المحفوظ) و`boxId` و`issueType` و`severity`. فشل البث لا يُبطل الحفظ.

---

*ملاحظة توافق: الاستجابة الناجحة في نسخ سابقة من هذا المستند كانت تُظهر غلاف `{"isSuccess":true,"value":"..."}` — هذا غير مطابق للتنفيذ؛ الغلاف الفعلي هو المعرّف المجرد أعلاه.*
