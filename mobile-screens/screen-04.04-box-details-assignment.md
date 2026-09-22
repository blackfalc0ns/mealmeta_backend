# Screen 04.04: تفاصيل البوكس واختيار السائق للإسناد (Screen 10.02 - Box Details & Driver Assignment)

## 🎯 الغرض من الشاشة ونظرة عامة (Overview & Purpose)
تُمثل هذه الشاشة الخطوة المباشرة التالية للنقر على زر `[إسناد]` في شاشة طابور الطلبات (`Screen 10.01 / 04.03`). تُمكّن الشاشة مسؤول التوصيل / المرسل (`Dispatcher`) من:
1. مراجعة بطاقة البوكس المستهدف ومعرفة المنطقة، نافذة التسليم، عدد الوجبات، والأولوية.
2. الاستفادة من **أفضل اقتراح ذكي (`✨ أفضل اقتراح`)** محدد مسبقاً بنقرة زر واحدة لاختيار السائق الأنسب (الأقرب جغرافياً أو الأقل ضغطاً).
3. استعراض ومقارنة قائمة السائقين المرشحين بدقة متناهية مع عرض الحالة التشغيلية الحية (`متاح`، `مشغول`، `في التوصيل`، `راجع للمطعم`)، الوسم التشغيلي، مسافة الطريق، الحمولة الحالية، والوقت المتوقع للإنهاء (`ETA`).
4. فحص محتويات وأصناف الوجبات والباركود عبر زر `[عرض البوكس]`.
5. تأكيد الإسناد عبر زر `[تأكيد الإسناد ✈️]` مع إرسال إشعار فوري للسائق وبث تحديث لحظي للعدادات عبر SignalR والرجوع السلس لطابور الطلبات.

---

## 🎨 التحليل البصري والتخطيطي (UI/UX Breakdown & Components)

### 1. الهيدر وشريط الحالة العلوي (Header & Top Navigation)
- شريط الحالة: الوقت `9:41`، مؤشرات الشبكة، الواي فاي، والبطارية.
- زر الإشعارات / التنبيهات: مربع منحني الحواف بأيقونة جرس ونقطة تنبيه حمراء.
- العنوان الرئيسي: **إسناد البوكس #BX-1256** (غامق ومركزي).

### 2. بطاقة تفاصيل البوكس (Box Overview Card)
بطاقة بيضاء بظلال ناعمة وإطار خفيف تحتوي على:
- **كود البوكس وشارة الحالة**:
  - كود البوكس: `#BX-1256` مع أيقونة 📦.
  - شارة الحالة: `جديد` (خلفية خضراء فاتحة ونص أخضر).
- **شبكة بيانات البوكس (2 أعمدة)**:
  - **العمود الأيمن**:
    - 📍 منطقة العميل: `منطقة السالمية`
    - 🕒 الوقت المطلوب: `09:30-10:30 ص`
    - 🍴 عدد الوجبات: `8 وجبات`
  - **العمود الأيسر**:
    - 🏪 المسافة من مطعمك: `6.2 كم`
    - 🏷️ الأولوية: `عالية` (نص برتقالي جذاب)

### 3. بطاقة أفضل ترشيح ذكي (Smart Top Recommendation Card)
بطاقة بارزة بإطار بنفسجي ناعم وخلفية مميزة:
- **الشارة العلوية**: `✨ أفضل اقتراح` (خلفية بنفسجية مبهجة بنجوم ذهبية/بيضاء).
- **صورة واسم السائق**: صورة دائرية للسائق + الاسم `سالم الحربي`.
- **شارة الحالة والوسم**:
  - شارة الحالة: `متاح` (أخضر).
  - وسم التوصية: `✔️ الأقرب`.
- **المقاييس الحيوية الثلاثة**:
  - 📍 المسافة: `4.8 كم`
  - 📦 الحمولة الحالية: `4 بوكسات`
  - 🕒 الإنهاء المتوقع: `10:15 ص` (ETA)
- **زر الاختيار (Radio Button)**: مفعل افتراضياً بنمط دائري بنفسجي مميز `⦿`.

### 4. قائمة السائقين المرشحين (Candidate Drivers Selection List)
- **عنوان القسم**: `اختر سائقاً للإسناد` مع رابط تفاعلي `عرض الكل ⊞`.
- **عناصر القائمة المرتبة (1، 2، 3، 4...)**:
  كل صف يحتوي على:
  - **رقم الترتيب**: شارة مربعة بلون رمادي فاتح ورقم تسلسلي.
  - **صورة السائق واسمه**: صورة رمزية دائرية مع الاسم الكامل.
  - **شارات الحالة والوسوم**:
    - مثال 1: `متاح` (أخضر) + `الأقل ضغطاً` (أخضر فاتح) - المسافة `6.2 كم`، الحمولة `0 بوكسات`، الإنهاء `10:20 ص`.
    - مثال 2: `مشغول` (برتقالي) + `منشغل بتسليم` (برتقالي فاتح) - المسافة `6.2 كم`، الحمولة `0 بوكسات`، الإنهاء `10:20 ص`.
    - مثال 3: `في التوصيل` (أزرق) + `قريب من العميل` (أزرق فاتح) - المسافة `6.2 كم`، الحمولة `0 بوكسات`، الإنهاء `10:20 ص`.
    - مثال 4: `راجع للمطعم` (بنفسجي) + `قريب من العميل` (أزرق فاتح) - المسافة `6.2 كم`، الحمولة `0 بوكسات`، الإنهاء `10:20 ص`.
  - **زر الاختيار (Radio Button)**: غير محدد افتراضياً `○`، وعند النقر عليه يتم إلغاء تحديد الاقتراح العلوي وتحديده هو.

### 5. شريط الإجراءات السفلي الثابت (Bottom Action Buttons)
شريط عائم أو ثابت فوق شريط التنقل:
- **زر رئيسي (تأكيد الإسناد ✈️)**:
  - زر بنفسجي عريض بنص أبيض وأيقونة طائرة ورقية / إرسال.
  - يقوم بإرسال أمر الإسناد للخادم.
- **زر ثانوي (عرض البوكس 👁️)**:
  - زر بخلفية بيضاء وإطار بنفسجي أنيق وأيقونة عين.
  - يفتح نافذة سفلية (BottomSheet / Modal) تحتوي على أصناف الوجبات والباركود وبيانات العميل المشفرة.

### 6. شريط التنقل السفلي (Bottom Navigation Bar)
- `الرئيسية` 🏠
- `الطلبات` 📋 (محدد ونشط)
- `التوصيل` 🚚
- `الدعم` 🎧
- `الحساب` 👤

---

## 🔌 عقود تكامل الخادم (API Contracts)

### 1. جلب بيانات شاشة الإسناد المتكاملة (Box & Candidates)
- **الرابط**: `GET /api/v1/dispatcher/orders/{boxId}/assignment-details`
- **الترويسات الإلزامية**:
  ```http
  Accept-Language: ar  # أو en
  Authorization: Bearer <token>
  ```
- **استجابة الخادم الناجحة (200 OK)**:
```json
{
  "box": {
    "boxId": "a1111111-1111-1111-1111-111111111111",
    "boxCode": "#BX-1256",
    "zoneName": "منطقة السالمية",
    "deliveryTimeWindow": "09:30-10:30 ص",
    "mealCount": 8,
    "mealCountLabel": "8 وجبات",
    "distanceKm": 6.2,
    "distanceText": "6.2 كم",
    "priority": "HighPriority",
    "priorityBadgeText": "عالية",
    "status": "Pending",
    "statusBadgeText": "جديد"
  },
  "bestSuggestion": {
    "driverId": "33333333-3333-3333-3333-333333333333",
    "fullName": "سالم الحربي",
    "avatarUrl": "https://cdn.mealmate.app/avatars/salem-h.jpg",
    "plateNumber": "DX-438922",
    "phone": "+96551234003",
    "distanceKm": 4.8,
    "distanceText": "4.8 كم",
    "activeOrdersCount": 2,
    "currentLoadBoxes": 4,
    "currentLoadLabel": "4 بوكسات",
    "rating": 4.9,
    "driverStatus": "Available",
    "driverStatusText": "متاح",
    "statusTag": "الأقرب",
    "estimatedFinishTimeText": "10:15 ص",
    "rank": 0,
    "isRecommended": true,
    "recommendationReason": "الأقرب جغرافياً لمنطقة التسليم"
  },
  "candidates": [
    {
      "driverId": "11111111-1111-1111-1111-111111111111",
      "fullName": "أحمد إبراهيم",
      "avatarUrl": "https://cdn.mealmate.app/avatars/ahmed.jpg",
      "plateNumber": "DX-439612",
      "phone": "+96551234001",
      "distanceKm": 6.2,
      "distanceText": "6.2 كم",
      "activeOrdersCount": 0,
      "currentLoadBoxes": 0,
      "currentLoadLabel": "0 بوكسات",
      "rating": 4.8,
      "driverStatus": "Available",
      "driverStatusText": "متاح",
      "statusTag": "الأقل ضغطاً",
      "estimatedFinishTimeText": "10:20 ص",
      "rank": 1,
      "isRecommended": false,
      "recommendationReason": null
    },
    {
      "driverId": "22222222-2222-2222-2222-222222222222",
      "fullName": "محمد السعيد",
      "avatarUrl": "https://cdn.mealmate.app/avatars/mohammed.jpg",
      "plateNumber": "DX-A58932",
      "phone": "+96551234002",
      "distanceKm": 6.2,
      "distanceText": "6.2 كم",
      "activeOrdersCount": 1,
      "currentLoadBoxes": 0,
      "currentLoadLabel": "0 بوكسات",
      "rating": 4.7,
      "driverStatus": "Busy",
      "driverStatusText": "مشغول",
      "statusTag": "منشغل بتسليم",
      "estimatedFinishTimeText": "10:20 ص",
      "rank": 2,
      "isRecommended": false,
      "recommendationReason": null
    },
    {
      "driverId": "44444444-4444-4444-4444-444444444444",
      "fullName": "يوسف العتيبي",
      "avatarUrl": "https://cdn.mealmate.app/avatars/yousef.jpg",
      "plateNumber": "DX-889102",
      "phone": "+96551234004",
      "distanceKm": 6.2,
      "distanceText": "6.2 كم",
      "activeOrdersCount": 1,
      "currentLoadBoxes": 0,
      "currentLoadLabel": "0 بوكسات",
      "rating": 4.6,
      "driverStatus": "InDelivery",
      "driverStatusText": "في التوصيل",
      "statusTag": "قريب من العميل",
      "estimatedFinishTimeText": "10:20 ص",
      "rank": 3,
      "isRecommended": false,
      "recommendationReason": null
    },
    {
      "driverId": "55555555-5555-5555-5555-555555555555",
      "fullName": "سالم الدوسري",
      "avatarUrl": "https://cdn.mealmate.app/avatars/salem-d.jpg",
      "plateNumber": "DX-771239",
      "phone": "+96551234005",
      "distanceKm": 6.2,
      "distanceText": "6.2 كم",
      "activeOrdersCount": 0,
      "currentLoadBoxes": 0,
      "currentLoadLabel": "0 بوكسات",
      "rating": 4.5,
      "driverStatus": "Returning",
      "driverStatusText": "راجع للمطعم",
      "statusTag": "قريب من العميل",
      "estimatedFinishTimeText": "10:20 ص",
      "rank": 4,
      "isRecommended": false,
      "recommendationReason": null
    }
  ]
}
```

---

### 2. نافذة "عرض البوكس" (View Box Modal)
- **الرابط**: `GET /api/v1/dispatcher/orders/{boxId}/summary`
- **استجابة الخادم الناجحة (200 OK)**:
```json
{
  "boxId": "a1111111-1111-1111-1111-111111111111",
  "boxCode": "#BX-1256",
  "customerMaskedId": "CUST-9821",
  "customerNameMasked": "أحمد م***",
  "customerPhoneMasked": "+965 9*** **44",
  "zoneName": "منطقة السالمية - قطعة 4",
  "address": "شارع سالم المبارك، عمارة 12، شقة 5",
  "deliveryTimeWindow": "09:30-10:30 ص",
  "boxCount": 1,
  "barcode": "MM-BX-1256-KWT",
  "deliveryNotes": "يرجى وضع البوكس عند الباب والرنين مرة واحدة",
  "allergies": [
    "مكسرات",
    "غلوتين"
  ],
  "meals": [
    {
      "mealId": "f1111111-1111-1111-1111-111111111111",
      "mealName": "سالمون مشوي مع الكينوا والخضار السوتيه",
      "quantity": 2,
      "category": "وجبة رئيسية",
      "notes": null
    },
    {
      "mealId": "f2222222-2222-2222-2222-222222222222",
      "mealName": "صدر دجاج بالأعشاب مع الأرز البني",
      "quantity": 2,
      "category": "وجبة رئيسية",
      "notes": null
    },
    {
      "mealId": "f3333333-3333-3333-3333-333333333333",
      "mealName": "سلطة كينوا يونانية خفيفة",
      "quantity": 2,
      "category": "سلطات",
      "notes": null
    },
    {
      "mealId": "f4444444-4444-4444-4444-444444444444",
      "mealName": "شوربة شوفان بالخضار العضوية",
      "quantity": 2,
      "category": "شوربات",
      "notes": null
    }
  ]
}
```

---

### 3. تأكيد إسناد البوكس للسائق المختار
- **الرابط**: `POST /api/v1/dispatcher/orders/{boxId}/assign`
- **جسم الطلب (Request Body)**:
```json
{
  "driverId": "33333333-3333-3333-3333-333333333333",
  "notes": "إسناد سريع للبوكس #BX-1256"
}
```
- **استجابة الخادم الناجحة (200 OK)**:
```json
{
  "boxId": "a1111111-1111-1111-1111-111111111111",
  "boxCode": "#BX-1256",
  "driverId": "33333333-3333-3333-3333-333333333333",
  "driverName": "سالم الحربي",
  "assignedAtUtc": "2026-09-22T03:45:00Z",
  "message": "تم إسناد البوكس للسائق بنجاح"
}
```

---

## ⚡ إدارة الحالات والتنقل (UI State & Navigation Flow)

1. **التحميل الأولي (Initial Loading)**:
   - عرض Shimmer Skeleton لبطاقة البوكس وبطاقة الترشيح وقائمة السائقين.
2. **إدارة الاختيار (Single Selection State)**:
   - يوجد حالة `selectedDriverId`:
     - يتم تعيينها افتراضياً إلى `bestSuggestion.driverId`.
     - عند نقر المستخدم على أي سائق في قائمة `candidates`، يتم نقل الاختيار إليه وإلغاء تحديد `bestSuggestion`.
     - عند نقر المستخدم مجدداً على `bestSuggestion`، يتم إلغاء تحديد السائق السابق وتحديد الاقتراح الأفضل.
3. **تأكيد الإسناد (On Submit)**:
   - إظهار مؤشر دوران داخل زر `[تأكيد الإسناد]`.
   - عند نجاح الطلب:
     1. عرض Snackbar / Toast بنجاح الإسناد: `تم إسناد البوكس للسائق بنجاح`.
     2. الرجوع فوراً إلى شاشة `Screen 10.01` (`Navigator.pop(context, true)`).
     3. تقوم شاشة `10.01` بتحديث عداداتها وقائمتها تلقائياً.
