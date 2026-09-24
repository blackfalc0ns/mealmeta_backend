# 🧪 الدليل الشامل والبيانات التجريبية لتسجيل ودورة حياة السائق (Driver Registration & Lifecycle Data)

هذا الملف يحتوي على بيانات واقعية وصحيحة ومطابقة بنسبة 100% لجميع شروط وقواعد التحقق (Validation Rules) في النظام، ومحدث بكافة التعديلات الأخيرة (**كلمة المرور الإلزامية التي يحددها السائق بنفسه**، **إمكانية التسجيل كسائق أسطول مستقل بدون مطعم**، و**دورة المراجعة الثنائية وحالات الشاشات**)، وجاهز للاستخدام الفوري من فريق الموبايل أو Postman أو cURL.

---

## 🔑 أهم التحديثات والقواعد الجوهرية (Crucial Updates)

1. **كلمة المرور (`password`)**:
   - **إلزامية** أثناء إنشاء الحساب في الخطوة الأولى / إرسال الطلب.
   - الحد الأدنى **8 خانات**.
   - يتم تشفيرها وحفظها مباشرة، وعند قبول السائق نهائياً (Screen 03.04) يسجل دخوله مباشرة برقم هاتفه وكلمة المرور هذه (Screen 01.05) دون الحاجة لـ OTP أو كلمة سر مؤقتة.
2. **اختيار المطعم (`restaurantId`)**:
   - **اختياري (Nullable)**:
     - إذا اختار السائق مطعماً: يرسل معرف المطعم `restaurantId` (GUID)، ويخضع الطلب للمراجعة المزدوجة (المطعم أولاً ثم الإدارة).
     - إذا كان سائقاً حراً لمنصة ميل ميت (Independent Fleet Driver): يرسل `"restaurantId": null`، وينتقل طلبه مباشرة للإدارة العامة للمراجعة والاعتماد.
3. **البريد الإلكتروني (`email`)**:
   - **اختياري تماماً (Optional)**؛ يُقبل `null` أو نص فارغ أو بريد إلكتروني بصيغة صحيحة.
4. **تطابق المستندات الإلزامية**:
   - البطاقة المدنية / الإقامة: يجب إرسال الوجه الأمامي والخلفي (`nationalIdFrontStorageKey`, `nationalIdBackStorageKey`).
   - رخصة القيادة: يجب إرسال الوجه الأمامي والخلفي (`drivingLicenseFrontStorageKey`, `drivingLicenseBackStorageKey`).
   - دفتر / استمارة المركبة: (`vehicleRegistrationStorageKey`).

---

## 🏬 1. قائمة بمعرفات المطاعم المعتمدة النشطة (Active Restaurant IDs)

*يمكن استدعاؤها عبر الـ API: `GET /api/v1/auth/staff/driver-registration/restaurants` أو استخدام أي GUID من القائمة أدناه:*

| اسم المطعم (عربي) | Restaurant Name (EN) | `restaurantId` (GUID) | هاتف المطعم |
|---|---|---|---|
| **أورا ميلز** | Aura Meals | `898259c1-4064-434e-ad20-be885987d8cb` | `+96550001010` |
| **إليفن سايكل** | Eleven Cycle | `1e77bd46-4473-4f22-8ffa-0733d4baf79f` | `+96550001021` |
| **الرقعي بليت** | Riggae Plate | `ce3571dc-0eed-414d-81df-15fd2260f177` | `+96550001041` |
| **العدان ديلي** | Adan Daily | `27b6bba9-36ec-4cb9-a1ee-93e2d90b0328` | `+96550001040` |
| **المنقف لايت** | Mangaf Lite | `35e263b5-112c-4483-bb9b-4b3a00dbf869` | `+96550001039` |
| **النعيم بوكس** | Naeem Box | `44ecf569-394f-4810-9f0e-85aa64c50405` | `+96550001042` |

---

## 📋 2. سيناريوهات التسجيل التجريبية الجاهزة (Registration Scenarios)

### 🔹 السيناريو الأول (الموصى به): سائق تابع لمطعم (بدون إيميل)
- **الرابط**: `POST /api/v1/auth/staff/driver-registration`
- **الترويسات (Headers)**:
  ```http
  Content-Type: application/json
  Accept-Language: ar
  ```
- **جسم الطلب (JSON)**:
```json
{
  "restaurantId": "898259c1-4064-434e-ad20-be885987d8cb",
  "fullNameAr": "أحمد محمد الشمري",
  "fullNameEn": "Ahmed Mohamed Al-Shammari",
  "phone": "+96551234001",
  "email": null,
  "password": "Password123!",
  "nationalId": "296041501234",
  "nationalIdExpiry": "2029-01-01T00:00:00Z",
  "dateOfBirth": "1996-04-15T00:00:00Z",
  "nationality": "كويتي",
  "vehicleType": "Car",
  "vehicleModel": "Toyota Camry 2022",
  "vehiclePlate": "KW-55102",
  "vehicleYear": 2022,
  "vehicleColor": "#5E35B1",
  "isVehicleOwned": true,
  "licenseNumber": "DL-551020",
  "licenseExpiry": "2030-05-01T00:00:00Z",
  "vehicleLicenseExpiry": "2028-06-15T00:00:00Z",
  "contractExpiry": null,
  "nationalIdFrontStorageKey": "uploads/drivers/driver-registration/sample_nid_front.jpg",
  "nationalIdBackStorageKey": "uploads/drivers/driver-registration/sample_nid_back.jpg",
  "drivingLicenseFrontStorageKey": "uploads/drivers/driver-registration/sample_license_front.jpg",
  "drivingLicenseBackStorageKey": "uploads/drivers/driver-registration/sample_license_back.jpg",
  "vehicleRegistrationStorageKey": "uploads/drivers/driver-registration/sample_vehicle_reg.jpg",
  "profileImageStorageKey": "uploads/drivers/driver-registration/sample_profile.jpg",
  "vehiclePhotoStorageKey": "uploads/drivers/driver-registration/sample_vehicle.jpg"
}
```

---

### 🔹 السيناريو الثاني: سائق أسطول مستقل بدون مطعم (`restaurantId: null`)
*مخصص للسائقين المستقلين التابعين لأسطول MealMate العام.*
- **الرابط**: `POST /api/v1/auth/staff/driver-registration`
- **جسم الطلب (JSON)**:
```json
{
  "restaurantId": null,
  "fullNameAr": "خالد سعد العتيبي",
  "fullNameEn": "Khaled Saad Al-Otaibi",
  "phone": "+96551234005",
  "email": null,
  "password": "Password123!",
  "nationalId": "295081203456",
  "nationalIdExpiry": "2029-12-31T00:00:00Z",
  "dateOfBirth": "1995-08-12T00:00:00Z",
  "nationality": "كويتي",
  "vehicleType": "Car",
  "vehicleModel": "Nissan Altima 2023",
  "vehiclePlate": "KW-77401",
  "vehicleYear": 2023,
  "vehicleColor": "White",
  "isVehicleOwned": true,
  "licenseNumber": "DL-774010",
  "licenseExpiry": "2031-01-01T00:00:00Z",
  "vehicleLicenseExpiry": "2029-05-15T00:00:00Z",
  "contractExpiry": null,
  "nationalIdFrontStorageKey": "uploads/drivers/driver-registration/sample_nid_front.jpg",
  "nationalIdBackStorageKey": "uploads/drivers/driver-registration/sample_nid_back.jpg",
  "drivingLicenseFrontStorageKey": "uploads/drivers/driver-registration/sample_license_front.jpg",
  "drivingLicenseBackStorageKey": "uploads/drivers/driver-registration/sample_license_back.jpg",
  "vehicleRegistrationStorageKey": "uploads/drivers/driver-registration/sample_vehicle_reg.jpg",
  "profileImageStorageKey": "uploads/drivers/driver-registration/sample_profile.jpg",
  "vehiclePhotoStorageKey": "uploads/drivers/driver-registration/sample_vehicle.jpg"
}
```

---

### 🔹 السيناريو الثالث: سائق وافد - دراجة نارية مع بريد إلكتروني
- **الرابط**: `POST /api/v1/auth/staff/driver-registration`
- **جسم الطلب (JSON)**:
```json
{
  "restaurantId": "1e77bd46-4473-4f22-8ffa-0733d4baf79f",
  "fullNameAr": "محمود حسن السيد",
  "fullNameEn": "Mahmoud Hassan El-Sayed",
  "phone": "+96561234002",
  "email": "mahmoud.driver@example.com",
  "password": "Password123!",
  "nationalId": "294021002345",
  "nationalIdExpiry": "2028-08-30T00:00:00Z",
  "dateOfBirth": "1994-02-10T00:00:00Z",
  "nationality": "مصري",
  "vehicleType": "Motorcycle",
  "vehicleModel": "Honda CB150",
  "vehiclePlate": "KW-M-3341",
  "vehicleYear": 2023,
  "vehicleColor": "#000000",
  "isVehicleOwned": false,
  "licenseNumber": "DL-908123",
  "licenseExpiry": "2029-11-20T00:00:00Z",
  "vehicleLicenseExpiry": "2027-12-01T00:00:00Z",
  "contractExpiry": "2026-12-31T00:00:00Z",
  "nationalIdFrontStorageKey": "uploads/drivers/driver-registration/sample_nid_front.jpg",
  "nationalIdBackStorageKey": "uploads/drivers/driver-registration/sample_nid_back.jpg",
  "drivingLicenseFrontStorageKey": "uploads/drivers/driver-registration/sample_license_front.jpg",
  "drivingLicenseBackStorageKey": "uploads/drivers/driver-registration/sample_license_back.jpg",
  "vehicleRegistrationStorageKey": "uploads/drivers/driver-registration/sample_vehicle_reg.jpg",
  "profileImageStorageKey": "uploads/drivers/driver-registration/sample_profile.jpg",
  "vehiclePhotoStorageKey": "uploads/drivers/driver-registration/sample_vehicle.jpg"
}
```

---

## 📤 3. رفع ملفات المستندات عبر Upload API
إذا أردت رفع صورة حقيقية من الكاميرا أو المعرض للحصول على `storageKey`:
- **الرابط**: `POST /api/v1/auth/staff/driver-registration/upload`
- **نوع الطلب**: `multipart/form-data`
- **الحقل**: `file` (ملف JPG أو PNG أو WebP بحد أقصى 15MB).
- **الاستجابة المستلمة (200 OK)**:
  ```json
  {
    "storageKey": "uploads/drivers/driver-registration/xxxx-yyyy.jpg",
    "readUrl": "/api/v1/uploads/content?key=uploads/drivers/driver-registration/xxxx-yyyy.jpg",
    "fileName": "id-front.jpg",
    "contentType": "image/jpeg",
    "sizeBytes": 1048576,
    "message": "تم رفع الملف بنجاح."
  }
  ```
- خذ الـ `storageKey` وضعه في الحقل المناسب في جسم طلب التسجيل.

---

## 🔍 4. فحص حالة الطلب والتنقل بين الشاشات (Screens 03.01 - 03.04)

### رابط فحص الحالة (Status API):
- **الرابط**: `GET /api/v1/auth/staff/driver-registration/status?phone=+96551234001`
*(أو تمرير `registrationId=GUID`)*.

### كائن الاستجابة النموذجي (`DriverApplicationStatusDto`):
```json
{
  "registrationId": "409286c5-c30f-4f95-b505-38054f0f1285",
  "phone": "+96551234001",
  "fullName": "أحمد محمد الشمري",
  "fullNameAr": "أحمد محمد الشمري",
  "fullNameEn": "Ahmed Mohamed Al-Shammari",
  "restaurantId": "898259c1-4064-434e-ad20-be885987d8cb",
  "restaurantName": "أورا ميلز",
  "status": "Submitted",
  "stage": 1,
  "badge": "قيد المراجعة",
  "title": "حسابك قيد المراجعة",
  "subtitle": "طلب التحاقك قيد المراجعة حالياً من قبل المطعم والإدارة.",
  "notice": "سنقوم بإشعارك عبر إشعار ورسالة نصية فور اكتمال المراجعة لتتمكن من تسجيل الدخول.",
  "rejectionReason": null,
  "changeRequestNotes": null,
  "restaurantApprovalStatus": "Submitted",
  "adminApprovalStatus": "Submitted",
  "canResubmit": false,
  "isApproved": false,
  "requestedByRole": null
}
```

### خريطة حالات وتوجيه شاشات الموبايل:

| قيمة `stage` | الشاشة المقابلة | معنى الحالة | الإجراء المطلوب من التطبيق |
|---|---|---|---|
| `stage: 1` | **Screen 03.01** | قيد المراجعة (Under Review) | عرض شاشة الانتظار مع الملاحظات والـ Subtitle الوارد من السيرفر. |
| `stage: 2` | **Screen 03.02** | مطلوب تعديل البيانات (Needs Changes) | عرض ملاحظات التعديل `changeRequestNotes`، وتفعيل زر "تعديل وإعادة الإرسال". الجهة الطالبة في `requestedByRole` ("Restaurant" أو "Admin"). |
| `stage: 3` | **Screen 03.03** | تم رفض الطلب (Rejected) | عرض سبب الرفض `rejectionReason` والجهة الرافضة `requestedByRole`. |
| `stage: 4` | **Screen 03.04** | تم قبول الحساب (Approved)! | عرض تهنئة القبول، وزر "تسجيل الدخول الآن" الذي يفتح Screen 01.05 لإدخال كلمة المرور. |

---

## 🔄 5. إعادة إرسال الطلب بعد التعديل (Screen 03.02 Resubmit)
عندما يكون الطلب في `stage: 2` (مطلوب تعديل):
- **الرابط**: `POST /api/v1/auth/staff/driver-registration/{registrationId}/resubmit`
- **جسم الطلب (JSON)** (يتم إرسال الحقول المراد تعديلها فقط):
```json
{
  "drivingLicenseFrontStorageKey": "uploads/drivers/driver-registration/new_license_front.jpg",
  "licenseExpiry": "2031-01-01T00:00:00Z",
  "vehiclePlate": "KW-55102"
}
```

---

## 🚀 6. تسجيل الدخول بعد القبول (Screen 01.05 Login)
فور انتقال الطلب إلى `stage: 4` (قبول الحساب):
- **الرابط**: `POST /api/v1/auth/staff/login`
- **جسم الطلب (JSON)**:
```json
{
  "phone": "+96551234001",
  "password": "Password123!",
  "role": "Driver"
}
```
- **استجابة النجاح (200 OK)**:
  تعيد `token` و `refreshToken` وبيانات السائق التشغيلية ليدخل السائق إلى الشاشة الرئيسية للتوصيل (Screen 05.01 / 05.02) فوراً!

---

## 💻 7. أمر cURL جاهز للتجربة المباشرة من الـ Terminal

```bash
curl -X POST "http://maelmate.runasp.net/api/v1/auth/staff/driver-registration" \
  -H "Content-Type: application/json" \
  -H "Accept-Language: ar" \
  -d '{
    "restaurantId": "898259c1-4064-434e-ad20-be885987d8cb",
    "fullNameAr": "أحمد محمد الشمري",
    "fullNameEn": "Ahmed Mohamed Al-Shammari",
    "phone": "+96551234001",
    "email": null,
    "password": "Password123!",
    "nationalId": "296041501234",
    "nationalIdExpiry": "2029-01-01T00:00:00Z",
    "dateOfBirth": "1996-04-15T00:00:00Z",
    "nationality": "كويتي",
    "vehicleType": "Car",
    "vehicleModel": "Toyota Camry 2022",
    "vehiclePlate": "KW-55102",
    "vehicleYear": 2022,
    "vehicleColor": "#5E35B1",
    "isVehicleOwned": true,
    "licenseNumber": "DL-551020",
    "licenseExpiry": "2030-05-01T00:00:00Z",
    "vehicleLicenseExpiry": "2028-06-15T00:00:00Z",
    "contractExpiry": null,
    "nationalIdFrontStorageKey": "uploads/drivers/driver-registration/sample_nid_front.jpg",
    "nationalIdBackStorageKey": "uploads/drivers/driver-registration/sample_nid_back.jpg",
    "drivingLicenseFrontStorageKey": "uploads/drivers/driver-registration/sample_license_front.jpg",
    "drivingLicenseBackStorageKey": "uploads/drivers/driver-registration/sample_license_back.jpg",
    "vehicleRegistrationStorageKey": "uploads/drivers/driver-registration/sample_vehicle_reg.jpg",
    "profileImageStorageKey": "uploads/drivers/driver-registration/sample_profile.jpg",
    "vehiclePhotoStorageKey": "uploads/drivers/driver-registration/sample_vehicle.jpg"
  }'
```
