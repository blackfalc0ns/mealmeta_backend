# 01 — شاشة نوع الاشتراك (`subscription-flow/types`)

## ما الذي يتغير في الموبايل؟

- حدّث موديل `SubscriptionTypeOption` ليقبل حقول جديدة.
- لا تفتح فلو الاشتراك الفردي لو `isAvailable == false`.
- لو اختار العائلة و`requiresConversion == true` → روح لشاشة الـChooser (`conversion-options`) **قبل** أي `PUT group`.

## API

```http
GET /api/v1/customer/subscription-flow/types
Authorization: Bearer <token>
```

## شكل الرد (مقتطف)

```json
[
  {
    "code": "INDIVIDUAL",
    "nameAr": "اشتراك فردي",
    "nameEn": "Individual subscription",
    "descriptionAr": "خطة غذائية لشخص واحد",
    "descriptionEn": "A meal plan for one person",
    "maxMembers": 1,
    "isAvailable": false,
    "membersConfiguredAfterPayment": false,
    "disabledReason": "checkout.existing_subscription",
    "requiresConversion": false,
    "conversionEndpoint": null
  },
  {
    "code": "FAMILY",
    "nameAr": "اشتراك عائلي",
    "nameEn": "Family subscription",
    "descriptionAr": "حوّل اشتراكك الفردي لعائلة وابقَ مديرًا",
    "descriptionEn": "Convert your individual plan into a family as manager",
    "maxMembers": 4,
    "isAvailable": true,
    "membersConfiguredAfterPayment": false,
    "disabledReason": null,
    "requiresConversion": true,
    "conversionEndpoint": "/api/v1/customer/subscriptions/current/conversion-options"
  }
]
```

## حقول جديدة

| حقل | نوع | معنى |
|-----|-----|------|
| `disabledReason` | `string?` | سبب تعطيل الخيار (مثل `checkout.existing_subscription`) |
| `requiresConversion` | `bool` | على `FAMILY`: لازم تحويل من فردي قبل إنشاء مجموعة جديدة |
| `conversionEndpoint` | `string?` | مسار الـChooser؛ استدعِه كما هو (نسبي تحت نفس الـhost) |

## سلوك UI مطلوب

| حالة | UI |
|------|-----|
| `INDIVIDUAL.isAvailable == false` | عطّل الكرت / أظهر رسالة من `disabledReason`؛ لا تبدأ checkout فردي |
| `FAMILY.requiresConversion == true` | عند الضغط على عائلة → `GET conversionEndpoint` (شاشة Chooser) |
| `FAMILY.requiresConversion == false` ووصف «أكمل مسودة» | افتح `GET .../family-subscription-flow/current` وأكمل المسودة |
| لا حظر | فلو قديم كما هو |

## ملفات Flutter المتوقعة (أمثلة أسماء)

- `subscription_type_option.dart` / mapper JSON
- شاشة `subscription_type_page.dart` / navigation
- repository `subscription_flow_repository.dart`

## لا تفعل

- لا تتجاهل `requiresConversion` وتعمل `PUT /family-subscription-flow/group` مباشرة عند وجود فردي → الباك يرجع `409 conversion.convert_required`.
