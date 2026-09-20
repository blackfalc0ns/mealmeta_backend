# Screen 03.04: حالة الطلب - تم قبول حسابك! (Account Approved)

## 🎯 الغرض من الشاشة
المرحلة الرابعة والنهائية (`Stage 4`) من شاشات حالة السائق؛ تظهر بعد اكتمال **دورة الموافقة المزدوجة بنجاح** (موافقة المطعم التابع له تلتها الموافقة النهائية من الإدارة العامة). تحتفي بالسائق وتوجهه للبدء بالعمل وتعيين كلمة المرور.

---

## 🎨 عناصر الواجهة والـ Layout (مطابقة للموك آب 03.04)

1. **الهيدر العلوي**:
   - شعار MealMate في المنتصف.
   - شارة علوية بلون أخضر زمردي ناعم: **"قبول الحساب"** / *"Account Approved"*.
2. **الرسم التوضيحي المركزي (Illustration)**:
   - لوح مهام (Clipboard) مع علامة صح بيضاء داخل دائرة بنفسجية/خضراء متوهجة `✔` وأوراق زهرية احتفالية تعبر عن النجاح والتفعيل.
3. **العناوين والرسائل الاحتفالية**:
   - **العنوان الرئيسي (Title)**: **"تم قبول حسابك!"** / *"Your Account Has Been Approved!"*.
   - **العنوان الفرعي (Subtitle)**: **"تم تفعيل حسابك بنجاح. يمكنك الآن البدء في العمل."** / *"Your account has been successfully activated. You can now start working."*.
4. **الأزرار الرئيسية (Action Buttons)**:
   - **زر الإجراء الرئيسي (Primary Purple Button)**:
     - نص الزر: **"إبدأ العمل"** / *"Start Working"*.
     - عند النقر عليه: يقوم التطبيق باستدعاء فحص الهاتف لتوليد رمز التحقق OTP ونقل السائق فوراً إلى [الشاشة 01.03: التحقق من الرمز](screen-01.03-verify-otp.md) ثم [الشاشة 01.04: تعيين كلمة المرور](screen-01.04-set-password.md).
   - **زر الإجراء الثانوي (Secondary Outlined Button)**:
     - نص الزر: **"العودة إلى تسجيل الدخول"** / *"Back to Sign In"*.

---

## 🔌 تكامل الباك إند (API Integration)

### 1. استعلام حالة القبول:
- **الرابط**: `GET /api/v1/auth/staff/driver-registration/status?phone=+966501234567`
- **استجابة الخادم (200 OK)**:
  ```json
  {
    "registrationId": "409286c5-c30f-4f95-b505-38054f0f1285",
    "status": "Approved",
    "stage": 4,
    "badge": "تم قبول الحساب",
    "title": "تم قبول حسابك!",
    "subtitle": "تم تفعيل حسابك بنجاح. يمكنك الآن البدء في العمل.",
    "restaurantApprovalStatus": "Approved",
    "adminApprovalStatus": "Approved",
    "canResubmit": false,
    "isApproved": true
  }
  ```

### 2. عند النقر على "إبدأ العمل":
- يستدعي التطبيق:
  ```http
  POST /api/v1/auth/staff/lookup-phone
  {
    "phone": "+966501234567",
    "role": "Driver"
  }
  ```
- يُرجع الخادم: `isFirstTimeSetup: true` ويُرسل رمز التحقق OTP إلى هاتف السائق.
- ينتقل التطبيق إلى [الشاشة 01.03: التحقق من رمز OTP](screen-01.03-verify-otp.md).
