# 5 — اختيار اليوم (API الحي)

Base:

```text
/api/v1/customer/subscriptions/{subscriptionId}
```

## Endpoints المعتمدة

| الغرض | Method | المسار | ملاحظة |
|--------|--------|--------|--------|
| أيام التقويم (خفيف) | GET | `/calendar` | `selectedMeals` ملخّص فقط (بدون ingredients/وصف/حساسية/ماكروز) |
| تفاصيل يوم واحد | GET | `/days/{date}` | يوم كامل بنفس شكل رد الـmutations |
| مطاعم اليوم + الحد | GET | `/days/{date}/restaurants` | المسار المفضّل |
| مطاعم الـpool المجمّد | GET | `/restaurants` | توافق قديم |
| تعيين مطعم ليوم | POST | `/days/{date}/restaurant` | `{ restaurantId }` → يوم كامل |
| وجبات مطعم اليوم | GET | `/days/{date}/restaurants/{restaurantId}/meals` | **المسار المفضّل** |
| وجبات متاحة (قديم) | GET | `/days/{date}/meals/available` | توافق Flutter — يستخدم مطعم اليوم الحالي |
| اختيار وجبة لخانة | POST | `/days/{date}/meals/slot` | `{ mealId, slot }` → يوم كامل |
| إكمال البوكس | POST | `/days/{date}/complete` | يوم كامل |
| اختيار تلقائي | POST | `/days/{date}/auto-select` | يوم كامل |

`{date}` = `YYYY-MM-DD`.

## مهم للموبايل

```text
❌ GET .../days/{date}/meals/available     ← كان 404 قبل إضافة التوافق؛ لا تعتمد عليه كمسار أساسي
✅ GET .../days/{date}/restaurants/{restaurantId}/meals
✅ GET .../days/{date}                     ← تفاصيل الوجبات المختارة ليوم واحد
```

`restaurantId` موجود في رد `/calendar` لكل يوم بعد اختيار المطعم (`POST .../restaurant`). قبل الاختيار يكون `null`.

`GET /calendar` → `selectedMeals[]` فيها فقط: `mealId`, الاسم, `slot`/`slotName*`, `sortOrder`, `imageUrl`, `isSelected`.  
للتفاصيل الكاملة (مكونات/وصف/ماكروز): راجع [`04-slim-calendar-and-day-detail.md`](./04-slim-calendar-and-day-detail.md) و`GET /days/{date}`.

## فلو يوم مفتوح

```text
1) GET .../calendar
2) اختر يوم (status/canEdit يسمح) — اختياري: GET .../days/{date} لتفاصيل المختارات
3) restaurantId من اليوم — أو GET .../days/{date}/restaurants ثم POST .../restaurant
4) GET .../days/{date}/restaurants/{restaurantId}/meals
   (اختياري ?slot=lunch)
5) POST .../days/{date}/meals/slot  لكل خانة مطلوبة
6) POST .../days/{date}/complete
7) حدّث المحلي من رد الـmutation أو GET .../days/{date}؛ للشهور أعد GET .../calendar
```

## قواعد الاختيار

- عدد الوجبات = مجموع `slotRequirements[].count` (مثال Lite: lunch 1 + snack 1).
- وجبة فيها `blockReason` → لا تُرسل.
- تغيير المطعم → امسح الاختيار المحلي وأعد تحميل الوجبات.
- بعد الاختيار: `GET .../meals` يرجع `isSelected: true` على الوجبات المختارة + `selectedMealIds[]` + `filledCount`.
- كمان `GET .../calendar` → `days[].selectedMeals` لنفس اليوم.
- `slotRequirements[]` و`meals[]` فيها `slotNameAr` / `slotNameEn` (الإفطار/الغداء/العشاء/سناك).
- لوجو المطعم: `restaurantLogoUrl` في يوم التقويم، و`logoUrl` في قائمة المطاعم وقائمة الوجبات.
- اسم المطعم في وجبات اليوم / قائمة المطاعم مع `Accept-Language: ar`: `nameAr` و`name` (نفس القيمة). مع الإنجليزي: `nameEn` و`name`.

## ظهور «مختارة» في الواجهة

```text
POST .../meals/slot
  → احفظ محليًا أو
  → أعد GET .../days/{date}/restaurants/{restaurantId}/meals
       meals[].isSelected == true  → علّم الكارت كمختار
       selectedMealIds             → نفس القائمة مختصرة
```

حتى يتم رفع الـAPI: اعتمدوا على `GET .../calendar` → `selectedMeals[].mealId` وطابقوها مع قائمة الوجبات.

## حالات اليوم (عرض)

| status / uiStatus | UI |
|-------------------|-----|
| `Open` / `drafting` | يمكن الاختيار |
| `CustomerSelected` | تم — قابل للتعديل قبل القفل |
| مقفول / `SentToRestaurant` | قراءة فقط |

التفاصيل: [`../../individual-calendar/01-individual-calendar-api.md`](../../individual-calendar/01-individual-calendar-api.md).
