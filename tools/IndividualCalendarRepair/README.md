# IndividualCalendarRepair

تدقيق الأيام القديمة لاشتراكات الأفراد التي حملت ختم `ConfirmedAtUtc` من مسار تجهيز الدفع القديم
(قبل إصلاح 2026-10-08 الذي يترك الأيام الجديدة بلا ختم). الأداة **report-only افتراضيًا**؛ لا تكتب
أي شيء في قاعدة البيانات دون قائمة مرشحين راجعها المشغل صراحةً.

## التصنيفات

| التصنيف | المعنى | الإجراء |
|---|---|---|
| `RepairablePaymentStamp` | فردية + اشتراك نشط + دفع موثق + يوم mutable + لا إرسال/تأكيد/تجهيز فعلي في سجل الطلبات + سجل تدقيق Fulfillment يثبت أصل الختم | مرشح للإصلاح بعد مراجعة المشغل |
| `ProtectedOperationalLock` | اليوم دخل التشغيل (إرسال/تأكيد مطعم/تجهيز/توصيل) أو وُجد طلب مطعم | محمي — لا يُمس أبدًا |
| `NoPaymentEvidence` | ختم على يوم mutable بلا دليل دفع مخزن | report فقط |
| `AmbiguousEvidence` | كل الشروط موجودة لكن الدليل توقيتي فقط وسجل تدقيق Fulfillment مفقود | report فقط |
| `PastDeadlineOpen` / `OutOfScope` | يوم سليم أو عائلي/ملغى/مجمّد | لا يدخل التقرير |

مساواة التوقيت وحدها لا تكفي أبدًا لإثبات أصل الختم؛ الشرط الفاصل هو وجود سجل تدقيق
Fulfillment للاشتراك (`AuditLogs.Action` يحوي "Fulfill").

## التشغيل

```bash
cd backend/tools/IndividualCalendarRepair

# تقرير فقط (لا كتابة):
MEALMATE_DB_CONNECTION="..." dotnet run > repair-report.tsv

# تطبيق على مرشحين راجعهم المشغل فقط:
# candidates.json = [{"subscriptionId":"...","dayId":"..."}, ...]
MEALMATE_DB_CONNECTION="..." dotnet run -- --apply candidates.json --operator <adminUserId>
```

## ضمانات وضع apply

- لكل يوم معاملة مستقلة، مع **إعادة تصنيف من الحالة الحالية** داخل المعاملة (`LegacyDayStampClassifier.CanApply`):
  تغيّرت الحالة بين التقرير والتطبيق (تأكيد مطعم أحدث، إرسال، قفل) → يُتخطى اليوم ولا يُكتب فوقه.
- الختم الممسوح مسبقًا (`ConfirmedAtUtc` فارغ) → تخطٍّ `stamp_already_clear` (تشغيل متكرر آمن).
- كل تعديل يكتب سجل تدقيق `IndividualCalendarRepair.StampCleared` مع قبل/بعد وسبب ومعرّف المشغل.
- لا تُمنح مهلة جديدة ولا تُعدّل مواعيد التوصيل؛ الإصلاح محصور في إزالة ختم مؤكد أنه من مسار الدفع القديم.

## الاختبارات

`tests/MealMate.UnitTests/CustomerCheckout/LegacyIndividualCalendarRepairTests.cs` يغطي تصنيف
حالات الخطة (يوم مختوم مثبت، تأكيد مطعم حقيقي، تجهيز جارٍ، اشتراك ملغى/مجمد، يوم تجاوز المهلة،
بلا دليل دفع، توقيت بلا سجل) وضمانات إعادة الفحص والتكرار في apply.
