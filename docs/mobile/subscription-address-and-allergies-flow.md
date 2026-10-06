# فلو متطلبات الاشتراك: عنوان التوصيل والحساسية والتفضيلات

دليل هندسي وشامل لمطوري تطبيق الموبايل (Flutter) يشرح **دورة حياة التحقق من المتطلبات الأساسية (العنوان + الحساسية + التفضيلات الغذائية)** قبل استعراض الباقات والتصنيفات في مسار الاشتراك.

---

## 1. لماذا هذا الترتيب؟ (Rationale)

في التحديث الأخير لنظام MealMate:
1. **استعراض المطاعم والوجبات يعتمد كلياً على عنوان العميل:**
   - الباقات والتصنيفات تعرض فقط المطاعم التي تغطي منطقة العميل (`RegionId`).
   - لا يمكن إظهار باقات أو مطاعم أو حساب التكلفة إلا بعد معرفة مكان التوصيل.
2. **سلامة الغذاء واستبعاد المكونات الضارة:**
   - يجب معرفة وتأكيد حساسية العميل (`Allergies`) وتفضيلاته الغذائية (`DietaryPreferences`) لضمان توفر وجبات آمنة ومطاعم مؤهلة قبل أن يختار العميل باقته ويدفع.
3. **لذلك، تم نقل الفحص ليصبح بوابة إلزامية (`Pre-requisite Gate`) عند أول شاشة في الاشتراك (`Bundles`).**

---

## 2. مخطط المسار الشامل (Flow Diagram)

```mermaid
flowchart TD
    Start([1. فتح البرنامج الغذائي]) --> CallBundles[عرض الباقات GET /bundles]
    CallBundles --> SelectDuration[2. اختيار الباقة والمدة Durations]
    SelectDuration --> ClickTierButton([3. الضغط على زر المتابعة للتصنيف])
    
    ClickTierButton --> CheckGate{فحص المتطلبات\nGET /prerequisites أو GET /tiers}
    
    CheckGate -- "409 (checkout.address_required)" --> AddressScreen[أ. شاشة الخريطة والعنوان]
    AddressScreen --> SaveAddress[POST /customers/me/addresses]
    SaveAddress --> ClickTierButton
    
    CheckGate -- "409 (checkout.allergies_required)" --> AllergiesScreen[ب. شاشة تأكيد الحساسية]
    AllergiesScreen --> SaveAllergies[PUT /customers/me/food-preferences/allergies]
    SaveAllergies --> ClickTierButton
    
    CheckGate -- "409 (checkout.dietary_preferences_required)" --> DietaryScreen[ج. شاشة التفضيلات الغذائية]
    DietaryScreen --> SaveDietary[PUT /customers/me/food-preferences/dietary]
    SaveDietary --> ClickTierButton
    
    CheckGate -- "200 OK (المتطلبات مكتملة)" --> ShowTiers[4. فتح شاشة التصنيفات Tiers]
    ShowTiers --> SummaryAndPay[5. ملخص الاشتراك والدفع]
```

---

## 3. بوابات الخطأ والتوجيه التلقائي عند الضغط على زر «المتابعة للتصنيف»

شاشة الباقات (`GET /bundles`) تفتح بحرية (200 OK) حتى يتمكن العميل من استعراض الباقات والمدد دون حظر.

**متى يحدث الفحص؟**
يحدث الفحص **فور ضغط العميل على الزر الذي ينقله إلى شاشة التصنيفات** عبر أحد خيارين:
- **الخيار الأول (الموصى به - نقطة تحقق مخصصة للزر):**
  ```http
  GET /api/v1/customer/subscription-flow/prerequisites
  Authorization: Bearer <customer-access-token>
  ```
- **الخيار الثاني (مباشرة عند جلب التصنيفات):**
  ```http
  GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?durationDays=26&includeFriday=false
  Authorization: Bearer <customer-access-token>
  ```

إذا كان هناك أي متطلب ناقص، ترجع الاستجابة فوراً بحالة **`409 Conflict`** بالترتيب الصارم التالي:

| الترتيب | كود الخطأ `error` | الـ `missingStep` | الـ `actionEndpoint` | الشاشة المطلوبة في التطبيق |
|:-------:|-------------------|-------------------|----------------------|----------------------------|
| **1** | `checkout.address_required` | `ADDRESS` | `POST /api/v1/customers/me/addresses` | **شاشة الخريطة وإضافة عنوان التوصيل** |
| **2** | `checkout.allergies_required` | `ALLERGIES` | `GET /api/v1/customers/me/food-preferences/allergies` | **شاشة تأكيد الحساسية الغذائية** |
| **3** | `checkout.dietary_preferences_required` | `DIETARY_PREFERENCES` | `GET /api/v1/customers/me/food-preferences/dietary` | **شاشة تأكيد التفضيلات (كيتو، نباتي..)** |

---

## 4. تفاصيل الخطوات الثلاث وكيفية إكمالها

### الخطوة الأولى: عنوان التوصيل (Address)

إذا رجع الخطأ `checkout.address_required`:

1. **شاشة الخريطة:** يحدد العميل موقعه ويضغط «تأكيد الموقع».
2. **استدعاء الـ Reverse Geocode:**
   ```http
   POST /api/v1/maps/reverse-geocode
   Content-Type: application/json

   {
     "latitude": 29.339405,
     "longitude": 48.081870
   }
   ```
   يرجع الـ API بيانات المنطقة والمحافظة (`suggestedRegionId`, `suggestedGovernorateId`, `street`, إلخ).
3. **حفظ العنوان:**
   ```http
   POST /api/v1/customers/me/addresses
   Authorization: Bearer <customer-token>
   Content-Type: application/json

   {
     "label": "المنزل",
     "addressLine": "شارع 2، السالمية، الكويت",
     "regionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
     "latitude": 29.341213,
     "longitude": 48.082421,
     "isDefault": true,
     "building": "17",
     "floor": "2",
     "unit": "5"
   }
   ```
4. **تنبيه المطاعم:** ترجع الاستجابة بقائمة `servingRestaurants` التي تغطي المنطقة الجديدة، و `restaurantCoverageNotice` — والتنبيه يُعاد فقط عند وجود اشتراك نشط للعضو (`hasActiveSubscription = true`)؛ بدونه تكون القيمة `null`.

---

### الخطوة الثانية: تأكيد الحساسية (Allergies)

إذا رجع الخطأ `checkout.allergies_required`:

1. **تحميل قائمة الحساسيات المتاحة:**
   ```http
   GET /api/v1/customers/me/food-preferences/allergies?page=1&pageSize=50
   Authorization: Bearer <customer-token>
   ```
   تحتوي القائمة في أول عنصر على خيار «لا توجد حساسية» (`code: "NONE"`).
2. **حفظ / تأكيد الحساسية:**
   ```http
   PUT /api/v1/customers/me/food-preferences/allergies
   Authorization: Bearer <customer-token>
   Content-Type: application/json

   {
     "allergies": [
       { "code": "MILK", "nameAr": "الحليب", "nameEn": "Milk" }
     ]
   }
   ```
   > [!TIP]
   > إذا لم يكن لدى العميل أي حساسية، يختار «لا توجد حساسية»، ويرسل التطبيق:
   > ```json
   > { "allergies": [] }
   > ```
   > بمجرد إرسال هذا الطلب، يسجل السيرفر `AllergiesCompletedAtUtc` ويُعتبر المتطلب مكتملاً!

---

### الخطوة الثالثة: تأكيد التفضيلات الغذائية (Dietary Preferences)

إذا رجع الخطأ `checkout.dietary_preferences_required`:

1. **تحميل قائمة التفضيلات (نباتي، كيتو، خالي من الجلوتين، إلخ):**
   ```http
   GET /api/v1/customers/me/food-preferences/dietary?page=1&pageSize=50
   Authorization: Bearer <customer-token>
   ```
   أول عنصر هو «لا أتبع نظاماً معيناً» (`code: "NONE"`).
2. **حفظ / تأكيد التفضيلات:**
   ```http
   PUT /api/v1/customers/me/food-preferences/dietary
   Authorization: Bearer <customer-token>
   Content-Type: application/json

   {
     "dietaryPreferenceCodes": ["KETO"]
   }
   ```
   > [!TIP]
   > إذا كان العميل لا يتبع نظاماً معيناً، يرسل مصفوفة فارغة:
   > ```json
   > { "dietaryPreferenceCodes": [] }
   > ```
   > بمجرد إرسال هذا الطلب، يسجل السيرفر `DietaryPreferencesCompletedAtUtc` ويُعتبر المتطلب مكتملاً!

---

## 5. الاستجابة الناجحة بعد اكتمال المتطلبات (200 OK)

بعد استيفاء الشروط الثلاثة، يعيد التطبيق استدعاء:
`GET /api/v1/customer/subscription-flow/programs/{programId}/bundles`

وترجع الاستجابة بنجاح:
```json
{
  "hasDeliveryAddress": true,
  "hasAllergies": true,
  "hasDietaryPreferences": true,
  "isProfileComplete": true,
  "bundles": [
    {
      "bundleId": "c85d774f-6f96-4ad1-b258-3721867c4852",
      "nameAr": "باقة 3 وجبات + سناك",
      "restaurantCount": 4,
      "restaurants": [
        {
          "restaurantId": "6c459f3d-5197-44f2-959c-70335e23c72b",
          "logoUrl": "/api/v1/uploads/content?key=..."
        }
      ],
      "mealsPerDay": 4,
      "isMostSelected": true
    }
  ]
}
```

**ملاحظة جوهرية:** المطاعم المرجعة داخل كل باقة (`restaurants`) وعددها (`restaurantCount`) هي فقط المطاعم التي تدعم التوصيل لمنطقة العميل المحفوظة.

---

## 6. كود معالجة الزر في Flutter (onPressProceedToTiers)

هذا الكود يُستدعى **عند ضغط العميل على زر «المتابعة للتصنيف»** (Continue to Tiers):

```dart
Future<void> onProceedToTiersPressed({
  required BuildContext context,
  required String programId,
  required String bundleId,
  required int durationDays,
  bool includeFriday = false,
}) async {
  try {
    // 1. استدعاء نقطة التحقق (أو مباشرة جلب التصنيفات)
    final response = await dio.get(
      '/api/v1/customer/subscription-flow/prerequisites',
    );

    // 2. إذا كانت المتطلبات مكتملة (200 OK) -> الانتقال لشاشة التصنيف مباشرة
    if (context.mounted) {
      Navigator.pushNamed(
        context,
        '/subscription-tiers',
        arguments: {
          'programId': programId,
          'bundleId': bundleId,
          'durationDays': durationDays,
          'includeFriday': includeFriday,
        },
      );
    }
  } on DioException catch (e) {
    if (e.response?.statusCode == 409) {
      final error = e.response?.data['error'];

      switch (error) {
        case 'checkout.address_required':
          // أ. فتح شاشة الخريطة وإضافة عنوان التوصيل
          await Navigator.pushNamed(context, '/add-delivery-address');
          // بعد العودة من حفظ العنوان، نعيد الضغط آلياً
          if (context.mounted) {
            onProceedToTiersPressed(
              context: context,
              programId: programId,
              bundleId: bundleId,
              durationDays: durationDays,
              includeFriday: includeFriday,
            );
          }
          break;

        case 'checkout.allergies_required':
          // ب. فتح شاشة تأكيد الحساسية
          await Navigator.pushNamed(context, '/confirm-allergies');
          // بعد العودة، نعيد المحاولة
          if (context.mounted) {
            onProceedToTiersPressed(
              context: context,
              programId: programId,
              bundleId: bundleId,
              durationDays: durationDays,
              includeFriday: includeFriday,
            );
          }
          break;

        case 'checkout.dietary_preferences_required':
          // ج. فتح شاشة تأكيد التفضيلات
          await Navigator.pushNamed(context, '/confirm-dietary-preferences');
          // بعد العودة، نعيد المحاولة
          if (context.mounted) {
            onProceedToTiersPressed(
              context: context,
              programId: programId,
              bundleId: bundleId,
              durationDays: durationDays,
              includeFriday: includeFriday,
            );
          }
          break;

        default:
          showErrorSnackBar(context, e.response?.data['error'] ?? 'حدث خطأ');
      }
    } else {
      showErrorSnackBar(context, 'فشل التحقق من المتطلبات');
    }
  }
}
```

---

## 7. ملخص سريع لمطوري الموبايل

1. **العميل الجديد:** عند اختياره البرنامج، سيمر أول مرة بالترتيب: (عنوان خريطة) ← (تأكيد حساسية) ← (تأكيد تفضيلات) ← ثم تفتح له شاشة الباقات مباشرة.
2. **العميل الحالي:** طالما بياناته مكتملة، تفتح له شاشة الباقات مباشرة (200 OK) مع مطاعم منطقته.
3. **التأكيد لمرة واحدة:** الحساسية والتفضيلات تكفي لمرة واحدة وتُحفظ في حسابه، ويمكن تعديلها لاحقاً من البروفايل.
4. **تغيير العنوان لاحقاً:** إذا غيّر العميل عنوانه الافتراضي، تتحدث فوراً قائمة المطاعم المتاحة في الباقات والتصنيفات لتطابق منطقته الجديدة.
