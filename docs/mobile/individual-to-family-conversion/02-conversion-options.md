# 02 — شاشة الاختيار (Chooser) — `conversion-options`

## ما الذي يتغير في الموبايل؟

- شاشة **جديدة** (أو bottom sheet) تظهر عندما `FAMILY.requiresConversion == true`.
- خياران فقط في Phase 1؛ الثاني معطل.

## API

```http
GET /api/v1/customer/subscriptions/current/conversion-options
Authorization: Bearer <token>
```

## شكل الرد

```json
{
  "subscriptionId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "status": "Active",
  "options": [
    {
      "code": "CONVERT_TO_FAMILY_MANAGER",
      "available": true,
      "titleAr": "أعمل عائلة وأبقى مدير",
      "titleEn": "Create a family and become manager",
      "action": "POST /api/v1/customer/family-subscription-flow/convert-from-individual",
      "disabledReason": null
    },
    {
      "code": "CHANGE_INDIVIDUAL_PLAN",
      "available": false,
      "titleAr": "أغيّر خطتي الفردية",
      "titleEn": "Change my individual plan",
      "action": null,
      "disabledReason": "change_individual_plan.not_available"
    }
  ]
}
```

## موديل مقترح

```dart
class ConversionOptionsScreen {
  final String subscriptionId;
  final String status; // Active, Paid, Scheduled, ...
  final List<ConversionOption> options;
}

class ConversionOption {
  final String code;
  final bool available;
  final String titleAr;
  final String titleEn;
  final String? action; // مسار نصي من الباك
  final String? disabledReason;
}
```

## سلوك UI

| `code` | `available` | ماذا تفعل؟ |
|--------|-------------|------------|
| `CONVERT_TO_FAMILY_MANAGER` | `true` | نفّذ `POST convert-from-individual` (انظر ملف 03) |
| `CONVERT_TO_FAMILY_MANAGER` | `false` | عطّل الزر؛ اعرض `disabledReason` (غالبًا `conversion.individual_not_convertible` عند PendingPayment) |
| `CHANGE_INDIVIDUAL_PLAN` | دائمًا `false` في Phase 1 | اعرضه معطلًا بنص «قريبًا» / رسالة من `disabledReason` — **لا تبنِ الفلو** |

## أخطاء شائعة من الـendpoint

| كود | معنى |
|-----|------|
| `conversion.no_individual_subscription` | مفيش اشتراك فردي حالي للتحويل |

## ملفات Flutter المتوقعة

- `conversion_options_page.dart`
- `conversion_options_model.dart`
- navigation من شاشة الأنواع → هنا
