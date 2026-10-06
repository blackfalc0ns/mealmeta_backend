# دليل الموبايل: خطوات إكمال متطلبات الاشتراك (العنوان والحساسية والتفضيلات)

دليل موجه لمطوري تطبيق الموبايل (Flutter) يشرح بالتفصيل **كيفية حل حالات الـ `409 Conflict` الثلاث** التي تظهر عند الضغط على زر «المتابعة للتصنيف» في مسار الاشتراك، وكيفية إرسال البيانات الصحيحة لكل خطوة حتى تكتمل المتطلبات ويفتح مسار التصنيفات بنجاح.

---

## 1. جدول البوابات الثلاث (The 3 Prerequisite Gates)

عند ضغط العميل على زر «المتابعة للتصنيف»، يتم فحص الحساب عبر:
- `GET /api/v1/customer/subscription-flow/prerequisites`
- أو مباشرة عبر `GET .../bundles/{bundleId}/tiers`

إذا كان هناك نقص في بيانات العميل الأساسية، يرجع الـ Backend بحالة **`409 Conflict`** بالترتيب الإلزامي التالي:

| الترتيب | كود الخطأ `error` | الـ `missingStep` | الـ `actionEndpoint` | الإجراء المطلوب من الموبايل |
|:-------:|-------------------|-------------------|----------------------|-----------------------------|
| **1** | `checkout.address_required` | `ADDRESS` | `POST /api/v1/customers/me/addresses` | فتح شاشة الخريطة لحفظ عنوان التوصيل |
| **2** | `checkout.allergies_required` | `ALLERGIES` | `GET /api/v1/customers/me/food-preferences/allergies` | فتح شاشة تأكيد الحساسية الغذائية |
| **3** | `checkout.dietary_preferences_required` | `DIETARY_PREFERENCES` | `GET /api/v1/customers/me/food-preferences/dietary` | فتح شاشة تأكيد التفضيلات والأنظمة الغذائية |

---

## 2. الخطوة الأولى: حل نقص العنوان (`checkout.address_required`)

يحدث هذا الخطأ عندما لا يملك العميل أي عنوان توصيل محفوظ في حسابه، أو عندما لا يحتوي عنوانه على `RegionId` معتمد.

### 2.1 تحديد الموقع من الخريطة (Reverse Geocode)
يقوم العميل باختيار الموقع على الخريطة والضغط على «تأكيد الموقع»:

```http
POST /api/v1/maps/reverse-geocode
Content-Type: application/json
```
**Body:**
```json
{
  "latitude": 29.339405,
  "longitude": 48.081870
}
```

**الاستجابة المرجعة:**
```json
{
  "formattedAddress": "17 شارع 2 جادة 2، السالمية، الكويت",
  "latitude": 29.341213,
  "longitude": 48.082421,
  "governorate": "حولي",
  "area": "السالمية",
  "block": "قطعة 5",
  "street": "شارع 2",
  "avenue": "جادة 2",
  "suggestedGovernorateId": "ec19c873-a752-4797-a6e3-068ddd3ccc2a",
  "suggestedRegionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "suggestedBlockId": "817ce4d2-7941-4c58-a0a9-8aa72bb93610"
}
```

### 2.2 إرسال طلب حفظ العنوان
يقوم العميل بإدخال بيانات المبنى/الدور/الشقة يدوياً، ويرسل التطبيق طلب الحفظ:

```http
POST /api/v1/customers/me/addresses
Authorization: Bearer <customer-access-token>
Content-Type: application/json
```
**Body:**
```json
{
  "label": "المنزل",
  "addressLine": "17 شارع 2 جادة 2، السالمية، الكويت",
  "regionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "blockRegionId": "817ce4d2-7941-4c58-a0a9-8aa72bb93610",
  "latitude": 29.341213,
  "longitude": 48.082421,
  "isDefault": true,
  "notifyOnDelivery": true,
  "governorate": "حولي",
  "area": "السالمية",
  "block": "قطعة 5",
  "street": "شارع 2",
  "avenue": "جادة 2",
  "building": "17",
  "floor": "2",
  "unit": "5"
}
```

**نتيجة الحفظ:**
- يتم حفظ العنوان وتعيينه عنواناً افتراضياً للتوصيل.
- ترجع الاستجابة بقائمة `servingRestaurants` التي تغطي عنوان العميل وتنبيه التغطية `restaurantCoverageNotice` (التنبيه يُعاد فقط عند وجود اشتراك نشط؛ وإلا فهو `null`).
- بمجرد اكتمال هذه الخطوة، يتم الانتقال تلقائياً للتحقق من الخطوة التالية.

---

## 3. الخطوة الثانية: حل نقص الحساسية (`checkout.allergies_required`)

يحدث هذا الخطأ إذا لم يقم العميل بتأكيد حساسيته الغذائية من قبل (`AllergiesCompletedAtUtc == null`).

> [!IMPORTANT]
> **التأكيد إلزامي حتى لو لم يكن لدى العميل أي حساسية!**
> يجب على العميل الدخول للشاشة وتأكيد خياراته (إما اختيار حساسيات معينة، أو اختيار "لا توجد حساسية").

### 3.1 جلب قائمة الحساسيات المعتمدة
```http
GET /api/v1/customers/me/food-preferences/allergies?page=1&pageSize=50
Authorization: Bearer <customer-access-token>
```

**الاستجابة:**
```json
{
  "allergens": [
    {
      "code": "NONE",
      "nameAr": "لا توجد حساسية",
      "nameEn": "No allergies",
      "isSelected": true,
      "isNone": true
    },
    {
      "code": "MILK",
      "nameAr": "الحليب ومنتجات الألبان",
      "nameEn": "Milk",
      "isSelected": false,
      "isNone": false
    },
    {
      "code": "EGGS",
      "nameAr": "البيض",
      "nameEn": "Eggs",
      "isSelected": false,
      "isNone": false
    },
    {
      "code": "NUTS",
      "nameAr": "المكسرات",
      "nameEn": "Tree Nuts",
      "isSelected": false,
      "isNone": false
    }
  ]
}
```

### 3.2 إرسال تأكيد الحساسية (حفظ الاختيارات)
```http
PUT /api/v1/customers/me/food-preferences/allergies
Authorization: Bearer <customer-access-token>
Content-Type: application/json
```

- **الحالة الأولى: العميل لديه حساسية محددة (مثال: الحليب والبيض):**
  ```json
  {
    "allergies": [
      { "code": "MILK", "nameAr": "الحليب ومنتجات الألبان", "nameEn": "Milk" },
      { "code": "EGGS", "nameAr": "البيض", "nameEn": "Eggs" }
    ]
  }
  ```

- **الحالة الثانية: العميل لا يعاني من أي حساسية (اختار "لا توجد حساسية"):**
  يرسل التطبيق مصفوفة فارغة `[]`:
  ```json
  {
    "allergies": []
  }
  ```

**نتيجة الحفظ:**
يقوم الـ Backend بتحديث جدول `CustomerAllergyPreferences` وتعيين `profile.AllergiesCompletedAtUtc = DateTime.UtcNow`، مما يحل هذا العائق نهائياً.

---

## 4. الخطوة الثالثة: حل نقص التفضيلات الغذائية (`checkout.dietary_preferences_required`)

يحدث هذا الخطأ إذا لم يقم العميل بتأكيد برامجه وتفضيلاته الغذائية من قبل (`DietaryPreferencesCompletedAtUtc == null`).

> [!IMPORTANT]
> **التأكيد إلزامي حتى لو كان العميل لا يتبع أي حمية خاصة!**
> يجب على العميل تأكيد ما إذا كان يتبع نظاماً (كيتو، نباتي، خالي من الجلوتين...) أو أنه "لا يتبع نظاماً معيناً".

### 4.1 جلب قائمة التفضيلات والأنظمة الغذائية
```http
GET /api/v1/customers/me/food-preferences/dietary?page=1&pageSize=50
Authorization: Bearer <customer-access-token>
```

**الاستجابة:**
```json
{
  "dietaryPreferences": [
    {
      "code": "NONE",
      "nameAr": "لا أتبع نظاماً غذائياً معيناً",
      "nameEn": "No specific diet",
      "isSelected": true,
      "isNone": true
    },
    {
      "code": "KETO",
      "nameAr": "كيتو",
      "nameEn": "Keto",
      "isSelected": false,
      "isNone": false
    },
    {
      "code": "VEGETARIAN",
      "nameAr": "نباتي",
      "nameEn": "Vegetarian",
      "isSelected": false,
      "isNone": false
    },
    {
      "code": "GLUTEN_FREE",
      "nameAr": "خالي من الجلوتين",
      "nameEn": "Gluten Free",
      "isSelected": false,
      "isNone": false
    }
  ]
}
```

### 4.2 إرسال تأكيد التفضيلات
```http
PUT /api/v1/customers/me/food-preferences/dietary
Authorization: Bearer <customer-access-token>
Content-Type: application/json
```

- **الحالة الأولى: العميل يتبع نظاماً محدداً (مثال: كيتو):**
  ```json
  {
    "dietaryPreferenceCodes": ["KETO"]
  }
  ```

- **الحالة الثانية: العميل لا يتبع أي نظام محدد (اختار "لا أتبع نظاماً معيناً"):**
  يرسل التطبيق مصفوفة فارغة `[]`:
  ```json
  {
    "dietaryPreferenceCodes": []
  }
  ```

**نتيجة الحفظ:**
يقوم الـ Backend بتحديث التفضيلات وتعيين `profile.DietaryPreferencesCompletedAtUtc = DateTime.UtcNow`، مما يحل هذا العائق نهائياً.

---

## 5. مخطط دورة المعالجة التلقائية في Flutter (Auto-Resolution Loop)

هذا الكود هو الطريقة الموصى بها لربط زر «المتابعة للتصنيف» بحيث يوجّه العميل بسلاسة عبر النواقص بالترتيب:

```dart
Future<void> onProceedToClassificationClicked({
  required BuildContext context,
  required String programId,
  required String bundleId,
  required int durationDays,
  bool includeFriday = false,
}) async {
  showLoadingIndicator(context);

  try {
    // 1. فحص المتطلبات عبر نقطة التحقق المخصصة للزر
    final response = await dio.get('/api/v1/customer/subscription-flow/prerequisites');

    hideLoadingIndicator(context);

    // 2. إذا رجع 200 OK -> كل المتطلبات مكتملة -> فتح شاشة التصنيفات
    if (context.mounted) {
      Navigator.pushNamed(
        context,
        '/subscription-tiers-screen',
        arguments: {
          'programId': programId,
          'bundleId': bundleId,
          'durationDays': durationDays,
          'includeFriday': includeFriday,
        },
      );
    }
  } on DioException catch (e) {
    hideLoadingIndicator(context);

    if (e.response?.statusCode == 409) {
      final error = e.response?.data['error'];

      switch (error) {
        case 'checkout.address_required':
          // توجيه العميل لشاشة الخريطة لحفظ العنوان
          final addressSaved = await Navigator.pushNamed(context, '/map-delivery-address');
          // بعد الحفظ، يعيد الزر الفحص آلياً للتحقق من الخطوة التالية
          if (addressSaved == true && context.mounted) {
            onProceedToClassificationClicked(
              context: context,
              programId: programId,
              bundleId: bundleId,
              durationDays: durationDays,
              includeFriday: includeFriday,
            );
          }
          break;

        case 'checkout.allergies_required':
          // توجيه العميل لشاشة تأكيد الحساسية
          final allergiesConfirmed = await Navigator.pushNamed(context, '/confirm-allergies');
          if (allergiesConfirmed == true && context.mounted) {
            onProceedToClassificationClicked(
              context: context,
              programId: programId,
              bundleId: bundleId,
              durationDays: durationDays,
              includeFriday: includeFriday,
            );
          }
          break;

        case 'checkout.dietary_preferences_required':
          // توجيه العميل لشاشة تأكيد البرامج الغذائية
          final dietaryConfirmed = await Navigator.pushNamed(context, '/confirm-dietary');
          if (dietaryConfirmed == true && context.mounted) {
            onProceedToClassificationClicked(
              context: context,
              programId: programId,
              bundleId: bundleId,
              durationDays: durationDays,
              includeFriday: includeFriday,
            );
          }
          break;

        default:
          showErrorDialog(context, e.response?.data['error'] ?? 'حدث خطأ غير متوقع');
      }
    } else {
      showErrorDialog(context, 'تعذر الاتصال بالخادم، يرجى المحاولة لاحقاً');
    }
  }
}
```

---

## 6. خلاصة وتأكيدات للمطور

1. **الترتيب ثابت ومحمي من السيرفر:** العنوان أولاً ← الحساسية ثانياً ← التفضيلات ثالثاً.
2. **التأكيد لمرة واحدة فقط:** بمجرد إكمال العميل لأي متطلب، يُسجل تاريخ إكماله في حسابه ولا يُطلب منه ثانية عند الاشتراك في برامج أخرى (إلا إذا رغب هو في تعديلها من إعدادات حسابه).
3. **خيار الـ NONE صالح دائماً:** لا يلزم أن يكون لدى العميل حساسية أو نظام كيتو؛ اختيار "لا يوجد" وإرسال مصفوفة فارغة `[]` يُعد إكمالاً صالحاً ورسمياً للمتطلب.
4. **بعد اكتمال الخطوات الثلاث:** تفتح شاشة التصنيف فوراً، وتكون جميع المطاعم المعروضة مطابقة جغرافياً لعنوان العميل ومطابقة لسلامته الغذائية.
