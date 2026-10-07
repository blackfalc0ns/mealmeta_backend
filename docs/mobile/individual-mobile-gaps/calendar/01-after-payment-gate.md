> Payment integration updated 2026-10-06: use the [Payzah contract](<F:/MealMate/backend/docs/mobile/payzah-payments.md>). Any old-provider payment endpoints, method IDs, credential/webhook fields or refund instructions below are superseded; they are retained only as historical context for this mixed guide.

# 4 — بعد الدفع: بوابة فتح التقويم

> لا تفتح شاشات اختيار المطعم/الوجبة قبل اجتياز هذه البوابة.

## التسلسل الصحيح

```text
رجوع من MyFatoorah
  → GET .../subscriptions/{id}/payment
      isPaid=false  → انتظر (backoff 2s → 5s → 10s، حد أقصى ~2 دقيقة)
      isFailed=true → فشل دفع
      isPaid=true   → انتقل لانتظار التفعيل
  → GET .../subscriptions/{id}/calendar
      409 calendar.fulfillment_not_ready → انتظر وأعد المحاولة
      200 + مصفوفة أيام → افتح تبويب التقويم
```

## معاني 409

```json
{
  "error": "calendar.fulfillment_not_ready",
  "isFulfillmentReady": false,
  "paymentStatus": "Paid",
  "failureReasonCode": "fulfillment.day_snapshot_missing"
}
```

| حالة | تصرف الموبايل |
|------|----------------|
| `paymentStatus=Paid` و`isFulfillmentReady=false` | «جاري تجهيز أيام اشتراكك» + retry |
| `failureReasonCode` يظهر بعد محاولات طويلة | اعرض دعم/أعد لاحقًا — لا تعمل checkout جديد فورًا |
| `isPaid=false` وما زال Pending | ابقَ على شاشة انتظار الدفع |

## ممنوع

- فتح التقويم بمجرد استلام deep link النجاح.
- استدعاء confirm-payment من التطبيق.
- دمج حالة «مدفوع» مع «جاهز للاختيار» في شاشة واحدة بدون polling منفصل.

## بعد 200

- خزّن `subscriptionId`.
- اعرض أيام `serviceDate` من المصفوفة.
- التفاصيل: [`02-day-selection-api.md`](./02-day-selection-api.md).

مرجع API أوسع: [`../../individual-calendar/01-individual-calendar-api.md`](../../individual-calendar/01-individual-calendar-api.md).
