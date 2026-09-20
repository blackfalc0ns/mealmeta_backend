# 🧪 بيانات تجريبية جاهزة لإنشاء واختبار حساب السائق (Driver Registration Test Data)

هذا الملف يحتوي على بيانات واقعية وصحيحة ومطابقة بنسبة 100% لجميع شروط وقواعد التحقق (Validation Rules) في النظام، وجاهزة للاستخدام الفوري في تجربة تسجيل السائق من تطبيق الموبايل أو Postman أو cURL.

---

## 🏬 1. قائمة بمعرفات المطاعم النشطة الحقيقية (Active Restaurant IDs)
*مأخوذة مباشرة من قاعدة بيانات السيرفر الحي (`http://maelmate.runasp.net`):*

| اسم المطعم (عربي) | Restaurant Name (EN) | `restaurantId` (GUID) | هاتف المطعم |
|---|---|---|---|
| **أورا ميلز** | Aura Meals | `898259c1-4064-434e-ad20-be885987d8cb` | `+96550001010` |
| **إليفن سايكل** | Eleven Cycle | `1e77bd46-4473-4f22-8ffa-0733d4baf79f` | `+96550001021` |
| **الرقعي بليت** | Riggae Plate | `ce3571dc-0eed-414d-81df-15fd2260f177` | `+96550001041` |
| **العدان ديلي** | Adan Daily | `27b6bba9-36ec-4cb9-a1ee-93e2d90b0328` | `+96550001040` |
| **المنقف لايت** | Mangaf Lite | `35e263b5-112c-4483-bb9b-4b3a00dbf869` | `+96550001039` |
| **النعيم بوكس** | Naeem Box | `44ecf569-394f-4810-9f0e-85aa64c50405` | `+96550001042` |

---

## 📋 2. الحالات التجريبية الكاملة (Complete Test Scenarios)

### 🔹 الحالة الأولى (الموصى بها): سائق كويتي - سيارة خاصة (بدون بريد إلكتروني)
*تناسب تجربة التسجيل بدون إيميل إلزامي.*

- **الرابط**: `POST http://maelmate.runasp.net/api/v1/auth/staff/driver-registration`
- **الترويسات (Headers)**:
  ```http
  Content-Type: application/json
  Accept-Language: ar
  ```
- **جسم الطلب (Request Body - JSON)**:
```json
{
  "restaurantId": "898259c1-4064-434e-ad20-be885987d8cb",
  "fullNameAr": "أحمد محمد الشمري",
  "fullNameEn": "Ahmed Mohamed Al-Shammari",
  "phone": "+96551234001",
  "email": null,
  "nationalId": "296041501234",
  "nationalIdExpiry": "2029-01-01T00:00:00Z",
  "dateOfBirth": "1996-04-15T00:00:00Z",
  "nationality": "كويتي",
  "vehicleType": "Car",
  "vehicleModel": "Toyota Camry 2022",
  "vehiclePlate": "KW-55102",
  "vehicleYear": 2022,
  "vehicleColor": "أبيض",
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

### 🔹 الحالة الثانية: سائق وافد (مصري) - دراجة نارية (مع بريد إلكتروني)
*تناسب تجربة مركبة دراجة نارية وسائق وافد.*

- **الرابط**: `POST http://maelmate.runasp.net/api/v1/auth/staff/driver-registration`
- **جسم الطلب (Request Body - JSON)**:
```json
{
  "restaurantId": "1e77bd46-4473-4f22-8ffa-0733d4baf79f",
  "fullNameAr": "محمود حسن السيد",
  "fullNameEn": "Mahmoud Hassan El-Sayed",
  "phone": "+96561234002",
  "email": "mahmoud.driver@example.com",
  "nationalId": "294021002345",
  "nationalIdExpiry": "2028-08-30T00:00:00Z",
  "dateOfBirth": "1994-02-10T00:00:00Z",
  "nationality": "مصري",
  "vehicleType": "Motorcycle",
  "vehicleModel": "Honda CB150",
  "vehiclePlate": "KW-M-3341",
  "vehicleYear": 2023,
  "vehicleColor": "أسود",
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

### 🔹 الحالة الثالثة: سائق سعودي - سيارة فان (Van)
*تناسب تجربة أرقام الهواتف السعودية (`+966`).*

- **الرابط**: `POST http://maelmate.runasp.net/api/v1/auth/staff/driver-registration`
- **جسم الطلب (Request Body - JSON)**:
```json
{
  "restaurantId": "ce3571dc-0eed-414d-81df-15fd2260f177",
  "fullNameAr": "عبدالعزيز خالد الدوسري",
  "fullNameEn": "Abdulaziz Khaled Al-Dossary",
  "phone": "+96651234003",
  "email": null,
  "nationalId": "1098765432",
  "nationalIdExpiry": "2029-10-15T00:00:00Z",
  "dateOfBirth": "1998-07-22T00:00:00Z",
  "nationality": "سعودي",
  "vehicleType": "Van",
  "vehicleModel": "Hyundai H1",
  "vehiclePlate": "SA-9901",
  "vehicleYear": 2021,
  "vehicleColor": "فضي",
  "isVehicleOwned": true,
  "licenseNumber": "DL-776543",
  "licenseExpiry": "2029-09-10T00:00:00Z",
  "vehicleLicenseExpiry": "2028-03-25T00:00:00Z",
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

## 📤 3. رفع ملف حقيقي عبر الـ Upload API
إذا أردت رفع صورة حقيقية من التطبيق أو Postman للحصول على `storageKey` بدلاً من القيم الافتراضية:

- **الرابط**: `POST http://maelmate.runasp.net/api/v1/auth/staff/driver-registration/upload`
- **نوع الطلب**: `multipart/form-data`
- **الحقل**: `file` (ملف صورة JPG أو PNG أو PDF لا يتجاوز 15MB).
- **الاستجابة المستلمة**:
  ```json
  {
    "storageKey": "uploads/drivers/driver-registration/xxxx-yyyy.jpg",
    "readUrl": "/api/v1/uploads/content?key=uploads/drivers/...",
    "fileName": "id-card.jpg",
    "contentType": "image/jpeg",
    "sizeBytes": 1048576,
    "message": "تم رفع الملف بنجاح."
  }
  ```
- خذ قيمة `storageKey` وضعها في الحقل المقابل (مثلاً `nationalIdFrontStorageKey`).

---

## 🔍 4. تجربة فحص حالة الطلب بعد التسجيل (Screen 03.01)
بعد إتمام التسجيل بنجاح، يمكنك تجربة الاستعلام عن حالة السائق بأحد طريقتين:

### أ. عبر رابط الاستعلام المباشر عن حالة التسجيل:
- **الرابط**: `GET http://maelmate.runasp.net/api/v1/auth/staff/driver-registration/status?phone=%2B96551234001`
*(لاحظ ترميز علامة `+` بـ `%2B` في الـ URL Query)*.
- **الاستجابة المتوقعة (200 OK)**:
  ```json
  {
    "registrationId": "xxxx-xxxx-xxxx",
    "phone": "+96551234001",
    "fullNameAr": "أحمد محمد الشمري",
    "restaurantNameAr": "أورا ميلز",
    "state": "Stage1UnderReview",
    "title": "حسابك قيد المراجعة",
    "subtitle": "طلب التحاقك قيد المراجعة حالياً من قبل المطعم والإدارة.",
    "notice": "سنقوم بإشعارك عبر إشعار ورسالة نصية فور اكتمال المراجعة."
  }
  ```

### ب. عبر شاشة فحص الهاتف الذكي (Screen 01.02 Phone Lookup):
- **الرابط**: `POST http://maelmate.runasp.net/api/v1/auth/staff/lookup-phone`
- **جسم الطلب**:
  ```json
  {
    "phone": "+96551234001",
    "role": "Driver"
  }
  ```
- **الاستجابة**: سيعيد الخادم `exists: true` و `isFirstTimeSetup: false` وكائن `applicationStatus` ليوجه السائق فوراً لشاشة "طلبك قيد المراجعة"!

---

## 💻 5. أمر cURL جاهز للتجربة السريعة من الطرفية (Terminal):

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
    "nationalId": "296041501234",
    "nationalIdExpiry": "2029-01-01T00:00:00Z",
    "dateOfBirth": "1996-04-15T00:00:00Z",
    "nationality": "كويتي",
    "vehicleType": "Car",
    "vehicleModel": "Toyota Camry 2022",
    "vehiclePlate": "KW-55102",
    "vehicleYear": 2022,
    "vehicleColor": "أبيض",
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
