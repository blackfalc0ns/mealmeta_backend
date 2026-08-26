# 7 — التقويم الخفيف + تفاصيل اليوم (موبايل)

> **الجمهور:** Flutter.  
> **الهدف:** تقليل حجم `GET /calendar` على الموبايل والسيرفر، مع تحميل تفاصيل اليوم عند الحاجة فقط.  
> **Base:** `/api/v1/customer/subscriptions/{subscriptionId}`  
> **Headers:** `Authorization: Bearer …` + `Accept-Language: ar` (أو `en`)

---

## ليه التغيير؟

رد التقويم الشهري كان بيرجع لكل يوم `selectedMeals` كاملة (مكونات، وصف، حساسية، ماكروز). ده تقيل على الشبكة والبارس.

| قبل | بعد |
|-----|-----|
| `GET /calendar` = أيام + وجبات مختارة **كاملة** | `GET /calendar` = أيام + وجبات مختارة **ملخّص** |
| — | `GET /days/{date}` = يوم **كامل** عند فتح اليوم |

اختيار المطعم/الوجبات من المينيو **ما اتغيّرش** — راجع [`02-day-selection-api.md`](./02-day-selection-api.md).

---

## Endpoints الجديدة/المعدّلة

| الغرض | Method | المسار |
|--------|--------|--------|
| شبكة الشهر (خفيف) | GET | `/calendar` |
| تفاصيل يوم واحد | GET | `/days/{date}` |

`{date}` = `YYYY-MM-DD`.

ما يتغيّرش شكل رد الـmutations (يوم كامل):

- `POST /days/{date}/restaurant`
- `POST /days/{date}/meals/slot`
- `POST /days/{date}/complete`
- `POST /days/{date}/auto-select`

---

## فلو الشاشات

```text
شاشة الشهر
  → GET .../calendar
  → ارسم الأيام من status / filledCount / requiredCount
  → selectedMeals الملخّص تكفي لعرض اسم/صورة الوجبة على اليوم

فتح يوم
  → GET .../days/{date}
  → لو `restaurantId` فاضي: المستخدم لسه مختارش مطعم → اعرض قائمة المطاعم ثم POST .../restaurant مرة واحدة
  → لو `restaurantId` موجود + `selectedMeals`:
       ❌ لا تعيد POST .../restaurant عند الرجوع من يوم تاني
       ✅ استخدم restaurantId ده مع GET .../meals
  → لو محتاج وصف / مكونات / ماكروز للمختارات: البيانات موجودة في نفس GET .../days/{date}
  → اختيار وجبات جديدة:
       GET .../days/{date}/restaurants/{restaurantId}/meals
       POST .../meals/slot  → استخدم الرد الكامل مباشرة
  → رجوع للشهر:
       حدّث اليوم محليًا من رد الـmutation
       أو أعد GET .../calendar (خفيف)
```

> بعد الـfulfillment: `restaurantId` / اسم المطعم / اللوجو تبقى `null` لحد `POST .../restaurant`. مطعم الـcheckout (`IsInitiallySelected`) للحجز فقط، مش اختيار يوم.
> تنقل بين الأيام: لو أعدت `POST .../restaurant` بمطعم مختلف، السيرفر بيمسح الوجبات المختارة — ده متوقع. لنفس المطعم الطلب بقى no-op ومش بيمسح.

---

## 1) `GET /calendar` — ملخّص

شكل الشاشة (مختصر):

```json
{
  "subscriptionId": "...",
  "programId": "...",
  "bundleId": "...",
  "requiredCount": 2,
  "slotRequirements": [
    { "slot": "lunch", "slotNameAr": "الغداء", "slotNameEn": "Lunch", "count": 1 },
    { "slot": "snack", "slotNameAr": "سناك", "slotNameEn": "Snack", "count": 1 }
  ],
  "isFulfillmentReady": true,
  "days": [ /* انظر يوم ملخّص */ ]
}
```

### يوم ملخّص (`days[]`)

نفس حقول الحالة المعتادة:

- `id`, `serviceDate`
- `restaurantId`, `restaurantNameAr` / `restaurantNameEn` (+ `restaurantName` بعد i18n)
- `restaurantLogoUrl`
- `status`, `uiStatus`, `isAutoSelected`, `canEdit`
- `selectionDeadlineUtc`, `lockAtUtc`, `lockRemainingSeconds`
- `filledCount`, `requiredCount`, `slotRequirements`
- `failureReasonCode`
- `selectedMeals[]` ← **ملخّص فقط**

### وجبة ملخّص (`selectedMeals[]`)

**موجود:**

| حقل | استخدام UI |
|-----|------------|
| `mealId` | مطابقة مع قائمة الوجبات |
| `nameAr` / `nameEn` (+ `name`) | عنوان على كارت اليوم |
| `slot`, `slotNameAr`, `slotNameEn` | خانة الإفطار/الغداء/… |
| `sortOrder` | ترتيب العرض |
| `imageUrl` | صورة مصغّرة |
| `isSelected` | عادة `true` للمختارات |

**مش موجود على `/calendar`:**

- `ingredients`
- `descriptionAr` / `descriptionEn`
- `calories`, `proteinG`, `carbsG`, `fatG`
- `presentAllergenCodes`, `mayContainAllergenCodes`, `dietaryPreferenceCodes`
- `blockReason`

```json
{
  "mealId": "...",
  "nameAr": "سلطة دجاج",
  "name": "سلطة دجاج",
  "slot": "lunch",
  "slotNameAr": "الغداء",
  "sortOrder": 1,
  "imageUrl": "https://...",
  "isSelected": true
}
```

### ممنوع على شاشة الشهر

- تفترض وجود `ingredients` أو macros جوه رد `/calendar`
- تعيد تحميل `/calendar` كامل عشان تفتح مودال تفاصيل وجبة — استخدم `/days/{date}` أو `/meals`

---

## 2) `GET /days/{date}` — يوم كامل

نفس شكل اليوم بعد `POST .../meals/slot` (كامل).

```http
GET /api/v1/customer/subscriptions/{subscriptionId}/days/2026-09-01
Accept-Language: ar
```

| رد | المعنى |
|----|--------|
| `200` + يوم | تفاصيل اليوم + `selectedMeals` كاملة |
| `404` / خطأ `calendar.day_unavailable` | التاريخ مش ضمن أيام الاشتراك |

`selectedMeals[].ingredients[]` فيها المكونات **غير الأساسية** فقط (للعرض على كارت الوجبة).

المكونات الأساسية تظهر في تفاصيل المطعم عند اختيار الوجبات:

`GET .../restaurants/{id}/meals` → `coreIngredients[]`

| حقل | معنى |
|-----|------|
| `id` | معرف المكون |
| `nameAr` / `nameEn` (+ `name`) | الاسم |
| `iconUrl` | أيقونة المكون الأساسي (`RestaurantIconUrl`) |

```json
"coreIngredients": [
  {
    "id": "...",
    "nameAr": "دجاج",
    "name": "دجاج",
    "iconUrl": "/api/v1/uploads/content?key=..."
  }
]
```

```json
"meals": [
  {
    "mealId": "...",
    "ingredients": [
      { "id": "...", "nameAr": "أرز", "isCore": false, "iconUrl": "..." }
    ]
  }
]
```

كمان اليوم فيه **مجموع ماكروز الوجبات المختارة**:

```json
"nutritionTotals": {
  "calories": 800,
  "proteinG": 60,
  "carbsG": 40,
  "fatG": 20
}
```

- مجموع من `selectedMeals[]` (أي قيمة `null` تُحسب 0).
- لو مفيش وجبات مختارة → كلها `0`.
- موجود كمان في رد `POST .../meals/slot` و`complete` و`auto-select` و`restaurant` (نفس `CustomerCalendarDayDto`).

---

## متى تستخدم إيه؟

| احتياج الموبايل | الـAPI |
|-----------------|--------|
| رسم شبكة الشهر + عدّاد الامتلاء | `GET /calendar` |
| صورة/اسم الوجبة على خلية اليوم | `GET /calendar` → `selectedMeals` الملخّص |
| مودال تفاصيل وجبة مختارة (مكونات/سعرات) | `GET /days/{date}` أو كارت من `/meals` |
| قائمة اختيار وجبات المطعم + المكونات الأساسية | `GET .../restaurants/{id}/meals` → `coreIngredients` + `meals` |
| بعد اختيار slot / complete | رد الـPOST (يوم كامل) — بلا `/calendar` إلا لتحديث الشهر |

---

## i18n سريع

مع `Accept-Language: ar` (بدون `X-Bilingual`):

- تقدر تعتمد على `name` / `restaurantName` / `slotName` الموحّدة
- وغالبًا يفضلّوا `nameAr` كمان؛ اللغة التانية تتخفي

مع الإنجليزي: نفس الفكرة مع `nameEn`.

---

## Checklist دمج Flutter

- [ ] موديل الشهر: `SelectedMealSummary` بدون ingredients
- [ ] موديل اليوم الكامل منفصل (`CustomerCalendarDay` الكامل)
- [ ] فتح اليوم → استدعاء `GET /days/{date}` فقط لو UI محتاج التفاصيل الثقيلة
- [ ] بعد `meals/slot` / `complete`: حدّث state من رد الـmutation
- [ ] ما تعتمدش على `ingredients` من `/calendar` (هتكون مش موجودة)
- [ ] اختبار اشتراك ~26 يوم: حجم رد `/calendar` أخف بوضوح من قبل

---

## مراجع

- اختيار مطعم/وجبات: [`02-day-selection-api.md`](./02-day-selection-api.md)
- قواعد UI: [`03-ui-rules-and-gaps.md`](./03-ui-rules-and-gaps.md)
- عقد حي أقدم: [`../individual-calendar/01-individual-calendar-api.md`](../individual-calendar/01-individual-calendar-api.md)
