# شاشة 04.10 / 10.10 - سجل العمليات (Operations Log)

تُعد شاشة **سجل العمليات** الأرشيف والمركز الميداني الشامل لمسؤول التوصيل (`Delivery Manager`)، حيث تعرض سجلاً تاريخياً دقيقاً لكافة أحداث التوصيل والإسناد والإلغاء وحالات الفشل لجميع البوكسات، مع إمكانية الفلترة المتقدمة، والبحث اللحظي، والتنقل بين الصفحات.

---

## 📱 تشريح ومكونات الواجهة (UI Anatomy)

### 1. الشريط العلوي (Header)
- **زر الرجوع (`<`)**: للعودة للوحة التحكم الرئيسية.
- **عنوان الشاشة**: `سجل العمليات` باللغة العربية (أو `Operations Log` بالإنجليزية).
- **زر التصفية (`تصفية ⫚`)**: أعلى اليسار بأيقونة فلتر بنفسجية لفتح خيارات التصفية المتقدمة.

### 2. شريط البحث وفلترة التاريخ (Search & Date Presets)
- **حقل البحث (`Search Bar`)**:
  - أيقونة عدسة `🔍`.
  - نص توضيحي: `ابحث برقم البوكس، السائق، أو العميل...`
  - يبحث بـ **Partial Match** غير حساس للأحرف عبر:
    1. كود البوكس (`#BX-10256`).
    2. اسم السائق (سواء السائق المنفذ، الأصلي، أو البديل).
    3. اسم العميل.
- **قائمة التاريخ المنسدلة (`Date Preset Dropdown`)**:
  - قائمة منسدلة سريعة: `📅 آخر 7 أيام ∨`.
  - الخيارات المدعومة:
    - `Today` (اليوم)
    - `Last7Days` (آخر 7 أيام - افتراضي)
    - `Last30Days` (آخر 30 يوماً)
    - `Custom` (تاريخ مخصص عبر تمرير `fromDateUtc` و `toDateUtc`)
    - `All` (كافة السجلات)

### 3. تبويبات الحالات مع العدادات المعزولة (Status Filter Chips)
شريط رقائق أفقي قابل للتمرير يضم 5 حالات مع شارة رقمية (Badge) لكل حالة:
1. **الكل (`All`)**: Badge رقمه `128` (محدد افتراضياً).
2. **مكتملة (`Completed`)**: Badge رقمه `98` (أخضر).
3. **ملغاة (`Cancelled`)**: Badge رقمه `3` (رمادي / أحمر).
4. **فشل التسليم (`Failed Delivery`)**: Badge رقمه `7` (أحمر).
5. **معاد إسنادها (`Reassigned`)**: Badge رقمه `17` (برتقالي).

> [!IMPORTANT]
> **قاعدة عزل العدادات (Counters Isolation)**:
> تعكس أعداد الحالات في كائن `counters` إجمالي العمليات ضمن نطاق التاريخ والبحث المختار، **ولا تتأثر** باختيار التبويب نفسه.

### 4. بطاقات سجل العمليات (Operation Cards)
تتضمن القائمة 4 أنواع رئيسية من البطاقات بحسب نوع العملية:

#### أ. مكتملة بنجاح (`Completed`):
- **اليمين**: صورة السائق + نقطة خضراء 🟢 بالزاوية + اسم السائق + كود البوكس `#BX-10256`.
- **الوسط**: `عميل: اسم العميل` + الموقع بدبوس خريطة `📍 حي الياسمين، الرياض`.
- **اليسار**: التوقيت `اليوم ، 10:45 ص` + شارة خضراء `مكتملة ✔` + سهم `<`.

#### ب. معاد إسنادها (`Reassigned`):
- **اليمين**: صورتان للسائقين وبينهما سهم انتقال `⬅`:
  - السائق الأصلي (السابق): صورته + اسمه تحته (مثل: `يوسف خالد`).
  - سهم الاتجاه: `⬅`.
  - السائق البديل (الجديد): صورته بنقطة برتقالية 🟠 + اسمه تحته (مثل: `شهد المطيري`).
- **الوسط**: بيانات العميل والموقع.
- **اليسار**: التوقيت `اليوم ، 10:28 ص` + شارة برتقالية `معاد إسنادها 🔄` + سهم `<`.

#### جـ. فشل التسليم (`Failed Delivery`):
- **اليمين**: صورة السائق بنقطة حمراء 🔴 + اسمه + كود البوكس.
- **الوسط**: بيانات العميل والموقع.
- **اليسار**: التوقيت `اليوم ، 09:50 ص` + شارة حمراء `فشل التسليم ✖` + سهم `<`.

#### د. ملغاة من قبل المطعم (`Cancelled by Restaurant`):
- **اليمين**: أيقونة متجر/مطعم 🏪 داخل إطار دائري + نص توضيحي: `تم الإلغاء من قبل المطعم`.
- **الوسط**: بيانات العميل والموقع.
- **اليسار**: التوقيت `أمس ، 06:40 م` + شارة رمادية `ملغاة ➖` + سهم `<`.

### 5. شريط التصفح السفلي (Pagination)
- زر **السابق** مع أيقونة `<` (معطل في الصفحة الأولى).
- مؤشر الصفحات: **`1 من 15`** (`pageNumber من totalPages`).
- زر **التالي** مع أيقونة `>` بإطار بنفسجي نشط.

---

## 🔌 عقد الـ REST API

### الرابط المعتمد:
```http
GET /api/v1/dispatcher/operations/log
GET /api/v1/dispatcher/operations-log
```

- **الصلاحية المطلوبة**: `DeliveryManager` (يتم استخراج `restaurantId` تلقائياً من الـ Token).
- **الترويسات (Headers)**:
```http
Accept-Language: ar  # أو en
Authorization: Bearer <JWT_TOKEN>
```

### معاملات الاستعلام (Query Parameters):
| المعامل | النوع | افتراضي | السلوك والقيم المسموحة |
| :--- | :--- | :--- | :--- |
| `restaurantId` | Guid? | null | اختياري (يُستخرج من التوكن تلقائياً). |
| `status` | string | `"All"` | `All`, `Completed`, `Cancelled`, `Failed`, `Reassigned`. |
| `search` | string | null | بحث جزئي في: كود البوكس، اسم السائق، أو اسم العميل. |
| `datePreset` | string | `"Last7Days"` | `Today`, `Last7Days`, `Last30Days`, `Custom`, `All`. |
| `fromDateUtc` | DateTime? | null | مطلوب فقط عند اختيار `datePreset = Custom`. |
| `toDateUtc` | DateTime? | null | مطلوب فقط عند اختيار `datePreset = Custom`. |
| `pageNumber` | int | 1 | يبدأ من 1. |
| `pageSize` | int | 10 | الافتراضي 10، الحد الأقصى 50. |

---

### استجابة الخادم الناجحة (200 OK):
```json
{
  "counters": {
    "allCount": 128,
    "completedCount": 98,
    "cancelledCount": 3,
    "failedCount": 7,
    "reassignedCount": 17
  },
  "operations": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa1",
      "boxId": "3fa85f64-5717-4562-b3fc-2c963f66afa2",
      "boxCode": "#BX-10256",
      "type": "Completed",
      "statusText": "مكتملة",
      "statusColor": "Green",
      "statusIcon": "check",
      "driver": {
        "id": "3fa85f64-5717-4562-b3fc-2c963f66afa3",
        "name": "أحمد السعيد",
        "avatarUrl": "https://cdn.mealmate.app/avatars/ahmed.jpg",
        "indicatorColor": "Green"
      },
      "originalDriver": null,
      "replacementDriver": null,
      "cancelledBy": null,
      "cancelledByText": null,
      "customer": {
        "id": "3fa85f64-5717-4562-b3fc-2c963f66afa4",
        "name": "أحمد العتيبي",
        "labelText": "عميل: أحمد العتيبي",
        "area": "حي الياسمين، الرياض",
        "addressText": "حي الياسمين، الرياض"
      },
      "timeText": "اليوم ، 10:45 ص",
      "occurredAtUtc": "2026-09-22T07:45:00Z"
    },
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa5",
      "boxId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "boxCode": "#BX-10256",
      "type": "Reassigned",
      "statusText": "معاد إسنادها",
      "statusColor": "Orange",
      "statusIcon": "sync",
      "driver": null,
      "originalDriver": {
        "id": "3fa85f64-5717-4562-b3fc-2c963f66afa7",
        "name": "يوسف خالد",
        "avatarUrl": "https://cdn.mealmate.app/avatars/yousef.jpg",
        "indicatorColor": "Grey"
      },
      "replacementDriver": {
        "id": "3fa85f64-5717-4562-b3fc-2c963f66afa8",
        "name": "شهد المطيري",
        "avatarUrl": "https://cdn.mealmate.app/avatars/shahad.jpg",
        "indicatorColor": "Orange"
      },
      "cancelledBy": null,
      "cancelledByText": null,
      "customer": {
        "id": "3fa85f64-5717-4562-b3fc-2c963f66afa4",
        "name": "أحمد العتيبي",
        "labelText": "عميل: أحمد العتيبي",
        "area": "حي الياسمين، الرياض",
        "addressText": "حي الياسمين، الرياض"
      },
      "timeText": "اليوم ، 10:28 ص",
      "occurredAtUtc": "2026-09-22T07:28:00Z"
    },
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa9",
      "boxId": "3fa85f64-5717-4562-b3fc-2c963f66afb0",
      "boxCode": "#BX-10256",
      "type": "CancelledByRestaurant",
      "statusText": "ملغاة",
      "statusColor": "Grey",
      "statusIcon": "minus",
      "driver": null,
      "originalDriver": null,
      "replacementDriver": null,
      "cancelledBy": "Restaurant",
      "cancelledByText": "تم الإلغاء من قبل المطعم",
      "customer": {
        "id": "3fa85f64-5717-4562-b3fc-2c963f66afa4",
        "name": "أحمد العتيبي",
        "labelText": "عميل: أحمد العتيبي",
        "area": "حي الياسمين، الرياض",
        "addressText": "حي الياسمين، الرياض"
      },
      "timeText": "أمس ، 06:40 م",
      "occurredAtUtc": "2026-09-21T15:40:00Z"
    }
  ],
  "pagination": {
    "pageNumber": 1,
    "pageSize": 10,
    "totalItems": 128,
    "totalPages": 15,
    "hasPreviousPage": false,
    "hasNextPage": true
  }
}
```

---

## 🎯 القواعد التشغيلية المعتمدة للموبايل (Client Guidelines)

1. **الترتيب الزمني**: العمليات مرتبة دائماً بالأحدث أولاً (`occurredAtUtc DESC`).
2. **عزل العدادات**: شارات الحالات (`counters`) لا تتأثر بالتبويب النشط؛ بل تعكس إجمالي كل حالة ضمن نطاق البحث والتاريخ الحالي.
3. **تغيير أي فلتر يعيد رقم الصفحة**: عند تغيير التبويب أو البحث أو التاريخ، يجب ضبط `pageNumber = 1`.
4. **استجابة خالية بنجاح**: في حال عدم وجود نتائج، يرجع الخادم `200 OK` مع `operations: []` و `totalItems: 0` (لا يرجع `404`).
5. **الضغط على أي بطاقة**: يتم استخدام `boxId` للانتقال المباشر لشاشة تتبع البوكس ومراحل التوصيل (`Screen 04.08 / 10.08 Box Tracking`).
