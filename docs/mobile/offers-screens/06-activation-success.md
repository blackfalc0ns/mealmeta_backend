# شاشة 12.06 — تأكيد تفعيل العرض بنجاح (Offers / Activation Success & Plan Setup)

**السابق:** [ملخص وتأكيد الدفع (12.05)](./05-promo-applied-checkout.md) · **التالي:** [تقويم اختيار الوجبات (Calendar Meals Flow)](/api/v1/customer/subscriptions/{subscriptionId}/calendar)

---

## 1. هدف الشاشة

إشعار العميل بنجاح عملية الاشتراك وتطبيق العرض الترويجي عليه بشكل رسمي، وعرض ملخص الفاتورة المدفوعة، مع توفير زر التحويل المباشر للانتقال إلى تقويم اختيار الوجبات والمطاعم للأيام المشترك بها (`Meal Calendar & Food Selection`).

---

## 2. عناصر الشاشة في التصميم

1. **الهيدر والترويسة**:
   - زر إغلاق `(X)` وزر سهم رجوع.
   - العنوان الرئيسي: `تم تفعيل العرض بنجاح` مصحوباً بأيقونة الاحتفال أو علامة التحقق الخضراء.
2. **بانر العرض المفعل (Activated Offer Banner)**:
   - صورة الوجبة المميزة.
   - عنوان العرض: `خصم 20% على الباقات الشهرية`.
   - الوصف: `خطط غذائية متكاملة تساعدك على تحقيق أهدافك`.
   - عداد الصلاحية: `ينتهي خلال : 02 يوم : 14 ساعة`.
3. **كارت ملخص طلبك (Your Activated Order Card)**:
   - صورة بوكس الوجبات الرسمي لـ MealMate.
   - الباقة: `باقة شهر - 26 يوم (4 وجبات يومياً)`.
   - السعر الأساسي: `52.000 KWD`.
   - الخصم (20%): `-10.400 KWD`.
   - **الإجمالي بعد الخصم (المدفوع)**: **`41.600 KWD`**.
4. **كود الخصم للمستخدم (Applied Promo Badge)**:
   - صندوق كود العرض: `كود الخصم للمستخدم: SAVE20`.
   - زر نسخ وتأكيد التطبيق `[نسخ]`.
5. **شروط وتفاصيل العرض (Specs)**:
   - `خصم 20% على جميع الباقات الشهرية`.
   - مدة وسريان العرض.
   - الشروط والأحكام: `يسري العرض على باقات شهرية فقط ، لا يمكن دمجه مع عروض أخرى`.
6. **زر الإجراء الرئيسي (Primary Action CTA)**:
   - زر أخضر بارز: `اختر خطة الباقة الآن`.
   - عند الضغط عليه: يتم التوجيه إلى شاشة اختيار الوجبات اليومية والمطاعم للمدة المشتراة.

---

## 3. الـ APIs المطلوبة

### 1) الاستعلام عن حالة الاشتراك النشط بعد الدفع

```http
GET /api/v1/customer/subscriptions/{subscriptionId:guid}/payment
Authorization: Bearer {Customer_JWT_Token}
```

#### Response Body (200 OK):
```json
{
  "subscriptionId": "8a7c6b5d-4e3f-2a1b-9c8d-7e6f5a4b3c2d",
  "status": "Active",
  "isPaid": true,
  "paymentMethod": "KNet",
  "totalAmountKwd": 41.600,
  "originalAmountKwd": 52.000,
  "discountAmountKwd": 10.400,
  "appliedOfferTitle": "خصم 20% على الباقات الشهرية",
  "durationDays": 26,
  "startDate": "2026-10-01",
  "endDate": "2026-10-26"
}
```

---

### 2) الانتقال إلى جدول وتقويم الوجبات (Subscription Calendar)

عند ضغط العميل على زر `اختر خطة الباقة الآن`:

```http
GET /api/v1/customer/subscriptions/{subscriptionId:guid}/calendar
Authorization: Bearer {Customer_JWT_Token}
```

#### Response Body (200 OK):
```json
{
  "subscriptionId": "8a7c6b5d-4e3f-2a1b-9c8d-7e6f5a4b3c2d",
  "status": "Active",
  "startDate": "2026-10-01",
  "endDate": "2026-10-26",
  "totalDays": 26,
  "filledDays": 0,
  "remainingDaysToSelect": 26,
  "days": [
    {
      "date": "2026-10-01",
      "dayOfWeekAr": "الخميس",
      "isConfigured": false,
      "selectedRestaurant": null,
      "mealsCount": 0
    }
  ]
}
```

---

## 4. كود التوجيه في Flutter (Dart Navigation Snippet)

```dart
class ActivationSuccessScreen extends StatelessWidget {
  final String subscriptionId;
  final AppliedOfferSummary summary;

  const ActivationSuccessScreen({
    Key? key,
    required this.subscriptionId,
    required this.summary,
  }) : super(key: key);

  void _navigateToMealSelectionCalendar(BuildContext context) {
    // توجيه العميل إلى تقويم اختيار وجبات الاشتراك
    Navigator.pushAndRemoveUntil(
      context,
      MaterialPageRoute(
        builder: (context) => CustomerSubscriptionCalendarScreen(
          subscriptionId: subscriptionId,
        ),
      ),
      (route) => route.isFirst, // تنظيف مكدس شاشات الدفع
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('تم تفعيل العرض بنجاح'),
        centerTitle: true,
        leading: IconButton(
          icon: const Icon(Icons.close),
          onPressed: () => Navigator.of(context).popUntil((route) => route.isFirst),
        ),
      ),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(16.0),
        child: Column(
          children: [
            _buildHeroBanner(),
            const SizedBox(height: 16),
            _buildOrderSummaryCard(summary),
            const SizedBox(height: 16),
            _buildAppliedPromoCodeBadge(summary.offerCode),
            const SizedBox(height: 16),
            _buildOfferSpecsCard(),
            const SizedBox(height: 24),
            SizedBox(
              width: double.infinity,
              height: 52,
              child: ElevatedButton(
                style: ElevatedButton.styleFrom(
                  backgroundColor: const Color(0xFF2E7D32), // اللون الأخضر المميز
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(12),
                  ),
                ),
                onPressed: () => _navigateToMealSelectionCalendar(context),
                child: const Text(
                  'اختر خطة الباقة الآن',
                  style: TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.bold,
                    color: Colors.white,
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
```

---

## 5. إدارة حالة التراجع (Back Navigation Guard)

> [!IMPORTANT]
> **عدم الرجوع لصفحة الدفع:**
> بمجرد وصول العميل لهذه الشاشة، تم قفل عملية الشراء والدفع بنجاح. يجب تعطيل زر الرجوع `WillPopScope` أو جعله يعود مباشرة إلى الصفحة الرئيسية `Home Screen` وليس إلى شاشة الدفع السابقة `12.05` لمنع تكرار طلب الدفع بالخطأ.
