# دليل مطوري الموبايل (Flutter): حلول التحديثات وتدفق اشتراك العائلة
# Flutter Mobile Integration Guide: Family Subscription & Food Preferences

> **تاريخ التحديث:** 16 سبتمبر 2026  
> **السيرفر الحي:** `http://maelmate.runasp.net`  
> **الفرع المنشور:** `dev`  
> **الفريق المستهدف:** مهندسو تطبيقات الموبايل (Flutter / Mobile Team)

---

## 🚀 ملخص التحديثات السريعة (TL;DR)
1. **المكونات غير المفضلة (Disliked Ingredients):** تم حل المشكلة جذرياً. يقبل السيرفر الآن كافة صيغ المسميات (`dislikedIngredients`, `ingredients`, `dislikedFoods`, `items`) وكافة صيغ المعرّفات (سواء `GUID` مجرد أو `INGREDIENT:{guid}` أو كود المكون أو اسمه بالعربي).
2. **البحث في الحساسية والمكونات والأنظمة الغذائية:** تم توفير Endpoints مخصصة ومباشرة تحت مسار العائلة تدعم `search`, `page`, `pageSize`.
3. **التقسيم (Pagination):** اختياري تماماً في جميع الـ Endpoints، والاستجابة تحتفظ دائماً بنفس الموديل (`List<T>` أو الكائن المحدد) لضمان عدم حدوث أي خطأ في الـ Type Casting في Dio.

---

## 1️⃣ الإجابة الحاسمة على استفسارات فريق الفلاتر الأربعة

### السؤال الأول: هل `GET /api/v1/customer/family-subscription-flow/programs` يحتاج Pagination؟
* **الإجابة:** **لا، غير مطلوب.**
* **السبب:** عدد برامج الاشتراك في النظام محدود وثابت (عادة ما بين 3 إلى 6 برامج مثل: كيتو، دايت صحي، رياضي، لو كارب). تحميلها دفعة واحدة أفضل لتجربة المستخدم وسرعة العرض.
* **دعم إضافي:** إذا أردت استخدام التقسيم اختيارياً، يمكنك تمرير `?page=1&pageSize=10` وسيقوم السيرفر باقتطاع النتائج مع الاحتفاظ بنفس الـ Model كمصفوفة JSON `List<CustomerProgramCardDto>`.

---

### السؤال الثاني: هل `POST /api/v1/customers/me/addresses/preview-coverage` يحتاج Pagination؟
* **الإجابة:** **الطلب يرجع كائن معاينة (Preview Object)، وقائمة المطاعم بداخله أصبحت تدعم التقسيم اختيارياً.**
* **الاستجابة:** ترجع كائناً يحتوي على `servingRestaurants: [...]`، وهو قائمة المطاعم التي تغطي منطقة العنوان.
* **كيفية الاستخدام:**
  - إذا لم تُرسل `page` و `pageSize`، ستصلك كل المطاعم التي تغطي العنوان.
  - إذا أردت تقسيم المطاعم، أرسلها في الـ Body:
    ```json
    {
      "governorate": "العاصمة",
      "area": "شرق",
      "block": "1",
      "page": 1,
      "pageSize": 10
    }
    ```

---

### السؤال الثالث: لماذا لم تكن المكونات غير المفضلة تُحفظ؟ وكيف يعمل السيرش في الحساسية والمكونات والأنظمة؟

#### أ) سبب المشكلة وحلها في الباك إند:
* **السبب السابق:** كان الموبايل يرسل خاصية باسم `dislikedIngredients` أو مصفوفة نصوص بدون البادئة `INGREDIENT:`، وكان نظام الفحص القديم يتطلب `dislikedFoods` وبادئة صارمة.
* **الحل المطبق:** السيرفر الآن مرن بنسبة 100%:
  1. يقبل أي اسم حقل في الـ JSON:
     - `"dislikedIngredients": [...]`
     - أو `"ingredients": [...]`
     - أو `"items": [...]`
     - أو `"dislikedFoods": [...]`
     - أو `"ingredientCodes": [...]`
  2. يقبل القيم سواء كانت مصفوفة نصوص أو مصفوفة كائنات:
     - `["d3b07384-113e-dec4-9eaa-6238ad5ff000"]` (GUID مجرد)
     - `["INGREDIENT:d3b07384-113e-dec4-9eaa-6238ad5ff000"]` (مع البادئة)
     - `["CHICKEN"]` أو `["R01-ING-04"]` (كود المكون من الكتالوج)
     - `["دجاج"]` (اسم المكون)
     - `[{"code": "...", "nameAr": "..."}]` (كائن كامل)

#### ب) كيفية البحث وتصفح المكونات والحساسية والأنظمة الغذائية (Search & Pagination):
تمت إضافة Endpoints مخصصة مباشرة في مسار العائلة لتسهيل العمل:

| الشاشة / المورد | Endpoint | Query Parameters المدعومة |
| :--- | :--- | :--- |
| **المكونات غير المفضلة** | `GET /api/v1/customer/family-subscription-flow/disliked-ingredients` | `?search=دجاج&page=1&pageSize=20` |
| **الحساسية** | `GET /api/v1/customer/family-subscription-flow/allergies` | `?search=حليب&page=1&pageSize=20` |
| **الأنظمة الغذائية** | `GET /api/v1/customer/family-subscription-flow/dietary` | `?search=كيتو&page=1&pageSize=20` |
| **كتالوج التفضيلات الكامل** | `GET /api/v1/customer/family-subscription-flow/food-preferences/catalog` | بدون معايير |

> **ملاحظة:** استجابة الـ GET لهذه الشاشات تحتوي على:
> - `items` (أو `ingredients` / `allergens` / `dietaryPreferences`)
> - `page`, `pageSize`, `totalCount`, `totalPages`
> - `hasNextPage`, `hasPreviousPage`
> - خيار `NONE` في البداية لتسهيل عرض حالة "أتناول كل أنواع الطعام" أو "لا توجد حساسية".

---

### السؤال الرابع: هل `GET .../programs/{pId}/bundles/{bId}/tiers/{tier}/restaurants?familyMemberId={mId}` يحتاج Pagination؟
* **الإجابة:** **اختياري، ويمكنك تمريره أو تجاهله.**
* **الاستجابة المعتمدة:** دائماً مصفوفة مطاعم `List<CustomerRestaurantBrowseCardDto>` حتى لا ينكسر كود التحويل في الموبايل.
* إذا أردت التقسيم، أضف معايير الـ Query:
  `GET .../restaurants?familyMemberId={mId}&page=1&pageSize=10`

---

## 2️⃣ نماذج الطلبات الجاهزة (Ready JSON Payloads)

### حفظ المكونات غير المفضلة لعضو في العائلة (Save Member Disliked Foods)
```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/disliked-ingredients
Authorization: Bearer <token>
Content-Type: application/json

{
  "dislikedIngredients": [
    "7fe77ed4-e152-45bf-8b7a-c21a92077d49",
    "INGREDIENT:3fa85f64-5717-4562-b3fc-2c963f66afa6"
  ]
}
```
*(أو يمكنك إرسال `"ingredients": [...]` أو `"dislikedFoods": [...]`، وستعمل جميعها بنجاح)*

---

### حفظ الحساسية لعضو في العائلة (Save Member Allergies)
```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/allergies
Authorization: Bearer <token>
Content-Type: application/json

{
  "allergies": [
    { "code": "MILK", "severity": "High" },
    { "code": "EGG", "severity": "Medium" }
  ]
}
```

---

### حفظ النظام الغذائي لعضو في العائلة (Save Member Dietary)
```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/dietary
Authorization: Bearer <token>
Content-Type: application/json

{
  "dietaryPreferenceCodes": [
    "KETO"
  ]
}
```

---

## 3️⃣ كود Dart / Dio جاهز للنسخ المباشر في تطبيق Flutter

```dart
import 'package:dio/dio.dart';

class FamilySubscriptionFlowApiService {
  final Dio _dio;

  FamilySubscriptionFlowApiService(this._dio);

  // 1. جلب البرامج (بدون Pagination أو مع Pagination اختياري)
  Future<List<Map<String, dynamic>>> getPrograms({int? page, int? pageSize}) async {
    final response = await _dio.get(
      '/api/v1/customer/family-subscription-flow/programs',
      queryParameters: {
        if (page != null) 'page': page,
        if (pageSize != null) 'pageSize': pageSize,
      },
    );
    return List<Map<String, dynamic>>.from(response.data);
  }

  // 2. البحث وتصفح المكونات غير المفضلة
  Future<Map<String, dynamic>> searchDislikedIngredients({
    String? search,
    int page = 1,
    int pageSize = 20,
  }) async {
    final response = await _dio.get(
      '/api/v1/customer/family-subscription-flow/disliked-ingredients',
      queryParameters: {
        if (search != null && search.isNotEmpty) 'search': search,
        'page': page,
        'pageSize': pageSize,
      },
    );
    return response.data as Map<String, dynamic>;
  }

  // 3. البحث وتصفح مسببات الحساسية
  Future<Map<String, dynamic>> searchAllergies({
    String? search,
    int page = 1,
    int pageSize = 20,
  }) async {
    final response = await _dio.get(
      '/api/v1/customer/family-subscription-flow/allergies',
      queryParameters: {
        if (search != null && search.isNotEmpty) 'search': search,
        'page': page,
        'pageSize': pageSize,
      },
    );
    return response.data as Map<String, dynamic>;
  }

  // 4. حفظ المكونات غير المفضلة لعضو العائلة
  Future<bool> saveMemberDislikedIngredients(
    String memberId,
    List<String> ingredientCodesOrGuids,
  ) async {
    final response = await _dio.put(
      '/api/v1/customer/family-subscription-flow/members/$memberId/disliked-ingredients',
      data: {
        'dislikedIngredients': ingredientCodesOrGuids,
      },
    );
    return response.statusCode == 200;
  }

  // 5. جلب مطاعم الفئة للعضو مع التقسيم الاختياري
  Future<List<Map<String, dynamic>>> getTierRestaurants({
    required String programId,
    required String bundleId,
    required int tier,
    required String familyMemberId,
    int? page,
    int? pageSize,
  }) async {
    final response = await _dio.get(
      '/api/v1/customer/subscription-flow/programs/$programId/bundles/$bundleId/tiers/$tier/restaurants',
      queryParameters: {
        'familyMemberId': familyMemberId,
        if (page != null) 'page': page,
        if (pageSize != null) 'pageSize': pageSize,
      },
    );
    return List<Map<String, dynamic>>.from(response.data);
  }

  // 6. فحص تغطية العنوان مع التقسيم الاختياري للمطاعم
  Future<Map<String, dynamic>> previewAddressCoverage({
    required String governorate,
    required String area,
    String? block,
    int? page,
    int? pageSize,
  }) async {
    final response = await _dio.post(
      '/api/v1/customers/me/addresses/preview-coverage',
      data: {
        'governorate': governorate,
        'area': area,
        if (block != null) 'block': block,
        if (page != null) 'page': page,
        if (pageSize != null) 'pageSize': pageSize,
      },
    );
    return response.data as Map<String, dynamic>;
  }
}
```

---

## 4️⃣ ملخص الحالات الخاصة
1. **تصفير خطة العضو (Plan Reset):** يتم بإرسال `null` في حقول الخطة إلى `PUT .../members/{memberId}/plan`، وسيعود العضو لحالة `NotStarted` ويخرج من الحسابات المالية.
2. **العضو بدون حساسية أو بدون تفضيلات غير مرغوبة:**
   - عند اختيار "أتناول كل أنواع الطعام"، أرسل مصفوفة فارغة `[]` أو `["NONE"]`.
   - عند اختيار "لا توجد حساسية"، أرسل مصفوفة فارغة `[]` أو `[{"code": "NONE"}]`.
