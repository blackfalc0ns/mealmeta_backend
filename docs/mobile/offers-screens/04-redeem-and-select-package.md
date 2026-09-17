# شاشة 12.04 — استرداد العرض واختيار الباقة (Offers / Redeem Offer & Select Package)

**السابق:** [تفاصيل العرض (12.02)](./02-offer-details.md) · **التالي:** [ملخص الدفع وتأكيد الكود (12.05)](./05-promo-applied-checkout.md)

---

## 1. هدف الشاشة

تمكين العميل من تطبيق العرض المختار على مدة الاشتراك المناسبة له (باقة شهرية 26 يوماً، نصف شهرية 13 يوماً، أو أسبوعية 6 أيام)، مع إظهار الأسعار الحقيقية قبل وبعد الخصم مباشرة من السيرفر لكل خيار، مع التحقق من أهلية كل باقة لشروط العرض (مثل الحد الأدنى للأيام `minSubscriptionDays`).

---

## 2. عناصر الشاشة في التصميم

1. **الهيدر العلوي**:
   - زر إغلاق `(X)` وزر رجوع.
   - عنوان الصفحة: `استرداد العرض`.
2. **بانر العرض المصغر (Offer Hero Card)**:
   - صورة الوجبة / البانر.
   - شارة الخصم البارزة: `خصم 20% على الباقات الشهرية`.
   - الوصف المختصر: `خطط غذائية متكاملة تساعدك على تحقيق أهدافك`.
   - عداد تنازلي نشط: `ينتهي خلال : 02 يوم : 14 ساعة`.
3. **صندوق تفاصيل العروض (Offer Specs Summary)**:
   - نسبة أو قيمة الخصم: `خصم 20% على جميع الباقات الشهرية`.
   - مدة وسريان العرض مع العداد.
   - كود الخصم (إن وجد) مع زر نسخ سريع: `SAVE20`.
   - الشروط والأحكام: `يسري العرض على باقات شهرية فقط ، لا يمكن دمجه مع عروض أخرى`.
4. **قائمة اختيار الباقة المناسبة (Package Selection Radio Cards)**:
   - **باقة شهر (26 يوم - 4 وجبات يومياً)**:
     - السعر الأساسي مشطوب: ~~52.000 KWD~~
     - السعر بعد الخصم: **41.600 KWD**
     - شارة توفير: `خصم 20%`
     - زر اختيار دائري (Radio Button - محدد افتراضياً).
   - **باقة نصف شهر (13 يوم - 4 وجبات يومياً)**:
     - السعر الأساسي مشطوب: ~~26.000 KWD~~
     - السعر بعد الخصم أو السعر الأصلي إذا كان غير مؤهل: **20.800 KWD**
     - زر اختيار دائري غير محدد.
   - **باقة أسبوع (6 أيام - 4 وجبات يومياً)**:
     - السعر الأساسي مشطوب: ~~14.500 KWD~~
     - السعر بعد الخصم: **11.600 KWD**
     - زر اختيار دائري غير محدد.
5. **شريط الإجمالي السفلي الثابت (Sticky Bottom Bar)**:
   - تسمية الإجمالي: `الإجمالي بعد الخصم` وبجانبها القيمة: `41.600 KWD`.
   - زر المتابعة: `استكمال الشراء` (مع أيقونة القفل: `🔒 عملية دفع آمنة`).

---

## 3. الـ APIs المطلوبة

### 1) جلب تسعير الباقة الدقيق مع العرض (Pricing Preview)

يتم استدعاء هذا الـ Endpoint للحصول على التسعير الرسمي الموثوق من السيرفر لكل باقة يختارها المستخدم:

```http
GET /api/v1/customer/subscription-pricing/preview?programId={programId}&bundleId={bundleId}&tier={tier}&durationDays={durationDays}&offerId={offerId}
Authorization: Bearer {Customer_JWT_Token}
```

#### Query Parameters:
- `programId` (UUID, اختياري): معرف البرنامج الغذائي المحدد.
- `bundleId` (UUID, اختياري): معرف حزمة الوجبات.
- `tier` (Integer, 0 = Standard, 1 = Premium, ...): فئة الاشتراك.
- `durationDays` (Integer, مطلوب): عدد الأيام (26 للشهر، 13 لنصف الشهر، 6 للأسبوع).
- `offerId` (UUID, مطلوب): معرف العرض المراد تطبيقه.

#### Response Body (200 OK):
```json
{
  "programId": "c4d15678-e123-4567-8901-123456789abc",
  "bundleId": "a1b2c3d4-e5f6-7890-1234-567890abcdef",
  "tier": 0,
  "durationDays": 26,
  "originalProgramBasePrice": 52.000,
  "subtotalBeforeDiscount": 52.000,
  "campaignDiscountApplied": 10.400,
  "promoCodeDiscountApplied": 0.000,
  "customMealSelectionsExtraCharge": 0.000,
  "deliveryFee": 0.000,
  "taxAmount": 0.000,
  "finalCustomerPrice": 41.600,
  "savingsPercentage": 20.0,
  "appliedCampaignTitle": "خصم 20% على الباقات الشهرية",
  "pricingBreakdownNotes": [
    "تم تطبيق خصم العرض الترويجي: 20%",
    "تم توفير 10.400 KWD بنجاح"
  ]
}
```

---

### 2) التحقق السريع من أهلية العرض (Evaluate Offer Discount)

في حال رغبة التطبيق في معرفة هل باقة معينة مقبولة ضمن شروط العرض (مثلاً عند التبديل بين 6 أيام و 26 يوماً):

```http
POST /api/v1/customer/offers/evaluate
Authorization: Bearer {Customer_JWT_Token}
Content-Type: application/json
```

#### Request Body:
```json
{
  "offerId": "7b85f264-b040-4591-a1b7-a36f90382345",
  "promoCode": "SAVE20",
  "cartAmount": 14.500,
  "durationDays": 6,
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

#### Response Body إذا كانت الباقة غير مستوفية للحد الأدنى (200 OK):
```json
{
  "isApplicable": false,
  "offerId": "7b85f264-b040-4591-a1b7-a36f90382345",
  "offerName": "خصم 20% على الباقات الشهرية",
  "calculatedDiscountKwd": 0.000,
  "finalAmountKwd": 14.500,
  "reasonIfFailed": "مدة الاشتراك (6 يوم) أقل من الحد الأدنى للعرض (26 يوم)."
}
```

---

## 4. نموذج البيانات في Flutter (Dart Models)

```dart
class PackageTierOption {
  final String id;
  final String titleAr;
  final int durationDays;
  final int mealsPerDay;
  final double originalPriceKwd;
  final double discountedPriceKwd;
  final double discountRate;
  final bool isEligibleForOffer;
  final String? ineligibilityReason;

  const PackageTierOption({
    required this.id,
    required this.titleAr,
    required this.durationDays,
    required this.mealsPerDay,
    required this.originalPriceKwd,
    required this.discountedPriceKwd,
    required this.discountRate,
    required this.isEligibleForOffer,
    this.ineligibilityReason,
  });
}

class PricingPreviewResult {
  final int durationDays;
  final double subtotalBeforeDiscount;
  final double campaignDiscountApplied;
  final double finalCustomerPrice;
  final double savingsPercentage;
  final String? appliedCampaignTitle;
  final List<String> pricingBreakdownNotes;

  const PricingPreviewResult({
    required this.durationDays,
    required this.subtotalBeforeDiscount,
    required this.campaignDiscountApplied,
    required this.finalCustomerPrice,
    required this.savingsPercentage,
    this.appliedCampaignTitle,
    required this.pricingBreakdownNotes,
  });

  factory PricingPreviewResult.fromJson(Map<String, dynamic> json) {
    return PricingPreviewResult(
      durationDays: json['durationDays'] as int,
      subtotalBeforeDiscount: (json['subtotalBeforeDiscount'] as num).toDouble(),
      campaignDiscountApplied: (json['campaignDiscountApplied'] as num).toDouble(),
      finalCustomerPrice: (json['finalCustomerPrice'] as num).toDouble(),
      savingsPercentage: (json['savingsPercentage'] as num).toDouble(),
      appliedCampaignTitle: json['appliedCampaignTitle'] as String?,
      pricingBreakdownNotes: (json['pricingBreakdownNotes'] as List<dynamic>?)
              ?.map((e) => e.toString())
              .toList() ??
          [],
    );
  }
}
```

---

## 5. قواعد العمل والتحقق (Business & Validation Rules)

1. **فحص الحد الأدنى للمدة (`minSubscriptionDays`)**:
   - إذا كان العرض يحدد `minSubscriptionDays: 26`، فالباقات ذات الـ 6 أيام أو 13 يوماً تُظهر إشعاراً توضيحياً: `(هذا العرض متاح للباقات الشهرية 26 يوم فقط)`، وإذا اختارها العميل يظهر السعر الأصلي كاملاً بدون احتساب خصم العرض.
2. **عدم حساب الأسعار محلياً (Server-Driven Pricing)**:
   - تطبيق الهاتف لا يقوم بحساب `41.600 = 52.000 * 0.80` بنفسه في الدفع. بل يطلب `GET /preview`، ويستخدم قيمة `finalCustomerPrice` المستلمة من الخادم لضمان الدقة وتجنب التلاعب.
3. **الحفاظ على `offerId` في مسار الطلب**:
   - عند الضغط على زر `استكمال الشراء`، يتم تمرير كائن `OfferDto` مع `durationDays` و `finalCustomerPrice` إلى شاشة ملخص الدفع `12.05`.

---

## 6. إجراءات الزر السفلي (CTA Flow)

```dart
void onProceedToCheckoutPressed(BuildContext context, PackageTierOption selectedPackage, OfferDto offer) {
  // الانتقال إلى شاشة 12.05 Checkout Confirmation
  Navigator.push(
    context,
    MaterialPageRoute(
      builder: (context) => PromoAppliedCheckoutScreen(
        offer: offer,
        selectedPackage: selectedPackage,
        durationDays: selectedPackage.durationDays,
      ),
    ),
  );
}
```
