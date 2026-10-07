> Payment integration updated 2026-10-06: use the [Payzah contract](<F:/MealMate/backend/docs/mobile/payzah-payments.md>). Any old-provider payment endpoints, method IDs, credential/webhook fields or refund instructions below are superseded; they are retained only as historical context for this mixed guide.

# ربط تطبيق الموبايل — اشتراك العائلة

> **عقد الشاشات (ملف لكل شاشة — استخدم ده للتنفيذ):**  
> [`family-screens/README.md`](./family-screens/README.md)

> **الدليل التشغيلي الكامل بأمثلة (باقات الحجم + الصور):**  
> [`family-subscription-mobile-flow-guide.md`](./family-subscription-mobile-flow-guide.md)

دليل Flutter / Customer App للربط على الـ API الجاهز. العملة **KWD**. JSON **camelCase**.

**Auth:** `Authorization: Bearer <customer JWT>` على كل الطلبات. غير العميل → `403`.

**لا تستخدم** `POST /api/v1/family_subscription/members` (قديم: إضافة أفراد بعد الدفع). الفلو الجديد كله تحت:

`https://{host}/api/v1/customer/family-subscription-flow`

---

## 1) شاشات التصميم → الـ APIs

| شاشة الموبايل | ماذا تعمل | APIs |
| --- | --- | --- |
| 1. إنشاء العائلة | اسم العائلة + اختيار باقة الحجم + أفراد | `GET packages` · `GET relationships` · `GET metadata` · `PUT group` (`familyPackageId`) · `GET current` · CRUD `members` · عناوين |
| 2. البرنامج | برنامج **لكل فرد** عبر شريط أفراد + كتالوج | `members[]` من `GET current` · كتالوج `family-subscription-flow/programs` · لاحقًا `PUT members/{id}/plan` |
| 3. الباقة والأيام | باقة + مدة + جمعة + تصنيف **لكل فرد** (نفس الشريط) | عائلة: `bundles` · فردي: `durations` · `tiers` ثم `PUT members/{id}/plan` |
| 4. الدفع | ملخص العائلة + طرق دفع + تأكيد (اعرض كل الأفراد) | `GET summary` · `GET payment-methods` · `POST checkout` ثم WebView لـ MyFatoorah |

### تحويل الاشتراك الفردي → عائلة (Phase 1)

> **دليل الموبايل المفصّل (ملف لكل جزء):**  
> [`individual-to-family-conversion/README.md`](./individual-to-family-conversion/README.md)

إذا كان لدى العميل اشتراك فردي حالي، **لا** تبدأ بـ `PUT group`. استخدم مسار التحويل:

| خطوة | API |
| --- | --- |
| أنواع الاشتراك | `GET /api/v1/customer/subscription-flow/types` → عند وجود فردي: `INDIVIDUAL.isAvailable=false` + `disabledReason`، و`FAMILY.requiresConversion=true` + `conversionEndpoint` |
| خيارات التحويل | `GET /api/v1/customer/subscriptions/current/conversion-options` |
| تحويل وأصبح مديرًا | `POST /api/v1/customer/family-subscription-flow/convert-from-individual` |
| إلغاء مسودة التحويل | `POST /api/v1/customer/family-subscription-flow/abandon-draft` (يعيد ربط الاشتراك الفردي ويحذف الـDraft) |
| ثم أكمل إضافة الأعضاء | نفس CRUD الأعضاء + `summary` + `checkout` |

- `CONVERT_TO_FAMILY_MANAGER` → متاح عندما الاشتراك الفردي `Paid` / `Scheduled` / `Active` / `Frozen` / `SuspendedForReview`.
- `PendingPayment` → `available: false` وسبب `conversion.individual_not_convertible`.
- `CHANGE_INDIVIDUAL_PLAN` → دائمًا `available: false` في Phase 1 (`change_individual_plan.not_available`).
- بعد التحويل: اشتراك المدير يبقى **نفس** `subscriptionId`؛ خطة المدير **مقفولة** (`conversion.manager_plan_locked` على `PUT .../plan`).
- في `GET summary`: `managerPrepaid`, `managerSubscriptionId`, `newMembersCount`, `newMembersSubtotal` + سعر المدير `0`؛ الخصم على الأعضاء الجدد فقط؛ الدفع يغطي الأعضاء الجدد فقط.
- `PUT group` بدون تحويل مسبق مع وجود اشتراك فردي قابل للتحويل → `409 conversion.convert_required`.
- أخطاء التحويل: `conversion.no_individual_subscription` · `conversion.already_in_family` (عائلة Active أو CheckoutPending غير محوّل) · `conversion.individual_not_convertible`. مسودة Draft قديمة بدون تحويل تُعاد استخدامها في نفس الـPOST.

**UI إلزامي:** شريط `members[]` يظهر في الشاشات 2 و 3 و 4؛ `selectedMemberId` محلي؛ كل فرد يختار برنامجه وتصنيفه لوحده. التفاصيل: القسم `3.5.1` في الدليل الكامل.

`currentStep` من `GET current` / `PUT group`:

| قيمة | معنى |
| --- | --- |
| `1` | إعداد الملفات الشخصية |
| `2` | فيه خطة ناقصة |
| `3` | كل الأفراد `Ready` — جاهز للدفع |
| `4` | `CheckoutPending` أو `Active` |

التصنيفات في المنتج هي **Basic / Platinum / Elite** (أرقام `1 / 2 / 3`) وليس Select/Plus/Max. المدد من كتالوج الأدمن (افتراضي 6 / 12 / 26 + مخصص) وليست 7/14/30 ثابتة.

---

## 2) Enums (System.Text.Json — رقم افتراضيًا)

اقبل **رقم**؛ إن وصلك اسم نصي تعامل معه كـ fallback.

| Enum | قيم |
| --- | --- |
| `status` مجموعة | `1` Draft · `2` CheckoutPending · `3` Active · `4` Frozen · `5` Dispute · `6` PendingDetach · `7` Cancelled |
| `role` | `1` Manager · `2` Member |
| `setupStatus` | `1` NotStarted · `2` InProgress · `3` Ready |
| `gender` | `1` Male · `2` Female |
| `selectedTier` / `tier` | `1` Basic · `2` Platinum · `3` Elite |

في الطلبات أرسل نفس الأرقام (مثال: `"gender": 1`, `"tier": 2`).

---

## 3) متى الفرد `Ready`؟

لازم معًا:

- `displayName` غير فارغ
- `ageYears` من 1 إلى 120
- `deliveryAddressId` عنوان يخص نفس العميل (`/api/v1/customers/me/addresses`)
- `programId` + `bundleId` + `selectedTier` + `durationDays`

بدون ذلك `canCheckout` يبقى `false` وسبب `family.member_incomplete`.

الخصم العائلي يطبّق إذا العدد ضمن نطاق **الباقة المختارة** (مثل 2–4 → 2 أو 3 أو 4). الدفع يتقفل تحت `minMembers` الباقة. السقف `maxMembers` الباقة.

```http
GET /api/v1/customer/family-subscription-flow/packages
PUT /api/v1/customer/family-subscription-flow/group
{ "name": "عائلة أحمد", "familyPackageId": "<guid>" }
```

حقول الباقة من `GET packages` (camelCase): `id`, `nameAr`, `nameEn`, `minMembers`, `maxMembers`, `discountPercent`, `isActive`, `sortOrder`, `imageStorageKey`, `imageUrl`.

إدارة الأدمن: **الاشتراكات والبرامج → باقات العائلة** (رفع صورة اختيارية لكل باقة). صفحة التسعير فيها رابط فقط.

لا تحذف المدير → `family.cannot_delete_manager`.

---

## 4) نقطة الدخول

```http
GET /api/v1/customer/subscription-flow/types
```

اختَر `code: "FAMILY"` و `membersConfiguredAfterPayment: false`.

بعدها ابدأ فلو العائلة. فلو الفرد يبقى:

`GET /api/v1/customer/subscription-flow/...` + `POST /api/v1/customer/subscriptions/checkout`.

---

## 5) كتالوج العائلة (مفلتر)

نفس كيانات الفردي، لكن الموبايل في فلو العائلة يستدعي مسارات العائلة للبرامج/الباقات فقط (ما يظهر إلا ما فعّله الأدمن بـ `availableForFamily` على البرنامج **والباقة** معاً):

```http
GET /api/v1/customer/family-subscription-flow/programs
GET /api/v1/customer/family-subscription-flow/programs/{programId}/bundles
```

بعد اختيار برنامج+باقة عائليين، المدد والتصنيفات والمطاعم تبقى من مسارات الفردي (نفس الـ IDs):

```http
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=false
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?durationDays=26&includeFriday=false
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants
```

`PUT .../members/{id}/plan` يرفض زوج غير عائلي بـ `family.catalog_not_available`.

`tier` في المسار: `1` أو `Basic` حسب ما يستخدمه الفردي عندكم.

لا تستخدم `subscription-flow/programs` داخل شاشات العائلة — ذلك يعرض كتالوج الفردي الكامل.

---

## 6) عناوين التوصيل

عنوان **مستقل لكل فرد**. طريقة الإدخال نفس الفردي: خريطة (lat/lng داخل الكويت) أو يدوي (منطقة/قطعة من الكتالوج).

لا تحفظ عنوان فرد تاني على `POST /api/v1/customers/me/addresses` — ده عنوان المدير فقط.

```http
GET    /api/v1/customer/family-subscription-flow/members/{memberId}/addresses
POST   /api/v1/customer/family-subscription-flow/members/{memberId}/addresses
PUT    /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/{addressId}
POST   /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/preview-coverage
GET    /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/{addressId}/preview-coverage
POST   /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/{addressId}/set-default
```

نفس جسم `CustomerAddressRequest` / `PreviewAddressCoverageRequest` الفردي. التفاصيل: [family-screens/06-address.md](./family-screens/06-address.md).

---

## 7) فلو العائلة — الطلبات

Base: `/api/v1/customer/family-subscription-flow`

### 7.1 Metadata + صلة القرابة

```http
GET /metadata
GET /relationships
```

`GET /relationships` قائمة صلة القرابة. في شاشة إضافة فرد استخدم العناصر التي `forDependents: true` (استبعد `self` — ده المدير فقط).

```json
[
  { "code": "self", "nameAr": "أنت", "nameEn": "You", "forDependents": false },
  { "code": "spouse", "nameAr": "زوج / زوجة", "nameEn": "Spouse", "forDependents": true },
  { "code": "father", "nameAr": "أب", "nameEn": "Father", "forDependents": true },
  { "code": "mother", "nameAr": "أم", "nameEn": "Mother", "forDependents": true },
  { "code": "child", "nameAr": "ابن / ابنة", "nameEn": "Child", "forDependents": true },
  { "code": "sibling", "nameAr": "أخ / أخت", "nameEn": "Sibling", "forDependents": true },
  { "code": "friend", "nameAr": "صديق / صديقة", "nameEn": "Friend", "forDependents": true },
  { "code": "parent", "nameAr": "والد / والدة", "nameEn": "Parent", "forDependents": true },
  { "code": "other", "nameAr": "أخرى", "nameEn": "Other", "forDependents": true }
]
```

أرسل `relationshipCode` في `POST/PUT members` بنفس الـ `code`.

`GET /metadata` لسه فيه `relationships` بنفس القائمة، بالإضافة لـ `maxMembers` و`discountPercent`.

### 7.1.1 صلة القرابة

```http
GET /relationships
```

قائمة ثابتة لشاشة اختيار صلة القرابة. استخدم العناصر التي `forDependents: true` عند إضافة فرد (استبعد `self` — ده المدير).

```json
[
  { "code": "self", "nameAr": "أنت", "nameEn": "You", "forDependents": false },
  { "code": "spouse", "nameAr": "زوج / زوجة", "nameEn": "Spouse", "forDependents": true },
  { "code": "father", "nameAr": "أب", "nameEn": "Father", "forDependents": true },
  { "code": "mother", "nameAr": "أم", "nameEn": "Mother", "forDependents": true },
  { "code": "child", "nameAr": "ابن / ابنة", "nameEn": "Child", "forDependents": true },
  { "code": "sibling", "nameAr": "أخ / أخت", "nameEn": "Sibling", "forDependents": true },
  { "code": "friend", "nameAr": "صديق / صديقة", "nameEn": "Friend", "forDependents": true },
  { "code": "parent", "nameAr": "والد / والدة", "nameEn": "Parent", "forDependents": true },
  { "code": "other", "nameAr": "أخرى", "nameEn": "Other", "forDependents": true }
]
```

أرسل `relationshipCode` في `POST/PUT members` بنفس الـ `code`. المدير يبقى `self`.

### 7.2 الحالة الحالية

```http
GET /current
```

- `200` + `FamilyGroupScreenDto` — أول عنصر في `members` هو المستخدم الحالي (`role: 1` Manager، `isCurrentUser: true`)
- لو مفيش مسودة: السيرفر ينشئ `Draft` باسم `عائلة {الاسم من البروفايل}` ويملأ بيانات المدير من البروفايل (الاسم، العمر من تاريخ الميلاد، العنوان الافتراضي)
- `404` + `code: "family.not_found"` فقط لو فيه مجموعة `Active` بالفعل (مش مسودة)

بيانات المدير من:

- `GET /api/v1/customers/me` → الاسم و`dateOfBirth`
- `GET /api/v1/customers/me/addresses` → العنوان الافتراضي (`isDefault`)

الجنس غير موجود على البروفايل؛ الافتراضي ذكر لحين تعديله من شاشة الفرد.

### 7.3 إنشاء / تعديل اسم العائلة

```http
PUT /group
Content-Type: application/json

{ "name": "عائلة أحمد" }
```

ينشئ مجموعة `Draft` ويضيف المدير تلقائيًا من البروفايل (`relationshipCode: "self"`, `isCurrentUser: true`).  
إن وُجد مسودة: يحدّث الاسم فقط ويكمل خانات المدير الفاضية من البروفايل.  
إن وُجد اشتراك عائلي `Active` → `409 family.active_exists`.

### 7.4 أفراد

```http
POST /members
PUT  /members/{memberId}
DELETE /members/{memberId}
```

جسم الكتابة:

```json
{
  "displayName": "سارة",
  "relationshipCode": "spouse",
  "gender": 2,
  "ageYears": 28,
  "deliveryAddressId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

مهم للموبايل:

- `deliveryAddressId` لازم يكون Guid من `GET /api/v1/customers/me/addresses`، أو `null` / احذف الحقل.
- **ممنوع** تبعت `""` فاضي — كان بيسبب `400` model binding. السيرفر دلوقتي يقبل `""` كـ `null` ويحافظ على العنوان السابق إن وُجد.
- **متبعتش** `nutritionalGoalCode` ولا `activityLevel` (اتشالوا من العقد).
- بدون عنوان صالح الفرد مش هيبقى `Ready` و`canCheckout` يفضل `false`.

المدير: `relationshipCode` يبقى `self` حتى لو أرسلت قيمة أخرى.  
إضافة فوق الحد → `409 family.max_members`.

استجابة الشاشة:

```json
{
  "familyGroupId": "...",
  "name": "عائلة أحمد",
  "status": 1,
  "maxMembers": 4,
  "discountPercent": 0.1,
  "currentStep": 1,
  "members": [
    {
      "memberId": "...",
      "role": 1,
      "displayName": "أحمد",
      "relationshipCode": "self",
      "gender": 1,
      "ageYears": 24,
      "deliveryAddressId": "...",
      "setupStatus": 1,
      "programId": null,
      "bundleId": null,
      "selectedTier": null,
      "durationDays": null,
      "includeFriday": false,
      "isCurrentUser": true
    }
  ]
}
```

### 7.5 خطة الفرد (شاشة 2 + 3)

الحفظ **جزئي** على نفس المسار: ابعت الحقول المتغيرة فقط عند كل ضغطة؛ السيرفر يدمج مع القيم الحالية.

```http
PUT /members/{memberId}/plan
{ "programId": "..." }
```

```http
PUT /members/{memberId}/plan
{
  "bundleId": "...",
  "tier": 1,
  "durationDays": 26,
  "includeFriday": false
}
```

ما زال مسموح إرسال الخطة كاملة في طلب واحد.  
الحقل المتساب = يفضل · JSON `null` = مسح · جسم فاضي → `family.plan_empty`.  
تغيير برنامج يمسّح الباقة/التصنيف لو مش متوافقين.

المجموعة لازم `Draft` وإلا `family.not_draft`.  
مدة غير مسموحة → `family.duration_not_available`.  
جمعة بدون مطاعم → `family.friday_unavailable`.

كرّر لكل فرد (مع شريط الأفراد) ثم انتقل للدفع عندما الكل `setupStatus === 3`.

### 7.6 الملخص / شاشة الدفع (شاشة 4)

```http
GET /summary
GET /summary?memberId={guid}
```

بدون `memberId` يختار المدير. **تبديل الفرد في الشريط** = نفس الطلب مع `memberId` الجديد → يتغيّر `selectedMember` فقط؛ الإجمالي و`mealOverview` ثابتين.

| قسم الشاشة | الحقول |
| --- | --- |
| شريط الأفراد | `members[]` (`setupStatus` / جاهز لما = 3) |
| ملخص الاشتراك للفرد المحدد | `selectedMember` |
| تفاصيل الأسعار للكل | `subtotal` · `familyDiscountPercent` · `familyDiscountAmount` · `finalPrice` · `currencyCode` |
| جدول نظرة عامة على العائلة | `mealOverview[]` |

**`selectedMember` (مهم للتصميم):**

- `memberId`, `displayName`, `role`, `isManager`, `roleAr` («عضو رئيسي» للمدير), `roleEn`
- `setupStatus`, `isReady`
- `programId`, `programNameAr`, `programNameEn`
- `bundleId`, `bundleNameAr`, `bundleNameEn`
- `selectedTier`, `tierNameAr`, `tierNameEn`
- `durationDays`, `durationLabelAr` (مثل `26 يوم`), `durationLabelEn`, `includeFriday`
- `breakfast` / `lunch` / `snack` / `dinner`, `mealsPerDay` + counters
- `memberPrice`

**`mealOverview[]` (الجدول):**

- `memberId`, `displayName`, `role`, `isManager`, `setupStatus`, `isReady`
- `breakfast`, `lunch`, `snack`, `dinner`, `mealsPerDay`

لا يوجد سطر ضريبة في الـ API. طرق الدفع منفصل: `GET /payment-methods?amount={finalPrice}`.

`canCheckout === true` فقط إذا: `Draft` + عدد الأفراد ضمن باقة الحجم + الكل Ready + `finalPrice > 0`.

مثال مختصر:

```json
{
  "familyGroupId": "...",
  "familyName": "عائلة أحمد",
  "selectedMemberId": "...",
  "selectedMember": {
    "memberId": "...",
    "displayName": "أحمد",
    "isManager": true,
    "roleAr": "عضو رئيسي",
    "isReady": true,
    "programNameAr": "متوازن",
    "bundleNameAr": "4 وجبات + سناك",
    "durationDays": 26,
    "durationLabelAr": "26 يوم",
    "selectedTier": 1,
    "tierNameAr": "أساسي",
    "breakfast": true,
    "lunch": true,
    "snack": true,
    "dinner": true,
    "mealsPerDay": 4,
    "memberPrice": 150.000
  },
  "subtotal": 300.000,
  "familyDiscountPercent": 0.10,
  "familyDiscountAmount": 30.000,
  "finalPrice": 270.000,
  "currencyCode": "KWD",
  "canCheckout": true,
  "checkoutBlockReason": null,
  "checkoutEndpoint": "POST /api/v1/customer/family-subscription-flow/checkout",
  "mealOverview": [
    {
      "memberId": "...",
      "displayName": "أحمد",
      "isManager": true,
      "isReady": true,
      "breakfast": true,
      "lunch": true,
      "snack": true,
      "dinner": true,
      "mealsPerDay": 4
    },
    {
      "memberId": "...",
      "displayName": "سارة",
      "isManager": false,
      "isReady": true,
      "breakfast": true,
      "lunch": true,
      "snack": false,
      "dinner": true,
      "mealsPerDay": 3
    }
  ]
}
```

### 7.7 طرق الدفع

```http
GET /payment-methods?amount=270.000
```

`amount` = `finalPrice` من الملخص ويجب `> 0`.

عنصر نموذجي (مثل الفردي): `paymentMethodId`, `code`, `nameAr`, `nameEn`, `integrationType` (`MYFATOORAH_HOSTED` أو `WALLET`), `isAvailable`, `unavailableReason`.

المحفظة غالبًا `isAvailable: false` و `unavailableReason: "wallet_not_implemented"`.

بعض أخطاء هذا المسار ترجع `{ "error": "subscription_flow.invalid_amount" }` وليس ProblemDetails.

### 7.8 الدفع

```http
POST /checkout

{
  "callbackUrl": "mealmate://checkout/success",
  "errorUrl": "mealmate://checkout/error",
  "correlationId": "optional-client-id",
  "paymentMethodId": 1
}
```

`callbackUrl` / `errorUrl` لازم scheme/host مسموح (`https` أو `mealmate` حسب إعداد السيرفر). وإلا `400 checkout.redirect_url_not_allowed`.

نجاح `200`:

```json
{
  "familyGroupId": "...",
  "paymentAnchorSubscriptionId": "...",
  "paymentUrl": "https://....myfatoorah.com/...",
  "holdExpiresAtUtc": "2026-08-19T16:30:00Z",
  "finalPrice": 270.000
}
```

افتح `paymentUrl` في WebView / متصفح. بعد الرجوع:

```http
GET /api/v1/customer/subscriptions
```

المتوقع: **اشتراكان أو أكثر** لنفس المدير، نفس `familyGroupId`. الحالة تتحول إلى `CheckoutPending` ثم `Active` بعد تأكيد البوابة.

لا تخفّض سعر المطعم بالخصم العائلي في الواجهة — الخصم على الإجمالي الظاهر للعميل فقط.

إعادة `POST checkout` والمجموعة `CheckoutPending` → `409 family.not_draft` (لا تنشئ فاتورة ثانية قبل انتهاء الحجز/الفشل).

---

## 8) أخطاء

معظم أخطاء MediatR:

```json
{
  "type": "...",
  "title": "...",
  "status": 409,
  "detail": "...",
  "code": "family.max_members"
}
```

اقرأ **`code`**. HTTP:

| status | معنى تقريبي |
| --- | --- |
| 400 | تحقق / مدة / عنوان تحويل |
| 404 | لا توجد مسودة / فرد |
| 409 | تعارض (حد الأفراد، اشتراك نشط، مش Draft، عضو ناقص، جمعة…) |
| 502 | بوابة الدفع |

| `code` | متى |
| --- | --- |
| `family.not_found` | لا مسودة |
| `family.name_required` | اسم فارغ |
| `family.active_exists` | فيه عائلة Active |
| `family.max_members` | تعديت الحد |
| `family.min_members` | أقل من فردين عند الدفع |
| `family.member_incomplete` | فرد مش Ready |
| `family.not_draft` | تعديل/دفع والمجوعة ليست Draft |
| `family.cannot_delete_manager` | حذف المدير |
| `family.duration_not_available` | مدة مرفوضة |
| `family.friday_unavailable` | الجمعة غير متاحة |
| `family.catalog_not_available` | برنامج/باقة غير متاحين لاشتراك العائلة |
| `family.invalid_amount` | `finalPrice <= 0` |
| `family.address_required` | عنوان مفقود (إن رجع) |
| `checkout.redirect_url_not_allowed` | رابط رجوع غير مسموح |
| `payment.gateway_not_configured` | البوابة غير مهيأة |
| `payment.initialization_failed` | فشل إنشاء فاتورة MyFatoorah |

---

## 9) ترتيب الاستدعاء المقترح (Flutter)

```text
1. GET subscription-flow/types          → المستخدم اختار FAMILY
2. GET family-subscription-flow/metadata
3. GET family-subscription-flow/current
     يظهر المستخدم ضمن members (isCurrentUser)
     404 فقط لو فيه عائلة Active
     تقدر تعدّل الاسم بـ PUT /group
4. GET /api/v1/customers/me/addresses    → picker الموقع
5. PUT/POST members لكل فرد حتى setupStatus يسمح بالخطة
6. GET family-subscription-flow/programs → bundles
   ثم subscription-flow/.../durations → tiers  (لكل فرد)
7. PUT members/{id}/plan لكل فرد
8. GET summary  حتى canCheckout == true
9. GET payment-methods?amount={finalPrice}
10. POST checkout → افتح paymentUrl
11. GET /api/v1/customer/subscriptions  (نفس familyGroupId)
```

---

## 10) خارج النطاق (لا تنتظره من الـ API)

- شاشات Flutter نفسها
- حساب login لكل طفل
- تجميد/فصل أفراد من الموبايل في هذا العقد
- ضريبة 15% منفصلة (السعر النهائي من المحرك + خصم العائلة)

مرجع تقني إضافي: `docs/superpowers/specs/2026-08-19-family-subscription-checkout-contract.md`
