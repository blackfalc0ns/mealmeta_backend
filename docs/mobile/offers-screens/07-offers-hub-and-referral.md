# شاشة 12.10 — المركز الرئيسي للعروض وبرنامج الإحالة (Offers / Referral & Special Deals Hub)

**التالي:** [تصفح جميع العروض (12.01)](./01-browse-offers.md) · **أو إدخال كود:** [رمز العرض (12.03)](./03-promo-code-input.md)

---

## 1. هدف الشاشة

تعتبر هذه الشاشة هي **الواجهة الرئيسية (Hub)** لتبويب "العروض" في شريط التنقل السفلي للتطبيق (`Bottom Navigation Bar`). تجمع الشاشة بين العروض والحملات الترويجية الكبرى (Hero Banners)، والتصنيفات السريعة، والعروض الأكثر طلباً، مع بانر استراتيجي لبرنامج إحالة الأصدقاء (`Referral Program`) الذي يمنح مكافآت خصم 30%.

---

## 2. عناصر الشاشة في التصميم

1. **الهيدر العلوي وشريط البحث (Top Bar)**:
   - أيقونة الإشعارات (مع نقطة التنبيهات الحمراء).
   - حقل البحث السريع: للبحث في العروض والباقات المتاحة.
   - عنوان التبويب: `العروض`.
2. **سلايدر العروض الكبرى (Hero Banner Slider)**:
   - عرض دوار (Carousel) مع مؤشرات الصفحات (Dots Indicator):
     - شارة الخصم: `خصم 30% على جميع الباقات`.
     - النص الترويجي: `ابدأ رحلتك الصحية اليوم`.
     - زر: `اشترك الآن` ⬅️ ينقل مباشرة لشاشة استرداد العرض واختيار الباقة `12.04`.
     - صورة علب وجبات MealMate الفاخرة.
3. **تصنيفات وفلاتر العروض (Category Chips)**:
   - `الكل` (محدد باللون الأخضر افتراضياً).
   - `اشتراكات`.
   - `كوبونات`.
   - `تغذية محدودة`.
   - `وجبات`.
4. **قسم عروض مميزة (Featured Deals Carousel / Grid)**:
   - كروت أفقية قابلة للتمرير مع شارة `الأكثر طلباً`:
     - **الباقة الشهرية**: خصم 20% | ~~52.000~~ **41.600 KWD**.
     - **باقة البروتين**: خصم 25% | ~~66.000~~ **50.000 KWD**.
     - **اشتراك 3 أشهر**: خصم 15% | ~~117.000~~ **100.000 KWD**.
5. **بانر كود الخصم المباشر (Promo Code Callout)**:
   - النص: `استخدم كود الخصم للحصول على خصم على اشتراكك`.
   - زر: `استفد الآن ⬅️` ⬅️ ينقل المستخدم لشاشة إدخال الكوبون `12.03`.
6. **بانر دعوة الأصدقاء وبرنامج الإحالة (Referral Banner)**:
   - رسم توضيحي لأصدقاء يشاركون التطبيق.
   - العنوان: `ادع أصدقائك واحصل على خصم 30%`.
   - زر الإجراء: `دعوة الأصدقاء` ⬅️ يفتح نافذة المشاركة لمشاركة رابط وكود الإحالة الخاص بالعميل.
7. **شريط التنقل السفلي (Bottom Navigation Bar)**:
   - `الرئيسية` (Home).
   - `الوجبات` (Meals).
   - `العروض` (Offers - التبويب النشط باللون الأخضر).
   - `اشتراكات` (Subscriptions).
   - `البروفايل` (Profile).

---

## 3. الـ APIs المطلوبة

### 1) جلب العروض النشطة والمميزة للـ Hub

```http
POST /api/v1/customer/offers/available
Authorization: Bearer {Customer_JWT_Token}
Content-Type: application/json
```

#### Request Body:
> **ملاحظة هامة للمطور:** جميع هذه الحقول **اختيارية (Optional)**. المستخدم لا يكتب أي قيمة بيده! يقوم كود التطبيق في Flutter بإرسالها تلقائياً، أو يمكن إرسال جسم فارغ `{}` وسيقوم الباك إند بالتعرف على المستخدم من الـ Token.

```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "cartAmount": 0,
  "durationDays": 0,
  "deliveryGovernorate": "العاصمة"
}
```

* **`customerId`**: يُستخرج تلقائياً من توكن الدخول (`JWT Token`) للعميل المسجل.
* **`cartAmount`**: يرسلها التطبيق تلقائياً `0` لأن العميل يتصفح العروض فقط ولم يختر سلة بعد.
* **`durationDays`**: ترسل `0` أو تُترك فارغة لجلب كافة العروض لجميع المدد أثناء التصفح.
* **`deliveryGovernorate`**: يقرأها التطبيق تلقائياً من العنوان المختار حالياً بأعلى الشاشة (مثل: "العاصمة" أو "حولي"). إذا لم يختر عنواناً تُترك `null` لعرض كل العروض.

---

### 2) جلب كود الإحالة ورابط المشاركة الخاص بالعميل

```http
GET /api/v1/customers/me
Authorization: Bearer {Customer_JWT_Token}
```

#### Response Body (200 OK):
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "fullName": "أحمد عبد الله",
  "referralCode": "AHMED30",
  "referralLink": "https://mealmate.app/ref/AHMED30",
  "referralRewardRate": 0.30
}
```

---

## 4. نموذج البيانات المقترح في Flutter (Dart Models)

```dart
class OffersHubData {
  final List<OfferDto> heroBanners;
  final List<OfferDto> featuredDeals;
  final String? customerReferralCode;
  final String? customerReferralLink;

  const OffersHubData({
    required this.heroBanners,
    required this.featuredDeals,
    this.customerReferralCode,
    this.customerReferralLink,
  });
}
```

---

## 5. أحداث التفاعل والتنقل (Navigation Triggers)

```dart
void handleHubNavigation(BuildContext context, String trigger, {dynamic data}) {
  switch (trigger) {
    case 'HERO_BANNER_CLICKED':
    case 'FEATURED_DEAL_CLICKED':
      final offer = data as OfferDto;
      Navigator.push(
        context,
        MaterialPageRoute(
          builder: (context) => OfferDetailsScreen(offerId: offer.id),
        ),
      );
      break;

    case 'PROMO_CODE_BANNER_CLICKED':
      Navigator.push(
        context,
        MaterialPageRoute(
          builder: (context) => const PromoCodeInputScreen(),
        ),
      );
      break;

    case 'REFERRAL_SHARE_CLICKED':
      final referralLink = data as String;
      Share.share(
        'اشترك في MealMate الآن واحصل على خصم 30% على وجباتك الصحية عبر الرابط التالي: $referralLink',
        subject: 'خصم 30% من وجبات MealMate',
      );
      break;

    case 'VIEW_ALL_OFFERS_CLICKED':
      Navigator.push(
        context,
        MaterialPageRoute(
          builder: (context) => const BrowseOffersScreen(),
        ),
      );
      break;
  }
}
```
