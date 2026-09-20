# Screen 02.02: تسجيل سائق جديد - الخطوة 2: بيانات المركبة والرخصة والتواريخ

## 🎯 الغرض من الشاشة
الخطوة الثانية في معالج تسجيل السائق (Stepper Step 2: **بيانات المركبة**)؛ مخصصة لإدخال بيانات المركبة المستخدمة في التوصيل وتفاصيل رخصة القيادة، بالإضافة إلى تواريخ الصلاحية والانتهاء لضمان مطابقة الشروط النظامية ومطابقة تصميم الواجهة تماماً.

---

## 🎨 عناصر الواجهة والـ Layout (UI Elements)

1. **شريط التقدم العلوي (Stepper)**:
   - `✓ 1. البيانات الشخصية` -> `● 2. بيانات المركبة` -> `○ 3. المستندات` -> `○ 4. مراجعة الطلب`.

2. **نوع المركبة (Vehicle Type)**:
   - قائمة اختيار مع أيقونة المركبة:
     - 🚗 سيارة (`Car`)
     - 🏍️ دراجة نارية (`Motorcycle`)
     - 🚐 فان / شاحنة صغيرة (`Van`)
     - 🚲 دراجة هوائية (`Bicycle`)
   - يتم جلب الخيارات ديناميكياً من الـ Endpoint:
     `GET /api/v1/auth/staff/driver-registration/vehicle-types`

3. **الشركة / الموديل (Make & Model)**:
   - حقل إدخال ذكي مع بحث وإكمال تلقائي (Search Autocomplete / Typeahead Dropdown).
   - المستخدم يكتب اسم الموديل أو الماركة (مثال: `تويوتا` أو `كامري` أو `Toyota` أو `Sunny` أو `هيونداي`).
   - يتم استدعاء Endpoint البحث مع تأخير (Debounce 300ms):
     `GET /api/v1/auth/staff/driver-registration/vehicle-models?search={term}&vehicleType={selectedType}`
   - يتم إرسال القيمة المختارة في حقل `vehicleModel` (مثال: `"Toyota Camry"`).

4. **سنة الصنع (Manufacturing Year)**:
   - منتقي السنة (Year Picker / Number) مع أيقونة التقويم (مثال: `2023`).

5. **رقم اللوحة (Plate Number)**:
   - شارة دولة الترخيص (علم الكويت 🇰🇼 - الكويت) + حقل إدخال رقم اللوحة (مثال: `KW-55102`).

6. **لون المركبة (Vehicle Color - HEX)**:
   - دوائر الألوان الدائرية المباشرة المطابقة للتصميم:
     - 🔴 أحمر: `#D32F2F`
     - 🔵 أزرق: `#1976D2`
     - ⚪ فضي / رمادي: `#9E9E9E`
     - ⚫ أسود: `#000000`
     - 🟣 بنفسجي: `#5E35B1`
     - ⚪ أبيض: `#FFFFFF` (مع إطار رمادي فاتح)
     - ➕ خيار **"أخرى"**: يفتح منتقي ألوان (Color Picker) لإدخال أي كود HEX مخصص.
   - يتم جلب الألوان ديناميكياً من:
     `GET /api/v1/auth/staff/driver-registration/vehicle-colors`
   - يتم إرسال كود الـ HEX مباشرة في الحقل `vehicleColor` (مثال: `"#5E35B1"` أو `"#D32F2F"`).

7. **المركبة ملكك؟ (Is Vehicle Owned?)**:
   - أزرار تبديل نعم / لا (Yes / No Toggle):
     - **نعم (Yes)**: `true` (محددة افتراضياً مع علامة الصح البنفسجية كما في التصميم).
     - **لا (No)**: `false`.

8. **بيانات رخصة القيادة والتواريخ (Driving License & Dates)**:
   - **رقم رخصة القيادة (License Number)**: نص إلزامي (مثال: `DL-551020`).
   - **تاريخ انتهاء رخصة القيادة (License Expiry)**: إلزامي (تاريخ مستقبلي).
   - **تاريخ انتهاء استمارة المركبة (Vehicle License Expiry)**: إلزامي (تاريخ مستقبلي).
   - **تاريخ انتهاء عقد العمل (Contract Expiry)**: اختياري.

9. **أزرار التنقل السفلية**:
   - زر **"متابعة"** (Continue / Next): بنفسجي عريض بكامل العرض للانتقال للخطوة 3 (المستندات).
   - سهم **"السابق"** (Back): للعودة للخطوة 1 وتعديل البيانات السابقة.

---

## 🌐 تفاصيل الـ Endpoints المتاحة للموبايل

### 1. جلب أنواع المركبات (Vehicle Types)
- **المسار**: `GET /api/v1/auth/staff/driver-registration/vehicle-types`
  *(يتوفر أيضاً مسار عام: `GET /api/v1/catalog/vehicles/types` أو `GET /api/v1/auth/vehicle-types`)*
- **الوصول**: عام (`AllowAnonymous`).
- **شكل الاستجابة (JSON Response)**:
```json
[
  {
    "code": "Car",
    "nameAr": "سيارة",
    "nameEn": "Car",
    "iconKey": "car"
  },
  {
    "code": "Motorcycle",
    "nameAr": "دراجة نارية",
    "nameEn": "Motorcycle",
    "iconKey": "motorcycle"
  },
  {
    "code": "Van",
    "nameAr": "فان / شاحنة صغيرة",
    "nameEn": "Van",
    "iconKey": "van"
  },
  {
    "code": "Bicycle",
    "nameAr": "دراجة هوائية",
    "nameEn": "Bicycle",
    "iconKey": "bicycle"
  }
]
```

---

### 2. جلب ألوان المركبات مع كود HEX (Vehicle Colors)
- **المسار**: `GET /api/v1/auth/staff/driver-registration/vehicle-colors`
  *(يتوفر أيضاً مسار عام: `GET /api/v1/catalog/vehicles/colors` أو `GET /api/v1/auth/vehicle-colors`)*
- **الوصول**: عام (`AllowAnonymous`).
- **شكل الاستجابة (JSON Response)**:
```json
[
  {
    "hex": "#D32F2F",
    "nameAr": "أحمر",
    "nameEn": "Red",
    "isDefault": true,
    "displayOrder": 1
  },
  {
    "hex": "#1976D2",
    "nameAr": "أزرق",
    "nameEn": "Blue",
    "isDefault": true,
    "displayOrder": 2
  },
  {
    "hex": "#9E9E9E",
    "nameAr": "فضي / رمادي",
    "nameEn": "Silver / Grey",
    "isDefault": true,
    "displayOrder": 3
  },
  {
    "hex": "#000000",
    "nameAr": "أسود",
    "nameEn": "Black",
    "isDefault": true,
    "displayOrder": 4
  },
  {
    "hex": "#5E35B1",
    "nameAr": "بنفسجي",
    "nameEn": "Purple",
    "isDefault": true,
    "displayOrder": 5
  },
  {
    "hex": "#FFFFFF",
    "nameAr": "أبيض",
    "nameEn": "White",
    "isDefault": true,
    "displayOrder": 6
  },
  {
    "hex": "#FBC02D",
    "nameAr": "أصفر",
    "nameEn": "Yellow",
    "isDefault": false,
    "displayOrder": 7
  },
  {
    "hex": "#FB8C00",
    "nameAr": "برتقالي",
    "nameEn": "Orange",
    "isDefault": false,
    "displayOrder": 8
  },
  {
    "hex": "#43A047",
    "nameAr": "أخضر",
    "nameEn": "Green",
    "isDefault": false,
    "displayOrder": 9
  },
  {
    "hex": "#6D4C41",
    "nameAr": "بني",
    "nameEn": "Brown",
    "isDefault": false,
    "displayOrder": 10
  },
  {
    "hex": "#1A237E",
    "nameAr": "كحلي",
    "nameEn": "Navy Blue",
    "isDefault": false,
    "displayOrder": 11
  },
  {
    "hex": "#D4AF37",
    "nameAr": "ذهبي",
    "nameEn": "Gold",
    "isDefault": false,
    "displayOrder": 12
  }
]
```

> **💡 ملاحظة لمطور الموبايل**: 
> - العناصر التي تحتوي على `"isDefault": true` هي الألوان الـ 6 الظاهرة في التصميم مباشرة.
> - عند اختيار دائرة لون، يتم حفظ قيمة `hex` وإرسالها في حقل `vehicleColor` كـ `"#5E35B1"`.
> - إذا اختار المستخدم "أخرى"، يظهر منتقي ألوان ويتم حفظ كود الـ HEX المختار أيضاً بنفس الطريقة.

---

### 3. البحث في الشركات والموديلات (Search Vehicle Models)
- **المسار**: `GET /api/v1/auth/staff/driver-registration/vehicle-models`
  *(يتوفر أيضاً مسار عام: `GET /api/v1/catalog/vehicles/models` أو `GET /api/v1/auth/vehicle-models`)*
- **الوصول**: عام (`AllowAnonymous`).
- **معاملات الاستعلام (Query Parameters)**:
  - `search`: كلمة البحث (عربي أو إنجليزي، مثال: `كامري`, `camry`, `تويوتا`, `toyota`, `sunny`, `صني`).
  - `vehicleType`: نوع المركبة لفلترة الموديلات المناسبة فقط (مثال: `Car` أو `Motorcycle` أو `Van` أو `Bicycle`).
  - `limit`: الحد الأقصى للنتائج (افتراضياً `40`).

#### مثال طلب البحث:
`GET /api/v1/auth/staff/driver-registration/vehicle-models?search=camry&vehicleType=Car`

#### شكل الاستجابة (JSON Response):
```json
[
  {
    "value": "Toyota Camry",
    "makeCode": "Toyota",
    "makeNameAr": "تويوتا",
    "makeNameEn": "Toyota",
    "modelCode": "Camry",
    "modelNameAr": "كامري",
    "modelNameEn": "Camry",
    "fullNameAr": "تويوتا كامري",
    "fullNameEn": "Toyota Camry",
    "vehicleType": "Car"
  }
]
```

---

### 4. جلب الشركات والموديلات كشجرة هرمية (Vehicle Makes & Nested Models)
- **المسار**: `GET /api/v1/auth/staff/driver-registration/vehicle-makes?vehicleType=Car`
- **الاستخدام**: في حال رغبة تطبيق الموبايل بعرض منتقي بعجلتين (Wheel Picker) للماركة أولاً ثم الموديل.
- **شكل الاستجابة (JSON Response)**:
```json
[
  {
    "code": "Toyota",
    "nameAr": "تويوتا",
    "nameEn": "Toyota",
    "vehicleTypes": ["Car", "Van"],
    "models": [
      {
        "code": "Camry",
        "nameAr": "كامري",
        "nameEn": "Camry",
        "fullNameAr": "تويوتا كامري",
        "fullNameEn": "Toyota Camry",
        "vehicleType": "Car"
      },
      {
        "code": "Corolla",
        "nameAr": "كورولا",
        "nameEn": "Corolla",
        "fullNameAr": "تويوتا كورولا",
        "fullNameEn": "Toyota Corolla",
        "vehicleType": "Car"
      }
    ]
  }
]
```

---

## 📦 نموذج الـ Payload المُرسل للباك إند (عند إنهاء التسجيل)
في الخطوة النهائية، يتم إرسال بيانات الخطوة 2 ضمن كائن التسجيل:
```json
{
  "vehicleType": "Car",
  "vehicleModel": "Toyota Camry",
  "vehiclePlate": "KW-55102",
  "vehicleYear": 2022,
  "vehicleColor": "#5E35B1",
  "isVehicleOwned": true,
  "licenseNumber": "DL-551020",
  "licenseExpiry": "2030-05-01T00:00:00Z",
  "vehicleLicenseExpiry": "2028-06-15T00:00:00Z",
  "contractExpiry": null
}
```

---

## 🔄 التحقق المحلي وقواعد الإدخال (Client Validations)
- **نوع المركبة**: إلزامي (اختيار من القائمة).
- **الموديل**: إلزامي (إما باختيار نتيجة من البحث أو كتابة الاسم).
- **سنة الصنع**: سنة صالحة (مثال من 1990 إلى السنة الحالية + 1).
- **لون المركبة**: كود الـ HEX المختار (مثال `#5E35B1`).
- **رقم اللوحة ورقم الرخصة**: حقول إلزامية.
- **تواريخ الصلاحية**: يجب أن تكون تواريخ مستقبلية أكبر من تاريخ اليوم.
