# شاشة 12.02 — تفاصيل العرض (Offers / Offer Details)

**السابق:** [تصفح العروض (12.01)](./01-browse-offers.md) · **التالي:** [اختيار الباقة والاسترداد (12.04)](./04-redeem-and-select-package.md)

---

## 1. هدف الشاشة

عرض الصفحة الكاملة لمواصفات العرض الترويجي أو الحملة المشتركة، شاملة تفاصيل الخصم، العداد التنازلي التفاعلي المباشر (أيام : ساعات : دقائق : ثواني)، كود الخصم المفعّل مع ميزة النسخ للحافظة، جدول الشروط والأحكام الكامل، وتوجيه العميل إلى شاشة اختيار الباقة المناسبة.

---

## 2. عناصر الشاشة في التصميم

1. **شريط العنوان العلوي (App Bar)**:
   - زر الرجوع الخلفي `<-`.
   - عنوان الشاشة: `تفاصيل العرض`.
   - أيقونة الإشعارات.
2. **البانر الرئيسي وشارة الخصم**:
   - صورة الوجبات الصحية العالية الجودة (`bannerImageUrl`).
   - بادج الخصم الدائري: `خصم 20% على الباقات الشهرية`.
3. **عداد الوقت المتبقي (Live Countdown Box)**:
   - تصميم بارز يعرض: `باقي على: 26 : 18 : 10 : 15` (أيام : ساعات : دقائق : ثواني).
   - يتناقص لحظياً كل ثانية في تطبيق الفلاتر عبر `Timer.periodic`.
4. **كود الخصم المفعّل (Active Promo Code Box)**:
   - كارت ملون يحتوي على: `كود مفعّل: SAVE20`.
   - أيقونة النسخ (Copy Icon): تضغط عليها لنسخ الكود إلى الـ Clipboard مع إظهار Toast / SnackBar: "تم نسخ رمز العرض".
5. **الشارات التحفيزية (Feature Badges)**:
   - `أكثر مبيعاً` (لو العرض عليه طلب عالي).
   - `متاح للجميع` (لو `customerSegment == All`).
   - `مرونة في الاختيار`.
6. **جدول المواصفات والشروط (Specifications Table)**:
   - **نوع العرض**: خصم على الباقات الشهرية (أو نسبة مئوية).
   - **مدة العرض**: عداد الانتهاء وتاريخ النهاية `endsAtUtc`.
   - **صالح حتى**: البرامج أو الباقات المشمولة (`eligiblePrograms` / `eligibleBundles`).
   - **الشروط**: لا يمكن دمجه مع عروض أخرى (`allowPromoCodeStacking == false`).
   - **رمز العرض**: الكود المخصص مع زر نسخ سريع.
7. **قسم "لماذا هذا العرض؟" (Value Propositions)**:
   - `توفير أكبر`: توفير مالي حقيقي ومباشر على الفاتورة.
   - `مناسب للأهداف`: خطة تغذية متكاملة تساعد على الالتزام.
   - `مرونة في الاختيار`: حرية اختيار الوجبات وتغيير المواعيد.
8. **زر الإجراء الرئيسي (Primary CTA)**:
   - `اختر الباقة المناسبة` ⬅️ ينقل المستخدم مباشرة لشاشة **12.04**.

---

## 3. الـ APIs المطلوبة

### جلب تفاصيل العرض الفردي بالـ ID

```http
GET /api/v1/customer/offers/{offerId}
Authorization: Bearer {Customer_JWT_Token}
```

#### Response Body (200 OK):
```json
{
  "id": "7b85f264-b040-4591-a1b7-a36f90382345",
  "nameAr": "خصم 20% على الباقات الشهرية",
  "nameEn": "20% Off Monthly Packages",
  "descriptionAr": "خطط غذائية متكاملة تساعدك على تحقيق أهدافك البدنية بأفضل سعر",
  "descriptionEn": "Comprehensive nutrition plans to help you reach your physical goals",
  "bannerImageUrl": "https://cdn.mealmate.app/offers/monthly_20.png",
  "status": 3,
  "type": 1,
  "discountType": 1,
  "fundingMode": 3,
  "discountRate": 0.200000,
  "discountAmountKwd": null,
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
```

---

## 4. كود إدارة العداد التنازلي في Flutter

```dart
class OfferCountdownWidget extends StatefulWidget {
  final DateTime endsAtUtc;

  const OfferCountdownWidget({Key? key, required this.endsAtUtc}) : super(key: key);

  @override
  _OfferCountdownWidgetState createState() => _OfferCountdownWidgetState();
}

class _OfferCountdownWidgetState extends State<OfferCountdownWidget> {
  late Timer _timer;
  late Duration _remaining;

  @override
  void initState() {
    super.initState();
    _calculateRemaining();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) {
        setState(() {
          _calculateRemaining();
        });
      }
    });
  }

  void _calculateRemaining() {
    final diff = widget.endsAtUtc.difference(DateTime.now().toUtc());
    _remaining = diff.isNegative ? Duration.zero : diff;
  }

  @override
  void dispose() {
    _timer.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final days = _remaining.inDays.toString().padLeft(2, '0');
    final hours = (_remaining.inHours % 24).toString().padLeft(2, '0');
    final minutes = (_remaining.inMinutes % 60).toString().padLeft(2, '0');
    final seconds = (_remaining.inSeconds % 60).toString().padLeft(2, '0');

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
      decoration: BoxDecoration(
        color: const Color(0xFFF1F8F1),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Text(
        "باقي على: $days : $hours : $minutes : $seconds",
        style: const TextStyle(
          color: Color(0xFF2E7D32),
          fontWeight: FontWeight.bold,
          fontSize: 15,
        ),
      ),
    );
  }
}
```

---

## 5. قواعد الانتقال والربط مع الشاشة التالية

- عند الضغط على زر **`اختر الباقة المناسبة`**:
  - يتم تمرير كائن `offer` (أو `offerId`) إلى شاشة **`12.04 (Redeem & Select Package)`**.
  - لا يتم استدعاء أي API لحجز العرض في هذه الشاشة (مجرد معاينة وتصفح).
