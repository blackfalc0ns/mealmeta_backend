# دليل الحساسية والمرفوضات الغذائية لفرد العائلة (Allergies vs. Disliked Foods)

> **خاص بفلو العائلة:** مسارات أفراد العائلة تحت `https://maelmate.runasp.net/api/v1/customer/family-subscription-flow`

---

## 1. ملخص تنفيذي وسريع (الفرق بين المفاهيم الثلاثة)

| المفهوم | المعنى | هل هو إلزامي لإتمام الاشتراك والدفع؟ | الـ Endpoint في فلو العائلة | هل له شاشة مستقلة في الفلو؟ |
|---|---|:---:|---|:---:|
| **الحساسية (Allergies)** | حساسية مهددة للصحة (مثل: الفول السوداني، الحليب، البيض) | **نعم (إلزامي)** إما باختيار الحساسيات أو كود `NONE` | `PUT .../members/{memberId}/allergies` | **نعم (شاشة 5)** |
| **الأطعمة غير المفضلة (Disliked Foods)** | مكونات لا يحب الفرد طعمها (مثل: البصل، الثوم، الفطر) | **لا (اختياري تماماً)** | `PUT .../members/{memberId}/disliked-ingredients` | **لا** (غير مطلوب في فلو الدفع) |
| **التفضيل الغذائي (Dietary)** | حمية الفرد العامة (نباتي، كيتو، حلال، إلخ) | **لا (اختياري تماماً)** | `PUT .../members/{memberId}/dietary` | **لا** (غير موجود بفلو العائلة) |

---

## 2. شاشة الحساسية (الشاشة رقم 5 في فلو العائلة) — إلزامية

### أ) القاعدة الأساسية:
- لا يمكن لأي فرد الانتقال لخطوة الدفع ما لم يتم تسجيل حالته مع الحساسية:
  - إذا كان لديه حساسية: تُرسل أكواد الحساسيات المحددة.
  - إذا لم تكن لديه أي حساسية: يُرسل كود خاص اسمه `"code": "NONE"`.
- فور حفظ الحساسية (سواء بأكواد أو بـ `NONE`)، يقوم السيرفر بتسجيل تاريخ الاكتمال `AllergiesCompletedAtUtc`، وتصبح خطوة الحساسية مكتملة وجاهزة للدفع.

---

### ب) الـ APIs لشاشة الحساسية:

#### 1. جلب كتالوج الحساسيات العامة (لعرض الشيبس في الموبايل):
```http
GET /api/v1/customers/me/food-preferences/allergies?page=1&pageSize=50
```
- يرجع قائمة الحساسيات المعتمدة في النظام (مثل `PEANUTS`, `MILK`, `EGGS`, `GLUTEN`, ...).

#### 2. حفظ حساسية فرد محدد في العائلة:
```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/allergies
Content-Type: application/json
```

**الحالة الأولى — الفرد سليم وليس لديه حساسية:**
```json
{
  "allergies": [
    { "code": "NONE" }
  ]
}
```

**الحالة الثانية — الفرد يعاني من حساسية محددة:**
```json
{
  "allergies": [
    { "code": "MILK", "severity": "High" },
    { "code": "PEANUTS", "severity": "Medium" }
  ]
}
```

- **الاستجابة الناجحة:** `200 OK` بقيمة `true`.

#### 3. الاستعلام عن حساسية الفرد المحفوظة:
```http
GET /api/v1/customer/family-subscription-flow/members/{memberId}/allergies
```
- الاستجابة:
```json
{
  "selected": [
    {
      "code": "MILK",
      "nameAr": "حليب ومشتقاته",
      "nameEn": "Milk and Dairy",
      "severity": "High"
    }
  ],
  "isCompleted": true,
  "dislikedFoods": []
}
```

---

## 3. ما قصة "الأطعمة غير المفضلة" (Disliked Ingredients)؟

### لماذا هي منفصلة؟
- في تجربة المستخدم لتطبيق الموبايل، الهدف هو **تسريع عملية الاشتراك والدفع** لعائلة قد تتكون من 4 أو 5 أفراد.
- اشتراط إدخال المكونات المرفوعة لكل فرد بالتفصيل (مثل البصل أو الفلفل أو الطماطم) يجعل عملية الشراء معقدة وطويلة.
- لذلك:
  1. **الباك إند لا يعتبر الأطعمة غير المفضلة عائقاً للدفع (Not a Gate).**
  2. لن يمنع السيرفر الدفع أبداً بسبب عدم إدخال مكونات غير مفضلة.
  3. شاشات الفلو في الموبايل المعتمدة حالياً تنتقل مباشرة من:
     `شاشة الباقة والمدة (4) ← شاشة الحساسية (5) ← شاشة العنوان (6) ← الدفع (7)`.

### هل يحتاج الموبايل لمناداة `disliked-ingredients` في فلو العائلة؟
- **لا، ليس مطلوباً.** الموبايل يكتفي تماماً بحفظ الحساسية عبر:
  `PUT /api/v1/customer/family-subscription-flow/members/{memberId}/allergies`.
- حتى لو أردت إرسال مكونات غير مفضلة اختيارياً مع الحساسية في نفس الطلب، يمكنك تمرير `dislikedFoods` اختيارياً داخل body نفس طلب الحساسية:
  ```json
  {
    "allergies": [ { "code": "NONE" } ],
    "ingredientCodes": [ "INGREDIENT:onion" ]
  }
  ```
  ولكن الموبايل **غير ملزم** إطلاقاً بطلب مسار `PUT /disliked-ingredients` كشاشة مستقلة.

---

## 4. نموذج كود الموبايل (Flutter / Dart)

```dart
// حفظ الحساسية لفرد العائلة
Future<bool> saveMemberAllergies({
  required String memberId,
  required List<String> selectedAllergenCodeList,
}) async {
  // إذا لم يختر المستخدم أي حساسية، نرسل NONE إجبارياً
  final payload = {
    "allergies": selectedAllergenCodeList.isEmpty
        ? [ { "code": "NONE" } ]
        : selectedAllergenCodeList.map((code) => { "code": code }).toList(),
  };

  final response = await dio.put(
    '/api/v1/customer/family-subscription-flow/members/$memberId/allergies',
    data: payload,
  );

  return response.statusCode == 200;
}
```

---

## 5. الأسئلة الشائعة (FAQ)

### س: هل لو لم نستدعِ مسار `disliked-ingredients` سيتوقف الدفع؟
**ج:** **لا مطلقاً.** الشرط الوحيد في خطوة الحساسية هو مناداة `PUT .../members/{memberId}/allergies` وإرسال كود `NONE` إذا لم تكن هناك حساسية.

### س: ماذا يحدث إذا نسي الموبايل إرسال حساسية أحد الأفراد؟
**ج:** عند محاولة الدفع أو جلب الملخص `GET summary`، سيرجع الباك إند:
- `canCheckout: false`
- `checkoutBlockReason: "family.allergies_required"`
- الطلب سيرجع خطأ `409 Conflict`.
- بمجرد مناداة `PUT .../allergies` بكود `NONE` أو الأكواد المختارة، يصبح الفرد `Ready` فوراً للدفع.
