# Screen 01.01: اختيار الدور (Role Selection)

## 🎯 الغرض من الشاشة
الشاشة الافتتاحية للمصادقة في التطبيق، تتيح للمستخدم تحديد صفته التشغيلية (**سائق** أو **مسؤول توصيل**) قبل الشروع في إدخال بيانات الدخول، لضمان تحميل سياق الصلاحيات المناسب وعدم حدوث أي تضارب.

---

## 🎨 عناصر الواجهة والـ Layout (UI Elements)

1. **الهيدر العلوي (Header)**:
   - شعار MealMate الرسمي في المنتصف.
   - زر تبديل اللغة (العربية / English) أعلى اليمين/اليسار.
2. **الرسائل الترحيبية**:
   - العنوان الرئيسي (H1): **"اختر دورك للمتابعة"** / *"Choose your role to continue"*.
   - العنوان الفرعي: **"يرجى تحديد دورك الوظيفي للوصول إلى مهامك اليومية"** / *"Please select your job role to access your daily tasks"*.
3. **بطاقتي الاختيار (Selection Cards)**:
   - **بطاقة السائق (Driver Card)**:
     - أيقونة توجيه / دراجة نارية أو سيارة.
     - العنوان: **"سائق"** / *"Driver"*.
     - الوصف: **"توصيل الطلبات، تحديث الحالات، ومتابعة المسارات"** / *"Deliver orders, update statuses, and follow routes"*.
   - **بطاقة مسؤول التوصيل (Delivery Manager Card)**:
     - أيقونة إدارة / لوحة تحكم ومخطط رحلات.
     - العنوان: **"مسؤول توصيل"** / *"Delivery Manager"*.
     - الوصف: **"إدارة الرحلات، تعيين السائقين، ومتابعة الحركة"** / *"Manage trips, assign drivers, and monitor dispatch"*.
4. **زر المتابعة (Action Button)**:
   - زر **"متابعة"** / *"Continue"* (معطل حتى يتم تحديد أحد الخيارين).

---

## 🔄 السلوك والتفاعل (User Flow)

1. يقوم المستخدم بالضغط على إحدى البطاقتين؛ تصبح البطاقة نشطة بإطار ملون (Brand Primary Color) وظل مميز.
2. عند النقر على زر **"متابعة"**:
   - يتم تخزين الدور المحدد محلياً في حالة التطبيق (`Driver` أو `DeliveryManager`).
   - ينتقل التطبيق فوراً إلى [الشاشة 01.02: إدخال رقم الهاتف](screen-01.02-enter-phone.md) مع تمرير قيمة `role`.

---

## 🌐 مفاتيح الترجمة (Localization Keys)

| المفتاح | العربية (AR) | الإنجليزية (EN) |
|---|---|---|
| `auth.role_selection_title` | اختر دورك للمتابعة | Choose your role to continue |
| `auth.role_selection_subtitle` | يرجى تحديد دورك الوظيفي للوصول إلى مهامك اليومية | Please select your job role to access your daily tasks |
| `roles.driver` | سائق | Driver |
| `roles.driver_desc` | توصيل الطلبات، تحديث الحالات، ومتابعة المسارات | Deliver orders, update statuses, and follow routes |
| `roles.delivery_manager` | مسؤول توصيل | Delivery Manager |
| `roles.delivery_manager_desc` | إدارة الرحلات، تعيين السائقين، ومتابعة الحركة | Manage trips, assign drivers, and monitor dispatch |
| `common.continue` | متابعة | Continue |
