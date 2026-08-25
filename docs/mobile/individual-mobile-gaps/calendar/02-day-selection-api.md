# 5 — اختيار اليوم (API الحي فقط)

Base:

```text
/api/v1/customer/subscriptions/{subscriptionId}
```

## Endpoints المعتمدة على السيرفر الحي

| الغرض | Method | المسار |
|--------|--------|--------|
| أيام التقويم | GET | `/calendar` |
| مطاعم الـpool المجمّد | GET | `/restaurants` |
| تعيين مطعم ليوم | POST | `/days/{date}/restaurant` |
| وجبات متاحة | GET | `/days/{date}/meals/available` |
| حفظ وجبات اليوم | POST | `/days/{date}/meals` |
| اختيار تلقائي | POST | `/days/{date}/auto-select` |

`{date}` = `YYYY-MM-DD`.

## غير متوفر على الحي (لا تربط عليه الآن)

- `GET /days/{date}/restaurants`
- `POST /days/{date}/meals/slot`
- `POST /days/{date}/complete`

الحفظ الحالي = `POST .../meals` بجسم `mealIds` كاملة.

## فلو يوم مفتوح

```text
1) من calendar اختر يوم status يسمح بالتعديل (مثل Open)
2) GET /restaurants          → قائمة المطاعم المسموحة لهذا الاشتراك
3) POST /days/{date}/restaurant { restaurantId }
4) GET /days/{date}/meals/available
5) المستخدم يختار حسب slotRequirements
6) POST /days/{date}/meals { mealIds: [...] }
7) GET /calendar             → حدّث status + selectedMealIds
```

## قواعد الحفظ

- عدد `mealIds` = مجموع `slotRequirements[].count`.
- كل وجبة لها `slot` يطابق الخانات المطلوبة.
- وجبة فيها `blockReason` → لا تُرسل.
- تغيير المطعم → امسح الاختيار المحلي وأعد الخطوات من (3).

## حالات اليوم (عرض)

| status (أمثلة) | UI |
|----------------|-----|
| `Open` | يمكن الاختيار |
| `CustomerSelected` | تم الاختيار — قابل للتعديل قبل القفل |
| `SentToRestaurant` / مقفول | قراءة فقط |
| أيام الجمعة خارج الخدمة | لا تظهر أو غير قابلة للاختيار حسب `serviceDates` |

التفاصيل الكاملة للنماذج: [`../../individual-calendar/01-individual-calendar-api.md`](../../individual-calendar/01-individual-calendar-api.md).  
سلوك الشاشات/Figma: [`../../individual-calendar/00-individual-calendar-flow.md`](../../individual-calendar/00-individual-calendar-flow.md).
