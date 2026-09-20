# Screen 02.03: تسجيل سائق جديد - الخطوة 3: رفع المستندات والوثائق

## 🎯 الغرض من الشاشة
الخطوة الأخيرة في معالج تسجيل السائق؛ تتيح التقاط أو رفع صور المستندات الرسمية والوثائق المطلوبة عبر الكاميرا أو معرض الصور، ثم إرسال الطلب النهائي إلى الخادم ليبدأ دورة المراجعة المزدوجة (المطعم + الإدارة).

---

## 🎨 عناصر الواجهة والـ Layout (UI Elements)

1. **شريط التقدم العلوي (Stepper)**:
   - `✓ 1. البيانات الشخصية` -> `✓ 2. المركبة والرخصة` -> `● 3. المستندات`.
2. **بطاقات رفع المستندات الإلزامية (Required Documents)**:
   - كل بطاقة تحتوي على:
     - اسم المستند وأيقونته.
     - معاينة مصغرة (Thumbnail) عند اكتمال الرفع مع زر حذف/تعديل 🗑️.
     - مؤشر نسبة التحميل (Upload Progress Bar).
     - علامة خضراء `✓ تم الرفع بنجاح`.
   - **المستندات الإلزامية**:
     1. صورة بطاقة الهوية / الإقامة (الوجه الأمامي).
     2. صورة بطاقة الهوية / الإقامة (الوجه الخلفي).
     3. صورة رخصة القيادة (الوجه الأمامي).
     4. صورة رخصة القيادة (الوجه الخلفي).
     5. صورة استمارة / رخصة سير المركبة.
3. **بطاقات المستندات الاختيارية (Optional Documents)**:
   - الصورة الشخصية للسائق (Profile Photo).
   - صورة المركبة (Vehicle Photo).
   - صورة عقد العمل المبرم مع المطعم (Contract Document).
4. **الموافقة على الشروط والأحكام**:
   - مربع اختيار (Checkbox): *"أقر بصحة البيانات والوثائق المرفوعة وأوافق على الشروط والأحكام"*.
5. **زر الإرسال النهائي**:
   - زر **"إرسال الطلب للمراجعة"** / *"Submit Application"*.
   - ينشط بعد اكتمال رفع المستندات الإلزامية وتفعيل الموافقة على الشروط.

---

## 🔌 تكامل الباك إند (API Integration)

### 1. رفع كل ملف بمفرده فور اختياره:
- **الرابط**: `POST /api/v1/auth/staff/driver-registration/upload`
- **نوع المحتوى**: `multipart/form-data`
- **الحقل**: `file` (صورة JPG/PNG/WebP حتى 15 ميجابايت)
- **استجابة النجاح (200 OK)**:
  ```json
  {
    "storageKey": "https://ik.imagekit.io/f7h9cj23x/uploads/drivers/driver-registration/doc_xyz.png",
    "readUrl": "https://ik.imagekit.io/f7h9cj23x/uploads/drivers/driver-registration/doc_xyz.png",
    "fileName": "doc.png",
    "contentType": "image/png",
    "sizeBytes": 1048576
  }
  ```

### 2. إرسال طلب التسجيل المكتمل:
- **الرابط**: `POST /api/v1/auth/staff/driver-registration`
- **جسم الطلب (Request Body)**:
  ```json
  {
    "restaurantId": "0a40e4ff-72c7-4754-94e3-50b5f505b730",
    "fullNameAr": "أحمد محمد الشمري",
    "fullNameEn": "Ahmed Mohamed Al-Shammari",
    "phone": "+966501234567",
    "email": null, // البريد اختياري، يُقبل null
    "nationalId": "1098765432",
    "nationalIdExpiry": "2029-01-01T00:00:00Z",
    "dateOfBirth": "1994-05-20T00:00:00Z",
    "nationality": "Saudi",
    "vehicleType": "Car",
    "vehicleModel": "Toyota Camry",
    "vehiclePlate": "ABC1234",
    "vehicleYear": 2023,
    "vehicleColor": "Silver",
    "isVehicleOwned": true,
    "licenseNumber": "LIC987654",
    "licenseExpiry": "2029-06-01T00:00:00Z",
    "vehicleLicenseExpiry": "2028-12-01T00:00:00Z",
    "contractExpiry": null,
    "nationalIdFrontStorageKey": "https://ik.imagekit.io/.../nid_front.png",
    "nationalIdBackStorageKey": "https://ik.imagekit.io/.../nid_back.png",
    "drivingLicenseFrontStorageKey": "https://ik.imagekit.io/.../lic_front.png",
    "drivingLicenseBackStorageKey": "https://ik.imagekit.io/.../lic_back.png",
    "vehicleRegistrationStorageKey": "https://ik.imagekit.io/.../veh_reg.png",
    "profileImageStorageKey": null,
    "vehiclePhotoStorageKey": null,
    "contractStorageKey": null
  }
  ```

### استجابة النجاح (200 OK):
```json
{
  "registrationId": "409286c5-c30f-4f95-b505-38054f0f1285",
  "restaurantId": "0a40e4ff-72c7-4754-94e3-50b5f505b730",
  "restaurantName": "بالانس بوكس",
  "phone": "+966501234567",
  "status": "Submitted",
  "message": "تم إرسال طلب تسجيل السائق بنجاح وهو قيد المراجعة والاعتماد."
}
```

---

## 🔄 السلوك والتنقل (Navigation)
- فور نجاح الإرسال، ينتقل التطبيق مباشرة إلى [الشاشة 03.01: حالة الطلب - قيد المراجعة](screen-03.01-status-under-review.md).
