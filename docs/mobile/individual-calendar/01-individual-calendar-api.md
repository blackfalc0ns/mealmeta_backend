# تقويم الاشتراك الفردي — عقد ربط الموبايل (API الحي)

> **الجمهور:** Flutter.  
> **Base URL الحي المُختبر:** `http://maelmate.runasp.net`  
> **تاريخ التحقق:** 2026-08-25  
> **شاشة السلوك:** [`00-individual-calendar-flow.md`](./00-individual-calendar-flow.md)  
> **قبل التقويم:** [`../../mobile-customer-subscription-flow.md`](../../mobile-customer-subscription-flow.md) + [`../../mobile-checkout-flow.md`](../../mobile-checkout-flow.md)

هذا الملف يصف **ما يعمل الآن على السيرفر المرفوع** (Swagger الحي)، بعد تجربة فلو كامل: تسجيل → عنوان/حساسية → checkout → تفعيل الاشتراك → ظهور التقويم → اختيار وجبات.

---

## 0. نتيجة التجربة الحية (مختصر)

| الخطوة | النتيجة |
|--------|---------|
| `POST /api/v1/auth/customers/register` + `verify-phone` بـ OTP `123456` | نجاح |
| عنوان توصيل + تأكيد حساسية/نظام غذائي (حتى لو «لا يوجد») | مطلوب قبل الملخص |
| `GET .../summary` ثم `POST .../checkout` | نجاح → `PendingPayment` + `paymentUrl` MyFatoorah |
| بعد اكتمال الدفع/الـfulfillment | `GET .../payment` → `isPaid: true` |
| `GET .../calendar` | مصفوفة أيام `Open` مع `slotRequirements` و`isFulfillmentReady: true` |
| `GET .../restaurants` | pool مجمّد للاشتراك |
| `GET .../days/{date}/meals/available` | وجبات الـsnapshot |
| `POST .../days/{date}/restaurant` + `POST .../days/{date}/meals` | المطعم/الوجبات تُحفظ؛ اليوم يصبح `CustomerSelected` |

حساب التجربة:

- هاتف: `+96590586243`
- كلمة السر: `TestPass123`
- `subscriptionId`: `8546f444-89f8-445b-a5f6-9076d49c9be0`
- برنامج: Lifestyle · باقة Lite (غداء + سناك) · Basic · 6 أيام

> التسجيل بالهاتف وليس بالإيميل. حقل `email` في البروفايل يبقى `null` حالياً.

---

## 1. Headers مشتركة

```http
Authorization: Bearer <customer-access-token>
Accept: application/json
Accept-Language: ar
Content-Type: application/json
```

---

## 2. متى يظهر تبويب التقويم؟

1. عميل مسجّل ومتحقق الهاتف.
2. عنوان توصيل بمنطقة (`regionId` Area من `GET /api/v1/catalog/locations?countryCode=KW`).
3. تأكيد صفحة الحساسية + التفضيلات الغذائية (يمكن اختيار NONE).
4. اشتراك فردي بعد الدفع و**اكتمال الـfulfillment**.

بوابة التقويم:

```http
GET /api/v1/customer/subscriptions/{subscriptionId}/calendar
```

| الرد | المعنى |
|------|--------|
| `200` + مصفوفة أيام | جاهز للعرض |
| `409` مع `calendar.fulfillment_not_ready` | انتظر/أعد المحاولة — لا تفتح شاشة الاختيار بعد |
| غير مدفوع / بدون اشتراك | لا تعرض تقويم اختيار؛ وجّه للاشتراك |

تحقق سريع بعد الرجوع من MyFatoorah:

```http
GET /api/v1/customer/subscriptions/{subscriptionId}/payment
```

اعرض «تم الدفع» عند `isPaid: true`، ثم ابدأ polling على `calendar` حتى `200`.

---

## 3. Endpoints التقويم على السيرفر الحي

Base:

```text
/api/v1/customer/subscriptions/{subscriptionId}
```

| الغرض | Method | المسار |
|--------|--------|--------|
| أيام التقويم (خفيف) | GET | `/calendar` |
| تفاصيل يوم واحد (كامل) | GET | `/days/{date}` |
| pool المطاعم المجمّد | GET | `/restaurants` |
| تغيير مطعم اليوم | POST | `/days/{date}/restaurant` |
| وجبات متاحة لليوم | GET | `/days/{date}/meals/available` |
| حفظ وجبات اليوم | POST | `/days/{date}/meals` |
| اختيار تلقائي | POST | `/days/{date}/auto-select` |

`{date}` = `YYYY-MM-DD`.

> `GET /calendar` يعيد ملخص `selectedMeals` (بدون ingredients/وصف/حساسية/ماكروز). للتفاصيل: `GET /days/{date}`.
> مسارات أحدث موجودة في الكود المحلي (`/days/{date}/restaurants`, `/meals/slot`, `/complete`) — اربط الموبايل بالمسارات في [`../individual-mobile-gaps/calendar/02-day-selection-api.md`](../individual-mobile-gaps/calendar/02-day-selection-api.md).

---

## 4. نماذج JSON (حي)

### 4.1 `GET .../calendar` → مصفوفة أيام

```json
[
  {
    "id": "bcb04042-0cf7-422e-8972-61cb7efd14d1",
    "serviceDate": "2026-08-31",
    "restaurantId": "6faa6a77-fe21-4ba0-9201-b348c64087ea",
    "restaurant": {
      "restaurantId": "6faa6a77-fe21-4ba0-9201-b348c64087ea",
      "nameAr": "دايت كير",
      "nameEn": "Diet Care"
    },
    "status": "Open",
    "selectionDeadlineUtc": "2026-08-28T00:00:00Z",
    "lockAtUtc": null,
    "resolvedMenuPlanVersionId": "654db5ef-f598-4b4b-a74e-ec82e300a846",
    "isAutoSelected": false,
    "hasMealSnapshot": true,
    "isFulfillmentReady": true,
    "slotRequirements": [
      { "slot": "lunch", "count": 1 },
      { "slot": "snack", "count": 1 }
    ],
    "failureReasonCode": null,
    "selectedMealIds": []
  }
]
```

حالات يوم شوهدت حياً:

- `Open` — لم تكتمل الوجبات / قابل للتعديل ضمن المهلة
- `CustomerSelected` — وجبات محفوظة

اعرض عدّاد الباقة من `slotRequirements` (مثال Lite = غداء 1 + سناك 1). الجمعة غير مشمولة لا تظهر كيوم خدمة.

### 4.2 `GET .../restaurants`

```json
{
  "restaurantPoolSnapshotId": "...",
  "eligibilityCheckedAtUtc": "...",
  "restaurantCount": 4,
  "restaurants": [
    {
      "restaurantId": "...",
      "nameAr": "...",
      "nameEn": "Diet Care",
      "tier": "Basic",
      "isInitiallySelected": true,
      "capturedAtUtc": "..."
    }
  ]
}
```

لا تخلط هذه القائمة مع مطاعم الـcheckout الحيّة. هذا الـpool مثبت وقت الاشتراك.

### 4.3 اختيار مطعم اليوم

```http
POST /days/{date}/restaurant
```

```json
{ "restaurantId": "6faa6a77-fe21-4ba0-9201-b348c64087ea" }
```

رد مختصر:

```json
{
  "id": "bcb04042-0cf7-422e-8972-61cb7efd14d1",
  "restaurantId": "6faa6a77-fe21-4ba0-9201-b348c64087ea"
}
```

تغيير المطعم يجب أن يصفّر اختيار الوجبات في الواجهة ويعيد تحميل `meals/available`.

### 4.4 وجبات متاحة

```http
GET /days/{date}/meals/available
```

عناصر مهمة:

| حقل | استخدام الموبايل |
|-----|-------------------|
| `mealId` | اختيار/حفظ |
| `slot` | `breakfast` / `lunch` / `dinner` / `snack` |
| `blockReason` | عطّل الوجبة إن وُجد |
| `presentAllergenCodes` / `mayContainAllergenCodes` | عرض/فلتر |
| `calories` / `proteinG` / `carbsG` / `fatG` | شاشة التغذية |
| `ingredients[]` | تفاصيل المكوّنات |

اجمع حسب `slot`، واسمح بعدد = `slotRequirements[].count` لكل خانة.

### 4.5 حفظ الوجبات

```http
POST /days/{date}/meals
```

```json
{
  "mealIds": [
    "2f4fb4d6-7c1b-4f4e-81cb-8d1bf16fff74",
    "5fd34858-4a97-4284-a326-e0eca35de598"
  ]
}
```

- أرسل كل `mealIds` لليوم دفعة واحدة (عدد = مجموع `slotRequirements`).
- بعدها أعد `GET .../calendar`؛ المتوقع `status: CustomerSelected` و`selectedMealIds` ممتلئة.
- لا يوجد على الحي endpoint منفصل اسمه `complete` — الحفظ عبر `POST .../meals` هو تأكيد البوكس حالياً.

### 4.6 اختيار تلقائي

```http
POST /days/{date}/auto-select
```

استخدمه للاختبار أو عند انتهاء المهلة (السيرفر قد يشغّله تشغيلياً أيضاً).

---

## 5. ترتيب شاشات Flutter المقترح (على الـAPI الحي)

```text
تبويب التقويم
  → GET /subscriptions/current   (أو خزّن subscriptionId بعد الدفع)
  → GET .../calendar
  → يوم فاضي/Open
       → GET .../restaurants          (05.11)
       → POST .../days/{date}/restaurant
       → GET .../days/{date}/meals/available   (05.12/05.13)
       → POST .../days/{date}/meals            (تم)
       → GET .../calendar                      (تحديث الحالة)
```

قواعد واجهة من عقد الشاشات:

- يوم واحد = مطعم واحد.
- لا تفعّل «تم» قبل اكتمال عدد خانات الباقة.
- قبل مهلة التعديل فقط؛ بعد القفل قراءة فقط.
- الحساسية Hard Block عبر `blockReason` / أكواد الحساسية.

---

## 6. ما قبل التقويم (تذكير سريع)

```http
POST /api/v1/auth/customers/register
POST /api/v1/auth/customers/verify-phone          # OTP ثابت على البيئة: 123456
POST /api/v1/customers/me/addresses
PUT  /api/v1/customers/me/food-preferences/allergies
PUT  /api/v1/customers/me/food-preferences/dietary
GET  /api/v1/customer/subscription-flow/...
POST /api/v1/customer/subscriptions/checkout
GET  /api/v1/customer/subscriptions/{id}/payment
GET  /api/v1/customer/subscriptions/{id}/calendar
```

ملخص ناقص البروفايل يرجع `409 subscription_flow.profile_incomplete` مع `nextAction` مثل `ALLERGIES` / `DIETARY_PREFERENCES`.

`summary` و`checkout` يتطلبان `startDate` (يوم اختيار العميل). أول صندوق = اختيار + 3 أيام؛ الرد يعيد `chosenStartDate` و`startDate` (أول توصيل). التفاصيل: [`../../mobile-customer-subscription-flow.md`](../../mobile-customer-subscription-flow.md).

---

## 7. Checklist للمبرمج

- [ ] Base URL بدون تكرار `/api/v1`
- [ ] لا تفتح التقويم قبل `isPaid` + `calendar` = 200
- [ ] استخدم فقط pool `GET .../restaurants` للاشتراك الحالي
- [ ] احفظ الوجبات بـ `mealIds` الكاملة حسب `slotRequirements`
- [ ] عطّل أي وجبة فيها `blockReason`
- [ ] لا تعتمد مسارات `/restaurants/{id}/meals` أو `/complete` قبل رفعها للسيرفر الحي
- [ ] بعد الحفظ أعد جلب التقويم لتحديث `status` و`selectedMealIds`
