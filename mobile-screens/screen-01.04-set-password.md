# Screen 01.04: تعيين كلمة المرور لأول مرة (Set Password)

## 🎯 الغرض من الشاشة
شاشة مستقلة ومنفصلة عن شاشة رمز التحقق؛ تظهر للسائق أو مسؤول التوصيل عند إعداد حسابه لأول مرة لإنشاء كلمة مرور قوية وتأكيدها، وباكتمالها يتم تسجيل دخوله وإصدار توكنات الوصول (`accessToken` و `refreshToken`) لينطلق مباشرة إلى واجهة العمل الرئيسية.

---

## 🎨 عناصر الواجهة والـ Layout (UI Elements)

1. **الهيدر والرسائل**:
   - أيقونة قفل ومفتاح أمان.
   - العنوان الرئيسي: **"تعيين كلمة المرور"** / *"Set New Password"*.
   - العنوان الفرعي: **"أنشئ كلمة مرور قوية لحماية حسابك واستخدامها لتسجيل الدخول مستقبلاً"**.
2. **حقول الإدخال (Password Inputs)**:
   - **كلمة المرور الجديدة (New Password)**:
     - إدخال مشفر مع زر إظهار/إخفاء كلمة المرور (Show/Hide Password Eye Toggle).
   - **تأكيد كلمة المرور (Confirm Password)**:
     - زر إظهار/إخفاء.
3. **مؤشرات قوة كلمة المرور (Password Strength Rules)**:
   - علامات تحقق ديناميكية ملونة (تتحول للأخضر عند استيفاء الشرط):
     - [ ] 8 خانات على الأقل.
     - [ ] حرف كبير واحد على الأقل (Uppercase).
     - [ ] رقم واحد على الأقل (Digit).
     - [ ] رمز خاص واحد على الأقل (Special Character e.g. `!@#$%`).
     - [ ] تطابق كلمتي المرور.
4. **زر الحفظ والدخول**:
   - زر **"تفعيل الحساب والدخول"** / *"Set Password & Sign In"*.
   - معطل حتى تتطابق كلمتا المرور وتستوفي معايير الأمان.

---

## 🔌 تكامل الباك إند (API Integration)

- **الرابط**: `POST /api/v1/auth/staff/set-password`
- **التوثيق (Auth)**: مفتوح (`Anonymous`)
- **جسم الطلب (Request Body)**:
  ```json
  {
    "phone": "+966501234567",
    "role": "Driver", // أو "DeliveryManager"
    "verificationToken": "CfDJ8IiaSLRQVAJCtZcCGYOjl0AL8geloEPTH98WQfgbB...",
    "newPassword": "Driver@Password2026!",
    "confirmPassword": "Driver@Password2026!"
  }
  ```

### استجابة النجاح (200 OK):
```json
{
  "userId": "f6206b1f-9dbe-446e-ac07-3de3c63645bc",
  "phoneNumber": "+966501234567",
  "fullName": "محمد عبدالله",
  "userType": "Driver",
  "accountStatus": "Active",
  "restaurantId": "0a40e4ff-72c7-4754-94e3-50b5f505b730",
  "roles": ["Driver"],
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6...",
  "refreshToken": "Wnzh2bU7XTf8DhhZOizCbPe3...",
  "accessTokenExpiresAtUtc": "2026-09-20T11:00:00Z",
  "isAuthenticated": true
}
```

---

## 🔄 السلوك والتنقل (Navigation)
- فور استلام التوكنات بنجاح:
  1. حفظ `accessToken` و `refreshToken` وبيانات المستخدم في التخزين الآمن بالجهاز (`FlutterSecureStorage` / `AsyncStorage`).
  2. التوجيه إلى الشاشة الرئيسية للتطبيق:
     - إذا كان **سائق**: شاشة الرحلات والطلبات المكلفة له اليوم.
     - إذا كان **مسؤول توصيل**: شاشة لوحة تحكم الحركة وتوزيع الطلبات والرحلات.
