# Screen 01.05: تسجيل الدخول بكلمة المرور (Login with Password)

## 🎯 الغرض من الشاشة
تظهر للمستخدمين المعتمدين والمفعلين مسبقاً (سائق أو مسؤول توصيل) الذين سبق لهم تعيين كلمة المرور؛ حيث يُعرض رقم هاتفهم المحقق مع حقل إدخال كلمة المرور لتسجيل الدخول السريع.

---

## 🎨 عناصر الواجهة والـ Layout (UI Elements)

1. **شريط التنقل العلوي**:
   - زر الرجوع (Back) لتغيير رقم الهاتف أو تبديل الدور.
2. **بيانات الحساب**:
   - الاسم المعروض والصورة الرمزية (إن وجدت).
   - عرض رقم الهاتف: `+966 50 123 4567`.
   - اسم المطعم التابع له (مثال: `مطعم بالانس بوكس`).
3. **حقل كلمة المرور (Password Input)**:
   - حقل إدخال مشفر مع زر إظهار/إخفاء (Eye toggle).
4. **رابط نسيت كلمة المرور**:
   - نص قابل للنقر: **"نسيت كلمة المرور؟"** / *"Forgot Password?"*.
   - ينقل المستخدم إلى [الشاشة 01.06: نسيت كلمة المرور](screen-01.06-forgot-password-phone.md).
5. **زر الدخول (Sign In Button)**:
   - زر **"تسجيل الدخول"** / *"Sign In"*.
   - إمكانية تفعيل تسجيل الدخول بالبصمة / الوجه (Biometrics) إذا كانت مفعلة بالجهاز.

---

## 🔌 تكامل الباك إند (API Integration)

- **الرابط**: `POST /api/v1/auth/staff/login`
- **التوثيق (Auth)**: مفتوح (`Anonymous`)
- **جسم الطلب (Request Body)**:
  ```json
  {
    "phone": "+966501234567",
    "role": "Driver", // أو "DeliveryManager"
    "password": "Driver@Password2026!"
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

### الأخطاء المحتملة:
- **401 Unauthorized** (`auth.invalid_credentials`): كلمة المرور غير صحيحة.
- **403 Forbidden** (`auth.account_suspended`): الحساب موقوف، يرجى مراجعة إدارة المطعم.
