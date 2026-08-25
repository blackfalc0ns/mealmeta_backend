# 5 — اختيار اليوم (API الحي)

Base:

```text
/api/v1/customer/subscriptions/{subscriptionId}
```

## Endpoints المعتمدة

| الغرض | Method | المسار | ملاحظة |
|--------|--------|--------|--------|
| أيام التقويم | GET | `/calendar` | |
| مطاعم اليوم + الحد | GET | `/days/{date}/restaurants` | المسار المفضّل |
| مطاعم الـpool المجمّد | GET | `/restaurants` | توافق قديم |
| تعيين مطعم ليوم | POST | `/days/{date}/restaurant` | `{ restaurantId }` |
| وجبات مطعم اليوم | GET | `/days/{date}/restaurants/{restaurantId}/meals` | **المسار المفضّل** |
| وجبات متاحة (قديم) | GET | `/days/{date}/meals/available` | توافق Flutter — يستخدم مطعم اليوم الحالي |
| اختيار وجبة لخانة | POST | `/days/{date}/meals/slot` | `{ mealId, slot }` |
| إكمال البوكس | POST | `/days/{date}/complete` | |
| اختيار تلقائي | POST | `/days/{date}/auto-select` | |

`{date}` = `YYYY-MM-DD`.

## مهم للموبايل

```text
❌ GET .../days/{date}/meals/available     ← كان 404 قبل إضافة التوافق؛ لا تعتمد عليه كمسار أساسي
✅ GET .../days/{date}/restaurants/{restaurantId}/meals
```

`restaurantId` موجود في رد `/calendar` لكل يوم (`restaurantId`).

## فلو يوم مفتوح

```text
1) GET .../calendar
2) اختر يوم (status/canEdit يسمح)
3) restaurantId من اليوم — أو GET .../days/{date}/restaurants ثم POST .../restaurant
4) GET .../days/{date}/restaurants/{restaurantId}/meals
   (اختياري ?slot=lunch)
5) POST .../days/{date}/meals/slot  لكل خانة مطلوبة
6) POST .../days/{date}/complete
7) GET .../calendar للتحديث
```

## قواعد الاختيار

- عدد الوجبات = مجموع `slotRequirements[].count` (مثال Lite: lunch 1 + snack 1).
- وجبة فيها `blockReason` → لا تُرسل.
- تغيير المطعم → امسح الاختيار المحلي وأعد تحميل الوجبات.
- بعد الاختيار: `GET .../meals` يرجع `isSelected: true` على الوجبات المختارة + `selectedMealIds[]` + `filledCount`.
- كمان `GET .../calendar` → `days[].selectedMeals` لنفس اليوم.
- `slotRequirements[]` و`meals[]` فيها `slotNameAr` / `slotNameEn` (الإفطار/الغداء/العشاء/سناك).
- لوجو المطعم: `restaurantLogoUrl` في يوم التقويم، و`logoUrl` في قائمة المطاعم وقائمة الوجبات.

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
