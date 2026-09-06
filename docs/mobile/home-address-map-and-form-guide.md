# دليل الموبايل: خريطة عنوان المنزل وفورم إضافة العنوان

دليل تقني كامل وموجز لمطوري تطبيق الموبايل (Flutter) لتنفيذ شاشة **تحديد عنوان المنزل من الخريطة** وملء **بيانات الفورم تلقائياً** وحفظ العنوان في النظام.

---

## 1. مسار العمل في الموبايل (Workflow)

```text
[1. شاشة الخريطة]
  → يحدد العميل موقعه عبر (GPS / البحث / تحريك الـ Pin)
  → يضغط زر «تأكيد الموقع»

[2. استدعاء Reverse Geocode]
  → POST /api/v1/maps/reverse-geocode (بدون توكن)
  → الـ API يعيد بيانات العنوان + الـ IDs المقترحة للمحافظة والمنطقة والقطعة والشارع والجادة

[3. شاشة فورم العنوان]
  → تعبئة حقول الخريطة تلقائياً في الفورم
  → يقوم العميل بإكمال البيانات اليدوية (رقم المنزل / البناية، الدور، الشقة)

[4. حفظ العنوان]
  → POST /api/v1/customers/me/addresses (مع التوكن)
  → ترجع الاستجابة بقائمة المطاعم التي تغطي هذا العنوان + تنبيه التغطية
```

---

## 2. استدعاء Reverse Geocode (بعد تأكيد الموقع على الخريطة)

- **Endpoint:** `POST /api/v1/maps/reverse-geocode`
- **Auth:** Anonymous (لا يتطلب Bearer Token).
- **Content-Type:** `application/json`

### الطلب (Request):
```json
{
  "latitude": 29.339405795601476,
  "longitude": 48.08187089860439
}
```

### الاستجابة (Response):
```json
{
  "formattedAddress": "17-27 شارع 2 جادة 2، السالمية، الكويت",
  "latitude": 29.3412132,
  "longitude": 48.082421,
  "governorate": "حولي",
  "area": "السالمية",
  "block": "قطعة 5",
  "street": "شارع 2",
  "avenue": "جادة 2",
  "building": null,
  "suggestedGovernorateId": "ec19c873-a752-4797-a6e3-068ddd3ccc2a",
  "suggestedGovernorateNameAr": "حولي",
  "suggestedRegionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "suggestedRegionNameAr": "السالمية",
  "suggestedBlockId": "817ce4d2-7941-4c58-a0a9-8aa72bb93610",
  "suggestedBlockNameAr": "قطعة 5"
}
```

---

## 3. جدول ربط حقول الـ UI مع استجابة الخريطة

| # | حقل واجهة المستخدم (UI) | حقل الـ API | نوع الإدخال | ملاحظات |
|---|------------------------|--------------|-------------|---------|
| 1 | **المحافظة** | `suggestedGovernorateId` | Dropdown (تلقائي) | يتم اختياره تلقائياً في القائمة عبر الـ ID |
| 2 | **المنطقة** | `suggestedRegionId` | Dropdown (تلقائي) | **إلزامي** — يُبنى عليه تحديد تغطية المطاعم |
| 3 | **القطعة** | `suggestedBlockId` | Dropdown (تلقائي) | لو كانت القيمة `null` يترك للمستخدم الاختيار |
| 4 | **الشارع** | `street` | Text Field (تلقائي) | اسم ورقم الشارع المستخرج من الخريطة |
| 5 | **الجادة** (اختياري) | `avenue` | Text Field (تلقائي) | **منفصلة تماماً عن رقم البناية** |
| 6 | **رقم المنزل / البناية** | `building` | Text Field (يدوي) | **يُكتب يدوياً** بواسطة العميل (الخريطة لا تحدده بدقة) |
| 7 | **الدور** (اختياري) | `floor` | Text Field (يدوي) | يُكتب يدوياً بواسطة العميل |
| 8 | **رقم الشقة** (اختياري) | `unit` | Text Field (يدوي) | يُكتب يدوياً بواسطة العميل |

> [!IMPORTANT]
> **الجادة منفصلة عن رقم المبنى:** يقوم الـ Backend تلقائياً بفصل الجادة عن الشارع. يجب ألا يتم وضع الجادة في حقل المبنى `building`. حقول `building` و `floor` و `unit` تترك دائماً فارغة ليكتبها العميل بنفسه.

---

## 4. تطبيق Flutter: كود الربط بعد «تأكيد الموقع»

```dart
// استدعاء الـ API بعد اختيار الإحداثيات من الخريطة
final response = await dio.post(
  '/api/v1/maps/reverse-geocode',
  data: {
    'latitude': selectedLatLng.latitude,
    'longitude': selectedLatLng.longitude,
  },
);

final place = response.data;

// 1. تحديد قيم القوائم المنسدلة باستخدام الـ IDs
governorateDropdownId = place['suggestedGovernorateId'];
regionDropdownId = place['suggestedRegionId']; // إلزامي لتغطية المطاعم
blockDropdownId = place['suggestedBlockId'];

// 2. تعبئة الحقول النصية التلقائية
streetController.text = place['street'] ?? '';
avenueController.text = place['avenue'] ?? '';
addressLineController.text = place['formattedAddress'] ?? '';

// 3. مسح الحقول اليدوية ليقوم العميل بكتابتها
buildingController.clear();
floorController.clear();
unitController.clear();

// 4. حفظ الإحداثيات للاستخدام في طلب الحفظ
final finalLatitude = place['latitude'] as double;
final finalLongitude = place['longitude'] as double;
```

---

## 5. حفظ العنوان في النظام (Save Address)

- **Endpoint:** `POST /api/v1/customers/me/addresses`
- **Auth:** `Authorization: Bearer <customer-access-token>`
- **Content-Type:** `application/json`

### الطلب (Request Body):
```json
{
  "label": "المنزل",
  "addressLine": "17-27 شارع 2 جادة 2، السالمية، الكويت",
  "regionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "blockRegionId": "817ce4d2-7941-4c58-a0a9-8aa72bb93610",
  "latitude": 29.3412132,
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

#### الحقول الإلزامية للطلب:
1. `label`: مسمى العنوان (مثال: "المنزل"، "العمل").
2. `addressLine`: النص الكامل المقروء للعنوان (عادة ما يُؤخذ من `formattedAddress`).
3. `regionId`: معرّف المنطقة المأخوذ من `suggestedRegionId`.
4. `latitude` و `longitude`: إحداثيات صالحة تقع ضمن حدود دولة الكويت (Latitude: 28.5–30.2, Longitude: 46.5–48.5).

---

## 6. استجابة حفظ العنوان وتنبيه المطاعم المتاحة

عند نجاح الحفظ، ترجع الاستجابة بكافة تفاصيل العنوان مع **حقلين أساسيين** لواجهة التطبيق:

```json
{
  "id": "5f617637-25e2-4512-bdca-b5d1e2e604f3",
  "label": "المنزل",
  "addressLine": "17-27 شارع 2 جادة 2، السالمية، الكويت",
  "regionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "isDefault": true,
  "restaurantCoverageNotice": {
    "ar": "تنبيه: عند اعتماد هذا العنوان للتوصيل، قد تتغير قائمة المطاعم المتاحة لاشتراكك؛ ستظهر مطاعم تغطي منطقتك الجديدة وتختفي مطاعم لا تدعم التوصيل إليها.",
    "en": "Notice: When this address is used for delivery, available restaurants may change; restaurants serving your new area will appear, and restaurants not covering it will no longer be available."
  },
  "servingRestaurants": [
    {
      "id": "97db2bbb-b652-4aaa-8584-3414e6966792",
      "nameAr": "دايت هاوس",
      "nameEn": "Diet House",
      "logoUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Fdiet-house.webp",
      "rating": 4.8
    }
  ]
}
```

### إجراء الموبايل (Flutter UI Action):
1. **عرض رسالة التنبيه (`restaurantCoverageNotice`):**
   - اعرض BottomSheet أو Dialog يحتوي على نص التنبيه المترجم لتوضيح أن المطاعم المتاحة تتغير بناءً على تغطية المنطقة.
2. **عرض المطاعم المغطية (`servingRestaurants`):**
   - اعرض قائمة المطاعم التي تدعم التوصيل لهذا العنوان (الاسم، الشعار، والتقييم).
   - في حال كانت القائمة فارغة `[]`، اعرض رسالة توضيحية: «لا توجد مطاعم مشتركة تخدم هذه المنطقة حالياً».

---

## 7. ملخص وملاحظات عامة للمطور

1. **الأسماء باللغة العربية:** واجهة عناوين الكويت ترجع دائماً أسماء الشوارع والمناطق بالعربي حتى لو كان هيدر الطلب `Accept-Language: en`.
2. **الاعتماد على الـ IDs:** يتم ربط القوائم المنسدلة (المحافظة / المنطقة / القطعة) حصراً باستخدام الـ `suggested...Id` المرجع في الـ JSON وليس بمطابقة الاسم النصي.
3. **تحديث التغطية فوراً:** بمجرد حفظ العنوان واختياره كعنوان افتراضي، تصبح جميع شاشات فلو الاشتراك (الباقات `bundles` والتصنيفات `tiers`) مفلترة تلقائياً بالمطاعم التي تخدم هذا العنوان.
