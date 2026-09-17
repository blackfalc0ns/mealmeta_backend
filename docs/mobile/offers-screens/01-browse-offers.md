# شاشة 12.01 — تصفح العروض (Offers / Browse)

**التالي:** [تفاصيل العرض (12.02)](./02-offer-details.md) · **أو الاستفادة السريعة:** [اختيار الباقة (12.04)](./04-redeem-and-select-package.md)

---

## 1. هدف الشاشة

استعراض جميع العروض الترويجية والحملات التسويقية النشطة المؤهلة للعميل الحالي، مع إمكانية التصفية حسب النوع (عروض عامة، عروض مطاعم محددة، أو اشتراكات باقات)، وعرض بانر رئيسي مميز مع عداد تنازلي نشط وشيت سفلية (Bottom Sheet) للمعاينة السريعة.

---

## 2. عناصر الشاشة في التصميم

1. **الهيدر والبانر الرئيسي (Hero Banner)**:
   - نسبة الخصم البارزة: مثل `خصم 20% على الباقات الشهرية`.
   - الوصف التسويقي: `خطط غذائية متكاملة تساعدك على تحقيق أهدافك`.
   - عداد تنازلي حي: `ينتهي خلال: 02 يوم : 14 ساعة`.
   - زر سريع: `استفد الآن` (يفتح شيت المعاينة أو ينقل لشاشة 12.04).
2. **شريط الفلاتر والتبويبات (Filter Tabs)**:
   - `الكل`: يعرض جميع العروض والحملات المتاحة.
   - `العروض`: العروض ذات الخصومات المباشرة.
   - `المطاعم`: العروض المخصصة لمطاعم محددة (`eligibleRestaurants.length > 0`).
   - `اشتراكات`: العروض المقيدة بباقات اشتراك معينة.
3. **قائمة كروت العروض (Offers Grid / List)**:
   - صورة الوجبة / البانر (`bannerImageUrl`).
   - شارة الخصم: مثل `خصم 20%`.
   - عنوان العرض بالعربي: `nameAr`.
   - الوصف المختصر: `descriptionAr`.
   - زر الإجراء: `استفد الآن`.
4. **شيت المعاينة السفلية (Quick Bottom Sheet Preview)**:
   - تنبثق عند الضغط على الكارت:
     - تفاصيل العرض: مثل `خصم 20% على جميع الباقات الشهرية`.
     - مدة العرض: العداد والوقت المتبقي.
     - كود العرض (إن وُجد) مع زر نسخ.
     - الشروط والأحكام المختصرة: `يسري العرض على باقات شهرية فقط ، لا يمكن دمجه مع عروض أخرى`.
     - زر رئيسي: `استفد الآن` ⬅️ يوجه إلى شاشة `12.04`.

---

## 3. الـ APIs المطلوبة

### استعلام العروض المتاحة والمؤهلة للعميل

```http
POST /api/v1/customer/offers/available
Authorization: Bearer {Customer_JWT_Token}
Content-Type: application/json
```

#### Request Body:
```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "cartAmount": 0,
  "durationDays": 26,
  "deliveryGovernorate": "العاصمة"
}
```

> **ملاحظة:** إذا لم يكن لدى العميل سلة حالياً، مرر `cartAmount: 0` و `durationDays: 26` لجلب جميع العروض المتاحة بدون تقييد حجم السلة.

#### Response Body (200 OK):
```json
[
  {
    "id": "7b85f264-b040-4591-a1b7-a36f90382345",
    "nameAr": "خصم 20% على الباقات الشهرية",
    "nameEn": "20% Off Monthly Packages",
    "descriptionAr": "خطط غذائية متكاملة تساعدك على تحقيق أهدافك",
    "descriptionEn": "Comprehensive nutrition plans to help you reach your goals",
    "bannerImageUrl": "https://cdn.mealmate.app/offers/monthly_20.png",
    "status": 3,
    "type": 1,
    "discountType": 1,
    "fundingMode": 3,
    "discountRate": 0.200000,
    "discountAmountKwd": null,
    "platformShareRate": 0.100000,
    "restaurantShareRate": 0.100000,
    "startsAtUtc": "2026-09-15T00:00:00Z",
    "endsAtUtc": "2026-09-30T23:59:59Z",
    "minOrderAmountKwd": 0.000,
    "minSubscriptionDays": 26,
    "maxSubscriptionDays": 30,
    "totalUsageLimit": 100,
    "currentUsageCount": 18,
    "remainingSpots": 82,
    "perCustomerLimit": 1,
    "customerSegment": 1,
    "campaignId": "9b12a345-c123-4567-8901-abcdef123456",
    "allowPromoCodeStacking": false
  }
]
```

---

## 4. نموذج البيانات المقترح في Flutter (Dart Model)

```dart
class OfferItemModel {
  final String id;
  final String nameAr;
  final String nameEn;
  final String? descriptionAr;
  final String? bannerImageUrl;
  final double? discountRate; // 0.20 = 20%
  final double? discountAmountKwd;
  final DateTime startsAtUtc;
  final DateTime endsAtUtc;
  final int? remainingSpots;
  final bool allowPromoCodeStacking;
  final String? campaignId;

  OfferItemModel({
    required this.id,
    required this.nameAr,
    required this.nameEn,
    this.descriptionAr,
    this.bannerImageUrl,
    this.discountRate,
    this.discountAmountKwd,
    required this.startsAtUtc,
    required this.endsAtUtc,
    this.remainingSpots,
    required this.allowPromoCodeStacking,
    this.campaignId,
  });

  factory OfferItemModel.fromJson(Map<String, dynamic> json) {
    return OfferItemModel(
      id: json['id'],
      nameAr: json['nameAr'] ?? '',
      nameEn: json['nameEn'] ?? '',
      descriptionAr: json['descriptionAr'],
      bannerImageUrl: json['bannerImageUrl'],
      discountRate: (json['discountRate'] as num?)?.toDouble(),
      discountAmountKwd: (json['discountAmountKwd'] as num?)?.toDouble(),
      startsAtUtc: DateTime.parse(json['startsAtUtc']),
      endsAtUtc: DateTime.parse(json['endsAtUtc']),
      remainingSpots: json['remainingSpots'],
      allowPromoCodeStacking: json['allowPromoCodeStacking'] ?? false,
      campaignId: json['campaignId'],
    );
  }

  /// حساب الوقت المتبقي بالثواني للعداد
  Duration get remainingDuration => endsAtUtc.difference(DateTime.now().toUtc());

  String get formattedDiscountText {
    if (discountRate != null) {
      return "خصم ${(discountRate! * 100).toInt()}%";
    } else if (discountAmountKwd != null) {
      return "خصم ${discountAmountKwd!.toStringAsFixed(3)} د.ك";
    }
    return "عرض خاص";
  }
}
```

---

## 5. منطق الفلاتر والتصنيف (Filtering Logic)

- **تبويب "الكل"**: لا فلتر — يعرض كامل القائمة القادمة من السيرفر.
- **تبويب "العروض"**: `offers.where((o) => o.discountRate != null || o.discountAmountKwd != null)`.
- **تبويب "المطاعم"**: العروض المرتبطة بمطاعم محددة أو حملات مشتركة (`o.campaignId != null`).
- **تبويب "اشتراكات"**: العروض التي تحدد باقات اشتراك شهرية أو أسبوعية (`o.minSubscriptionDays != null`).

---

## 6. التعامل مع الأخطاء والحالات الخاصة (Edge Cases)

| الحالة | المعالجة في التطبيق |
| :--- | :--- |
| **القائمة فارغة (لا عروض نشطة)** | إظهار حالة Empty State لطيفة مع رسالة: "لا توجد عروض نشطة حالياً، ترقبوا عروضنا القادمة قريباً". |
| **انتهاء وقت العرض أثناء التصفح** | التحقق من `endsAtUtc`؛ إذا انتهى الوقت يتم إزالة العرض أو كتابة شارة "انتهى العرض" وتعطيل زر "استفد الآن". |
| **نفاد المقاعد (`remainingSpots == 0`)** | إظهار شارة "اكتمل العدد" وتعطيل الانتقال للشراء. |
