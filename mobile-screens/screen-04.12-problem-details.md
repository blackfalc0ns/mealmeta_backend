# Screen 04.12: تفاصيل المشكلة والبلاغ الميداني (Screen 10.12 - Problem Details)

## 🎯 الغرض من الشاشة ونظرة عامة (Overview & Purpose)
تُمثّل هذه الشاشة واجهة الفحص والتدخل المعمق لبلاغ أو مشكلة تشغيلية محددة (`Issue Resolution Workspace`) مخصصة لمسؤول التوصيل (`DeliveryManager`). تفتح الشاشة بالاعتماد على استلام معرّف المشكلة (`issueId`) فقط في المسار (`Route parameter`) واستدعاء الـ API لجلب تفاصيل البلاغ الحي.

تُمكّن الشاشة مسؤول التوصيل من:
1. **معاينة خطورة المشكلة وتوقيتها**: كود البوكس، المنطقة، عدد البوكسات المتأثرة، درجة الأولوية، تاريخ الإنشاء الفعلي (`createdAtUtc`) والنص الزمني (`reportedTimeText`).
2. **فحص نوع المشكلة وحالتها**: تصنيف المشكلة الصريح (`category`, `categoryLabel`, `categoryColor`) وحالتها الشاملة (`status`: "Open" | "InProgress" | "Resolved") وتسميتها (`statusLabel`).
3. **مراجعة السائق الحالي المكلف**: الاطلاع على حالته البرمجية الثابتة (`status`: "OnTask" | "Available" | "Unavailable")، وبيانات مركبته المهيكلة (`vehicle`: type, model, color, plateNumber) مع إمكانية الاتصال المباشر به.
4. **مراجعة شرح المشكلة وصور الإثبات**: استعراض صور الإثبات (`evidencePhotos`) مع الصور المصغرة (`thumbnailUrl`) لتوفير استهلاك البيانات، وإمكانية فتح الصورة الأصلية (`url`) بكامل الشاشة مع دعم التكبير (`Zoom & Pinch`).
5. **التحقق من بيانات الرحلة الرقمية (`tripInfo`)**: اسم العميل، عدد الوجبات، فترة التسليم الرقمية (`expectedDeliveryFromUtc`, `expectedDeliveryToUtc`) وموقع الاستلام والتسليم التفصيلي.
6. **اتخاذ القرار التشغيلي الحاسم**:
   - **تعيين سائق بديل**: الانتقال إلى شاشة `Screen 04.13` مع تمرير `issueId` فقط، حيث تتولى 04.13 استدعاء واجهات المرشحين وإعادة الإسناد، ثم تعود لـ 04.12 لتحديث السائق مباشرة.
   - **حل المشكلة وإغلاقها (`Mark as Resolved`)**: إدخال ملاحظات الحل (إلزامية بين 3 و 500 حرف)، وتحديث حالة البلاغ، مع منع تكرار الحل (409 Conflict). وفي حال كانت المشكلة محلولة مسبقاً، تعرض الشاشة كارت بيانات الحل (`resolution`) بدلاً من أزرار الإجراءات.

---

## 🎨 التحليل البصري والتخطيطي (UI/UX Breakdown & Components)

```
┌────────────────────────────────────────────────────────┐
│  [<]                   تفاصيل المشكلة               [⋮] │
├────────────────────────────────────────────────────────┤
│  ┌───────────────────────────────────────────────────┐ │
│  │  #BX-10256                         [أولوية عالية] │ │
│  │  📍 السالمية، ق 4                  📦 1 بوكس متأثر │ │
│  │  🏷️ مشكلة توصيل                   🕒 منذ 15 دقيقة │ │
│  └───────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────┤
│  👤 السائق الحالي المكلف                               │
│  ┌───────────────────────────────────────────────────┐ │
│  │  [👤] أحمد السعيد              [ 🟢 متصل على المهمة ] │ │
│  │      كود: DR-1025 • 🚗 تويوتا كامري • أبيض        │ │
│  │      لوحة: 12-34567                               │ │
│  │      📞 +965 50 123 4567        [ 📞 اتصال سريع ] │ │
│  └───────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────┤
│  📝 تفاصيل البلاغ                                      │
│  ┌───────────────────────────────────────────────────┐ │
│  │  العميل لم يستجب للاتصال بعد 3 محاولات متتالية،   │ │
│  │  تم الانتظار لمدة 10 دقائق عند مدخل البناية دون    │ │
│  │  أي استجابة.                                       │ │
│  │                                                   │ │
│  │  📷 صور الإثبات المرفقة (2):                       │ │
│  │  [ 🖼️ مصغرة 1 ]    [ 🖼️ مصغرة 2 ]                │ │
│  └───────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────┤
│  📦 تفاصيل الطلب والتوصيل                              │
│  ┌───────────────────────────────────────────────────┐ │
│  │  العميل: محمد الفضلي      • 🍱 2 وجبة              │ │
│  │  الفترة: 12:30 م - 02:00 م                         │ │
│  │  🏪 الاستلام: مطعم دايت كير - فرع السالمية         │ │
│  │  🏠 التسليم: السالمية، قطعة 4، شارع 12، منزل 15   │ │
│  └───────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────┤
│  -- في حال كانت المشكلة محلولة مسبقاً (status == "Resolved") --
│  ┌───────────────────────────────────────────────────┐ │
│  │  ✅ تم حل المشكلة                                  │ │
│  │  بواسطة: محمد أحمد (مسؤول التوصيل)                │ │
│  │  التاريخ: 22 سبتمبر 2026، 11:45 ص                  │ │
│  │  الملاحظات: تم التواصل مع العميل وتسليم البوكس     │ │
│  └───────────────────────────────────────────────────┘ │
│  -- في حال كانت المشكلة مفتوحة أو قيد المعالجة --      │
│     [ 🔄 تعيين سائق بديل ]      [ ✅ تم حل المشكلة ]    │
└────────────────────────────────────────────────────────┘
```

---

## 🔌 عقود تكامل الخادم (API Endpoints Specification)

### 1. جلب تفاصيل المشكلة المعمقة
- **الرابط (Route)**: `GET /api/v1/dispatcher/support/issues/{issueId}`
- **الصلاحية (Authorization)**: `Bearer Token` (`DeliveryManager` أو `Admin`)
- **Headers**: `Accept-Language: ar|en`

#### نموذج الاستجابة المتوقعة لمشكلة مفتوحة (`HTTP 200 OK`):
```json
{
  "issueId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "العميل لم يستجب للاتصال بعد 3 محاولات",
  "category": "DeliveryIssue",
  "categoryLabel": "مشكلة توصيل",
  "categoryColor": "#EF4444",
  "createdAtUtc": "2026-09-22T08:20:00Z",
  "reportedTimeText": "منذ 15 دقيقة",
  "status": "Open",
  "statusLabel": "مفتوحة",
  "metadata": {
    "boxCode": "#BX-10256",
    "area": "السالمية، ق 4",
    "affectedBoxesCount": 1,
    "affectedBoxesText": "1 بوكس متأثر",
    "priority": "High",
    "priorityText": "أولوية عالية",
    "priorityColor": "#EF4444"
  },
  "driver": {
    "driverId": "1a2b3c4d-5717-4562-b3fc-2c963f66afa6",
    "driverCode": "DR-1025",
    "fullName": "أحمد السعيد",
    "avatarUrl": "https://cdn.mealmate.app/avatars/dr-1025.jpg",
    "status": "OnTask",
    "statusText": "متصل على المهمة",
    "statusColor": "#10B981",
    "phoneNumber": "+965 50 123 4567",
    "vehicleInfo": "تويوتا كامري • أبيض",
    "vehicle": {
      "type": "سيارة",
      "model": "تويوتا كامري",
      "color": "أبيض",
      "plateNumber": "12-34567"
    }
  },
  "description": "العميل لم يستجب للاتصال بعد 3 محاولات متتالية، تم الانتظار لمدة 10 دقائق عند مدخل البناية دون أي استجابة.",
  "evidencePhotos": [
    {
      "id": "11111111-2222-3333-4444-555555555551",
      "url": "https://cdn.mealmate.app/evidence/photo_01.jpg",
      "thumbnailUrl": "https://cdn.mealmate.app/evidence/thumb_01.jpg",
      "uploadedAtUtc": "2026-09-22T08:21:00Z"
    },
    {
      "id": "11111111-2222-3333-4444-555555555552",
      "url": "https://cdn.mealmate.app/evidence/photo_02.jpg",
      "thumbnailUrl": "https://cdn.mealmate.app/evidence/thumb_02.jpg",
      "uploadedAtUtc": "2026-09-22T08:21:30Z"
    }
  ],
  "tripInfo": {
    "customerName": "محمد الفضلي",
    "mealsCount": 2,
    "mealsCountText": "2 وجبة",
    "expectedDeliveryFromUtc": "2026-09-22T09:30:00Z",
    "expectedDeliveryToUtc": "2026-09-22T11:00:00Z",
    "expectedDeliveryTimeText": "12:30 م - 02:00 م",
    "pickupLocation": "مطعم دايت كير - فرع السالمية",
    "deliveryAddress": "السالمية، قطعة 4، شارع 12، منزل 15"
  },
  "resolution": null
}
```

#### نموذج الاستجابة لمشكلة تم حلها مسبقاً (`status: "Resolved"`):
```json
{
  "issueId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "العميل لم يستجب للاتصال بعد 3 محاولات",
  "category": "DeliveryIssue",
  "categoryLabel": "مشكلة توصيل",
  "categoryColor": "#EF4444",
  "createdAtUtc": "2026-09-22T08:20:00Z",
  "reportedTimeText": "منذ ساعتين",
  "status": "Resolved",
  "statusLabel": "تم الحل",
  "metadata": { ... },
  "driver": { ... },
  "description": "...",
  "evidencePhotos": [ ... ],
  "tripInfo": { ... },
  "resolution": {
    "notes": "تم التواصل مع العميل هاتفياً وتم استلام الطلب بنجاح",
    "resolvedAtUtc": "2026-09-22T08:45:00Z",
    "resolvedById": "a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11",
    "resolvedByName": "محمد أحمد"
  }
}
```

---

### 2. تسجيل حل المشكلة وإغلاقها
- **الرابط (Route)**: `POST /api/v1/dispatcher/support/issues/{issueId}/resolve`
- **الصلاحية (Authorization)**: `Bearer Token` (`DeliveryManager` أو `Admin`)

#### جسم الطلب (Request Body):
```json
{
  "resolutionNotes": "تم التواصل مع العميل وتسليم الوجبات بنجاح"
}
```

#### قواعد حقل `resolutionNotes`:
1. **إجباري (Required)**: لا يقبل قيمة `null` أو مسافات فارغة فقط (`Whitespace`).
2. **اقتطاع المسافات (Trimmed)**: يتم إزالة المسافات من البداية والنهاية آلياً.
3. **الطول الأدنى والأقصى**: الحد الأدنى 3 أحرف، والحد الأقصى 500 حرف.
4. **في حال عدم مطابقة الشروط**: يرجع الخادم كود `400 Bad Request` مع:
   ```json
   {
     "code": "NOTES_INVALID",
     "message": "ملاحظات الحل مطلوبة ويجب أن تكون بين 3 و 500 حرف",
     "errors": {}
   }
   ```

#### منع تكرار عملية الحل (Concurrency / Duplicate Resolve Guard):
- إذا استُدعي `POST /resolve` على مشكلة حالتها `Resolved` أو `Closed` مسبقاً، يرجع الخادم:
  **HTTP 409 Conflict**:
  ```json
  {
    "code": "ISSUE_ALREADY_RESOLVED",
    "message": "تم حل المشكلة بالفعل",
    "errors": {}
  }
  ```

#### استجابة النجاح المتوقعة (`HTTP 200 OK`):
```json
{
  "issueId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Resolved",
  "statusLabel": "تم الحل",
  "resolution": {
    "notes": "تم التواصل مع العميل وتسليم الوجبات بنجاح",
    "resolvedAtUtc": "2026-09-22T08:45:00Z",
    "resolvedById": "a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11",
    "resolvedByName": "محمد أحمد"
  },
  "resolvedAtUtc": "2026-09-22T08:45:00Z",
  "message": "تم حل المشكلة بنجاح"
}
```

---

## 🔄 الربط مع شاشة تعيين السائق البديل (Screen 04.13)
1. **التنقل (Navigation)**:
   - عند النقر على زر `[ 🔄 تعيين سائق بديل ]`، يتم فتح شاشة `Screen 04.13` مع تمرير `issueId` كمعامل مسار فقط (`Route parameter`).
2. **عقود شاشة 04.13**:
   - `GET /api/v1/dispatcher/support/issues/{issueId}/candidates?pageNumber=1&pageSize=20`
   - `POST /api/v1/dispatcher/support/issues/{issueId}/reassign`
3. **استقبال النتيجة بدون استدعاء إضافي (Result Passing)**:
   - عند نجاح التعيين في `04.13`، تُغلق الشاشة وتُرجع كائن النتيجة المكتمل (`ReassignmentResult`) إلى `Screen 04.12`.
   - تقوم شاشة `04.12` بتحديث كارت السائق المعروض فورياً بناءً على:
     - `replacementDriverCode`
     - `driverName`
     - `issueStatus: "InProgress"`
   - لا حاجة لإعادة طلب `GET /api/v1/dispatcher/support/issues/{issueId}` ولا لعمل Polling.

---

## ⚠️ معالجة الأخطاء (Error Handling & HTTP Status Codes)
الصيغة الموحدة متوافقة تماماً مع معايير مشروع الموبايل `lib/core/errors`:
```json
{
  "code": "ERROR_CODE",
  "message": "رسالة واضحة للمستخدم",
  "errors": {}
}
```

| كود الحالة | كود الخطأ البرمجي (`code`) | السبب والتعامل في الواجهة |
| :--- | :--- | :--- |
| **`400 Bad Request`** | `NOTES_INVALID` | ملاحظات الحل فارغة أو أقل من 3 أو أكبر من 500 حرف. تُعرض رسالة الخطأ للمستخدم في نافذة التأكيد. |
| **`401 Unauthorized`** | `UNAUTHORIZED` | جلسة غير صالحة، تحويل فوري لصفحة الدخول. |
| **`403 Forbidden`** | `FORBIDDEN` | المستخدم ليس لديه صلاحية `DeliveryManager`. |
| **`404 Not Found`** | `ISSUE_NOT_FOUND` | المشكلة غير موجودة في النظام. تُعرض شاشة `EmptyStateWidget`. |
| **`409 Conflict`** | `ISSUE_ALREADY_RESOLVED` | تم حل البلاغ مسبقاً من قِبل مسؤول آخر. يُعرض الخطأ عبر `InlineApiErrorWidget` وتتحول حالة الشاشة لعرض كارت الحل. |
| **`422 Unprocessable`** | `VALIDATION_FAILED` | بيانات غير مقبولة منطقياً. |
| **`500 Internal Error`** | `INTERNAL_SERVER_ERROR` | خطأ داخلي في الخادم، إتاحة زر إعادة المحاولة. |

---

## 📱 المتطلبات والتدفق البرمجي في تطبيق الموبايل (Flutter Architecture)

### 1. معمارية الكود (Clean Architecture Layers)
- **Route**: يستقبل `String issueId` فقط (بدلاً من انتظار Entity كامل أو Fake Data).
- **ApiServices**: دالتان: `getProblemDetails(String issueId)` و `resolveIssue(String issueId, String notes)`.
- **RemoteDataSource & Repository**: تغليف الاستجابة في `Either<Failure, DispatcherProblemDetailsEntity>`.
- **ViewModel / Cubit**: إدارة الحالات التالية:
  - `IssueDetailsLoading`: أول تحميل.
  - `IssueDetailsLoaded`: نجاح جلب التفاصيل.
  - `IssueDetailsError`: فشل الجلب بدون بيانات سابقة.
  - `IssueResolving`: أثناء تنفيذ طلب الحل (تعطيل الزر وإظهار مؤشر تقدم دائري داخل الزر).
  - `IssueResolveError`: فشل الحل (إبقاء المحتوى وظهور `InlineApiErrorWidget` داخل الدايلوج).
  - `IssueResolvedSuccess`: نجاح الحل، تحديث الواجهة فوراً لعرض كارت الحل، وتمرير النتيجة عند الرجوع لـ `Screen 04.11`.

### 2. حالات العرض البصري (Visual States)
- **أول تحميل**: عرض `DispatcherIssueDetailsShimmer` باستخدام مكون `ShimmerWidget`.
- **فشل التحميل الأولي**: عرض ويدجت `ApiErrorWidget.fromTypedFailure` مع زر إعادة المحاولة (`Retry`).
- **حالة المشكلة غير الموجودة**: عرض `EmptyStateWidget`.
- **الصور**:
  - في الكارت: استخدام `thumbnailUrl` مع `AppCachedNetworkImage` مع صورة بديلة (`fallback placeholder`).
  - عند الضغط على الصورة: فتح عارض ملء الشاشة (`Full-screen preview`) مع دعم التكبير المزدوج (`InteractiveViewer / Zoom & Pinch`) باستخدام الرابط الأصلي `url`.
- **لا يوجد Polling**: الواجهة تعتمد بالكامل على ردود الـ API وأحداث التنقل دون أي استقصاء دوري.
