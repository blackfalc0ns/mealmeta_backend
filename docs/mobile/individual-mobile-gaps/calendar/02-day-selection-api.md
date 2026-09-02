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
| مطاعم اليوم + الحد | GET | `/days/{date}/restaurants?query=...` | المسار المفضّل — يدعم البحث `query` بالاسم، ويشمل `rating`, `ratingCount`, `deliveryTimeText`, `tier` |
| مطاعم الـpool المجمّد | GET | `/restaurants?query=...` | توافق قديم — يدعم البحث `query` بالاسم، ويشمل `rating`, `ratingCount`, `logoUrl`, `tier` |
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
3) لو restaurantId == null:
     GET .../days/{date}/restaurants ثم POST .../restaurant مرة واحدة فقط
   لو restaurantId موجود (رجوع ليوم متختار):
     ❌ لا تعيد POST .../restaurant
     ✅ استخدم restaurantId من GET .../days/{date}
4) GET .../days/{date}/restaurants/{restaurantId}/meals
   (اختياري ?slot=lunch)
5) POST .../days/{date}/meals/slot  لكل خانة مطلوبة
6) POST .../days/{date}/complete
7) حدّث المحلي من رد الـmutation أو GET .../days/{date}؛ للشهور أعد GET .../calendar
```

> مهم: إعادة `POST .../restaurant` بمطعم مختلف عن المختار تمسح الوجبات. عند التنقل بين الأيام اعتمد على `GET .../days/{date}` فقط لو اليوم متختار.

## قواعد الاختيار

- عدد الوجبات = مجموع `slotRequirements[].count` (مثال Lite: lunch 1 + snack 1).
- `GET .../restaurants/{id}/meals` يرجع **فقط** وجبات خانات الباقة في الاشتراك (من `slotRequirements`) — مش إفطار/عشاء لو الباقة غداء+سناك.
- وجبة فيها `blockReason` → لا تُرسل.
- تغيير المطعم → امسح الاختيار المحلي وأعد تحميل الوجبات.
- بعد الاختيار: `GET .../meals` يرجع `isSelected: true` على الوجبات المختارة + `selectedMealIds[]` + `filledCount`.
- كمان `GET .../calendar` → `days[].selectedMeals` لنفس اليوم.
- `slotRequirements[]` و`meals[]` فيها `slotNameAr` / `slotNameEn` (الإفطار/الغداء/العشاء/سناك).
- لوجو المطعم: `restaurantLogoUrl` في يوم التقويم، و`logoUrl` في قائمة المطاعم وقائمة الوجبات.
- اسم المطعم في وجبات اليوم / قائمة المطاعم مع `Accept-Language: ar`: `nameAr` و`name` (نفس القيمة). مع الإنجليزي: `nameEn` و`name`.
- مكونات الوجبة: `meals[].ingredients[]` للمكونات غير الأساسية فقط.
- المكونات الأساسية: `coreIngredients[]` على مستوى المطعم في `GET .../restaurants/{id}/meals` (مش متكررة في كل وجبة) — وتشمل بلد المنشأ (`originCountryCode` / الاسم / `flagEmoji`) لو ظاهر للعميل.

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
