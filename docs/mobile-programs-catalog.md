# ربط كتالوج برامج الاشتراك للموبايل

## Endpoint

```http
GET /api/v1/customer/subscription-flow/programs
Authorization: Bearer <customer-access-token>
Accept-Language: ar
```

الـendpoint يعيد البرامج `Active` فقط التي تحتوي على باقة فعالة واحدة على الأقل.

## Response الجديد

```json
[
  {
    "programId": "b59a2251-ba7f-4a78-a6bb-d43defa2e4d2",
    "code": "PROG_WEIGHT_LOSS",
    "shortCode": "WL",
    "nameAr": "خسارة الوزن",
    "nameEn": "Weight Loss",
    "descriptionAr": "وجبات محسوبة السعرات تساعدك على خسارة الوزن تدريجيًا مع الحفاظ على تغذية متوازنة.",
    "descriptionEn": "Calorie-controlled meals that support gradual weight loss while maintaining balanced nutrition.",
    "keywordsAr": [
      "سعرات محسوبة",
      "نتائج مستدامة"
    ],
    "keywordsEn": [
      "Calorie controlled",
      "Sustainable results"
    ],
    "imageUrl": null,
    "bundleCount": 3,
    "supportedSlots": [
      "breakfast",
      "lunch",
      "snack"
    ]
  }
]
```

## ما تم حذفه

تم حذف `nutritionStandards` بالكامل من Response العميل، وبالتالي لن تعود الحقول التالية:

```text
kcalMin
kcalMax
proteinGMin
proteinGMax
```

المعايير الغذائية ما زالت موجودة داخليًا في الباك للتحقق من المنيو والوجبات، لكنها ليست بيانات عرض للمستخدم.

## ربط الكارت

| عنصر التصميم | الحقل |
|---|---|
| اسم البرنامج | `nameAr` أو `nameEn` |
| النص أسفل الاسم | `descriptionAr` أو `descriptionEn` |
| الكلمات القصيرة | `keywordsAr` أو `keywordsEn` |
| الصورة | `imageUrl`، مع Placeholder عندما تكون `null` |
| فتح الخطوة التالية | مرر `programId` إلى `/programs/{programId}/bundles` |

في العربية:

```dart
final name = program.nameAr;
final description = program.descriptionAr;
final keywords = program.keywordsAr;
```

في الإنجليزية:

```dart
final name = program.nameEn;
final description = program.descriptionEn;
final keywords = program.keywordsEn;
```

لا تحاول تكوين الكلمات من `description`، ولا تثبت كلمات كل برنامج داخل الموبايل؛ الأدمن يستطيع تعديل الوصف والكلمات من إدارة البرامج.

## البرامج الحالية وبيانات العرض

| البرنامج | الوصف المختصر | الكلمات |
|---|---|---|
| خسارة الوزن | وجبات محسوبة السعرات لخسارة تدريجية ومتوازنة | سعرات محسوبة، نتائج مستدامة |
| بناء العضلات | وجبات غنية بالبروتين لدعم النمو والتعافي | بروتين أعلى، بناء العضلات |
| نمط حياة متوازن | وجبات بنسب غذائية مناسبة لنمط صحي | تغذية متوازنة، طاقة يومية |
| الأداء الرياضي | وجبات لدعم الطاقة والأداء والتعافي | أداء رياضي، دعم التعافي |
| كيتو | برنامج منخفض الكربوهيدرات يعتمد على الدهون الصحية | كربوهيدرات منخفضة، دهون صحية |
| قليل الكربوهيدرات | خطة مرنة بوجبات متنوعة سهلة الالتزام | كربوهيدرات أقل، خيارات مرنة |

## قواعد الـParser

- `descriptionAr` و`descriptionEn` يظلان Nullable لدعم أي برنامج جديد لم يكتمل محتواه بعد.
- `keywordsAr` و`keywordsEn` مصفوفتان، وقد تكونان فارغتين.
- الحد الإداري الأقصى 4 كلمات لكل لغة.
- طول كل كلمة/عبارة عرض لا يتجاوز 60 حرفًا.
- لا تعتمد على ترتيب `code`؛ القائمة مرتبة من الباك باستخدام `sortOrder` الداخلي.
- احتفظ بـParser يقبل Enum النصي والرقمي في بقية فلو الاشتراك أثناء فترة الانتقال.

## Model مقترح

```dart
class CustomerProgramModel {
  const CustomerProgramModel({
    required this.programId,
    required this.code,
    required this.shortCode,
    required this.nameAr,
    required this.nameEn,
    required this.descriptionAr,
    required this.descriptionEn,
    required this.keywordsAr,
    required this.keywordsEn,
    required this.imageUrl,
    required this.bundleCount,
    required this.supportedSlots,
  });

  final String programId;
  final String code;
  final String? shortCode;
  final String nameAr;
  final String nameEn;
  final String? descriptionAr;
  final String? descriptionEn;
  final List<String> keywordsAr;
  final List<String> keywordsEn;
  final String? imageUrl;
  final int bundleCount;
  final List<String> supportedSlots;

  factory CustomerProgramModel.fromJson(Map<String, dynamic> json) {
    return CustomerProgramModel(
      programId: json['programId'].toString(),
      code: json['code'].toString(),
      shortCode: json['shortCode']?.toString(),
      nameAr: json['nameAr']?.toString() ?? '',
      nameEn: json['nameEn']?.toString() ?? '',
      descriptionAr: json['descriptionAr']?.toString(),
      descriptionEn: json['descriptionEn']?.toString(),
      keywordsAr: (json['keywordsAr'] as List? ?? const [])
          .map((value) => value.toString())
          .toList(growable: false),
      keywordsEn: (json['keywordsEn'] as List? ?? const [])
          .map((value) => value.toString())
          .toList(growable: false),
      imageUrl: json['imageUrl']?.toString(),
      bundleCount: (json['bundleCount'] as num?)?.toInt() ?? 0,
      supportedSlots: (json['supportedSlots'] as List? ?? const [])
          .map((value) => value.toString())
          .toList(growable: false),
    );
  }
}
```

