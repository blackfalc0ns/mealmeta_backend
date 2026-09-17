# شاشة 12.03 — رمز العرض وإدخال الكوبون (Offers / Promo Code Input)

**السابق:** [المركز الرئيسي (12.10)](./07-offers-hub-and-referral.md) · **التالي بعد التحقق:** [اختيار الباقة والاسترداد (12.04)](./04-redeem-and-select-package.md)

---

## 1. هدف الشاشة

تمكين العميل من تفعيل كود خصم ترويجي أو كوبون يدوي (عن طريق الكتابة أو اللصق السريع من الحافظة)، أو اختيار أحد الأكواد المتاحة المقترحة مسبقاً في النظام (`SAVE20`, `FIT10`, `WELCOME`)، وفحص صلاحية الكود ونسبة الخصم فورياً عبر السيرفر قبل الانتقال لاختيار الباقة.

---

## 2. عناصر الشاشة في التصميم

1. **الرسم التوضيحي والعنوان (Illustration & Header)**:
   - أيقونة الظرف مع بطاقة الهدية الخضراء.
   - العنوان: `فعّل عرضك`.
   - النص الإرشادي: `أدخل أو الصق رمز العرض للاستفادة من المزايا المتاحة على الخطة الموثقة`.
2. **حقل إدخال الكود (Input Field with Paste Action)**:
   - حقل نصي كبير يقبل الحروف والأرقام بالإنجليزية (مثل: `SAVE20`).
   - أيقونة اللصق (Paste Clipboard Icon): عند الضغط عليها تقرأ من حافظة الهاتف وتضع النص فوراً في الحقل.
   - زر الإجراء: `تطبيق` (Apply).
3. **قائمة الرموز المتاحة (Available Promo Codes Carousel / List)**:
   - تعرض الكروت الجاهزة للاختيار بنقرة واحدة:
     - كارت `SAVE20` (خصم 20% على الباقات).
     - كارت `FIT10` (خصم 10% للرياضيين).
     - كارت `WELCOME` (خصم 15% للمشتركين الجدد).
   - زر سريع أسفل كل كارت: `تأكيد الرمز` ⬅️ يملأ الحقل ويتحقق منه تلقائياً.

---

## 3. الـ APIs المطلوبة

### التحقق من الكود وحساب الخصم بالسيرفر (بدون استهلاك)

```http
POST /api/v1/customer/offers/evaluate
Authorization: Bearer {Customer_JWT_Token}
Content-Type: application/json
```

#### Request Body:
```json
{
  "offerId": "7b85f264-b040-4591-a1b7-a36f90382345",
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "cartAmount": 52.000,
  "durationDays": 26,
  "hasPromoCode": true
}
```

#### Response Body — حالة النجاح (200 OK):
```json
{
  "offerId": "7b85f264-b040-4591-a1b7-a36f90382345",
  "isEligible": true,
  "calculatedDiscountAmount": 10.400,
  "ineligibilityReasons": [],
  "allowPromoCodeStacking": false
}
```

#### Response Body — حالة الفشل أو عدم الأهلية (200 OK):
```json
{
  "offerId": "7b85f264-b040-4591-a1b7-a36f90382345",
  "isEligible": false,
  "calculatedDiscountAmount": 0.000,
  "ineligibilityReasons": [
    "Offer does not allow stacking with additional promo codes.",
    "Offer is restricted to new customers only."
  ],
  "allowPromoCodeStacking": false
}
```

---

## 4. منطق الواجهة وتجربة المستخدم في Flutter

```dart
void applyPromoCode(String code) async {
  setState(() => _isLoading = true);
  
  try {
    final result = await offersApiService.evaluateOffer(
      offerCode: code.trim().toUpperCase(),
      cartAmount: currentCartAmount,
      durationDays: 26,
    );
    
    if (result.isEligible) {
      // إظهار إشعار النجاح والانتقال لشاشة اختيار الباقة مع تثبيت الكود المطبق
      Navigator.pushNamed(context, '/offers/redeem', arguments: {
        'offerId': result.offerId,
        'promoCode': code,
        'discountAmount': result.calculatedDiscountAmount,
      });
    } else {
      // إظهار سبب الرفض
      showErrorSnackBar(result.ineligibilityReasons.first);
    }
  } catch (e) {
    showErrorSnackBar("رمز العرض غير صحيح أو منتهي الصلاحية");
  } finally {
    setState(() => _isLoading = false);
  }
}
```

---

## 5. جدول رسائل الخطأ والتنبيهات المترجمة

| كود أو سبب الخطأ | الرسالة المعروضة للمستخدم |
| :--- | :--- |
| **`Offer.NotFound`** | "رمز العرض المدخل غير صحيح، تأكد من صحة الرمز وحاول مجدداً." |
| **`Offer.Expired`** | "عذراً، هذا العرض منتهي الصلاحية." |
| **`Offer.UsageLimit`** | "عذراً، اكتمل العدد الأقصى للمستفيدين من هذا العرض." |
| **`Offer.NotStackable`** | "لا يمكن دمج هذا الكود مع عروض ترويجية أخرى." |
| **`Offer.NewCustomersOnly`** | "هذا الرمز مخصص للمشتركين الجدد فقط." |
