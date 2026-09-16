# شاشة 2 — إعداد الحساب البنكي لاستلام الأرباح (02-bank-account-setup)

**الملف السابق:** [01-influencer-dashboard.md](./01-influencer-dashboard.md) · **التالي:** [المحفظة وسحب الأرباح](./03-withdrawals-and-wallet.md)

---

## 🎯 الهدف من الشاشة

تمكين المؤثر من إدارة الحساب المصرفي الذي ستُحوّل إليه أرباحه وعمولاته من تطبيق MealMate:
1. استعراض قائمة البنوك المعتمدة مباشرة من السيرفر (`GET /banks`) دون قيم ثابتة داخل التطبيق.
2. استعراض بيانات الحساب البنكي المسجل بصيغة آمنة ومقنعة (Masked IBAN).
3. إضافة حساب بنكي جديد أو تحديث بيانات الحساب الحالي (اسم البنك، اسم صاحب الحساب، ورقم الآيبان IBAN).
4. حذف أو إلغاء ربط الحساب البنكي مع تأكيد الأمان.
5. التحقق الفوري والدقيق من صيغة وطول الـ IBAN قبل الإرسال لمنع أخطاء التحويل المصرفي.

---

## 🖼️ المكونات البصرية للشاشة (UI Components)

### 1) حالة الحساب المضاف مسبقاً (Configured Bank Card):
- **بطاقة بنكية فاخرة (Debit-style Card View):**
  - شعار واسم البنك (مثال: بنك الكويت الوطني - NBK).
  - اسم صاحب الحساب الثلاثي أو كما هو مسجل في البنك.
  - رقم الآيبان المقنع بخط واضح: `KW82 •••• •••• 9012`.
  - بادج أخضر: `الحساب البنكي المعتمد لاستلام الأرباح`.
- **أزرار التحكم:**
  - زر `تعديل الحساب البنكي` (يفتح نموذج التعديل).
  - زر `حذف / إلغاء الربط` (أحمر) مع نافذة تأكيد منبثقة (Confirmation Dialog).

---

### 2) نموذج الإضافة / التعديل (Bank Account Form):
- **اختيار اسم البنك (Bank Selector / Dropdown):**
  - يتم ملء القائمة من `GET /api/v1/influencer-partner/banks`.
- **اسم المستفيد / صاحب الحساب (Account Holder Name):**
  - حقل نصي لإدخال الاسم الكامل المطابق للبطاقة المدنية والحساب البنكي.
- **رقم الآيبان (IBAN Input Field):**
  - لوحة مفاتيح أبجدية رقمية (Alphanumeric Keyboard) مع تحويل تلقائي للأحرف الكبيرة (Uppercase).
  - تنسيق تلقائي أثناء الكتابة بتقسيم الرمز كل 4 خانات لتسهيل القراءة (مثال: `KW82 NBOK 0000 1234 5678 9012`).
  - مؤشر تحقق لحظي (Checkmark أخضر عند اكتمال الصيغة الصحيحة بين 15 و 34 حرفاً ورقم).
- **زر الحفظ والتأكيد (Save & Confirm Button):**
  - زر عريض `حفظ الحساب البنكي المعتمد` مع مؤشر تحميل أثناء الاتصال بالسيرفر.

---

## 🌐 تفاصيل الـ APIs

### 1) جلب قائمة البنوك المعتمدة

```http
GET /api/v1/influencer-partner/banks
Authorization: Bearer <customer_jwt_token>
```

#### نموذج الاستجابة (Response 200 OK):
```json
[
  { "id": "nbk", "code": "NBK", "nameAr": "بنك الكويت الوطني", "nameEn": "National Bank of Kuwait", "isActive": true },
  { "id": "boubyan", "code": "BOUBYAN", "nameAr": "بنك بوبيان", "nameEn": "Boubyan Bank", "isActive": true },
  { "id": "kfh", "code": "KFH", "nameAr": "بيت التمويل الكويتي", "nameEn": "Kuwait Finance House", "isActive": true },
  { "id": "gulf", "code": "GBK", "nameAr": "بنك الخليج", "nameEn": "Gulf Bank", "isActive": true },
  { "id": "burgan", "code": "BURGAN", "nameAr": "بنك برقان", "nameEn": "Burgan Bank", "isActive": true },
  { "id": "warba", "code": "WARBA", "nameAr": "بنك وربة", "nameEn": "Warba Bank", "isActive": true },
  { "id": "cbk", "code": "CBK", "nameAr": "البنك التجاري الكويتي", "nameEn": "Commercial Bank of Kuwait", "isActive": true },
  { "id": "abk", "code": "ABK", "nameAr": "البنك الأهلي الكويتي", "nameEn": "Al Ahli Bank of Kuwait", "isActive": true },
  { "id": "kib", "code": "KIB", "nameAr": "بنك الكويت الدولي", "nameEn": "Kuwait International Bank", "isActive": true }
]
```

---

### 2) جلب الحساب البنكي الحالي للمؤثر

```http
GET /api/v1/influencer-partner/me/bank-account
Authorization: Bearer <customer_jwt_token>
```

#### نموذج الاستجابة في حالة وجود حساب محفوظ (200 OK):
```json
{
  "bankName": "National Bank of Kuwait",
  "accountHolderName": "Fahad Al-Mutawa",
  "iban": "KW82NBOK0000123456789012",
  "maskedIban": "KW82 •••• •••• 9012",
  "isConfigured": true
}
```

#### نموذج الاستجابة في حالة عدم وجود حساب محفوظ (200 OK):
```json
{
  "bankName": "",
  "accountHolderName": "",
  "iban": "",
  "maskedIban": null,
  "isConfigured": false
}
```

---

### 3) إضافة أو تحديث الحساب البنكي

```http
PUT /api/v1/influencer-partner/me/bank-account
Content-Type: application/json
Authorization: Bearer <customer_jwt_token>

{
  "bankName": "National Bank of Kuwait",
  "accountHolderName": "Fahad Al-Mutawa",
  "iban": "KW82NBOK0000123456789012"
}
```

#### الاستجابة بعد الحفظ الناجح (200 OK):
```json
{
  "bankName": "National Bank of Kuwait",
  "accountHolderName": "Fahad Al-Mutawa",
  "iban": "KW82NBOK0000123456789012",
  "maskedIban": "KW82 •••• •••• 9012",
  "isConfigured": true
}
```

---

### 4) حذف الحساب البنكي

```http
DELETE /api/v1/influencer-partner/me/bank-account
Authorization: Bearer <customer_jwt_token>
```

#### الاستجابة (200 OK):
```json
{
  "isSuccess": true
}
```

---

## 🛡️ قواعد التحقق البرمجية (Validation Rules)

1. **اسم البنك (`bankName`):**
   - إلزامي، من 2 إلى 200 حرف.
2. **اسم صاحب الحساب (`accountHolderName`):**
   - إلزامي، مطابق للهوية الرسمية، من 2 إلى 200 حرف.
3. **رقم الآيبان (`iban`):**
   - إلزامي.
   - الطول الإجمالي بعد إزالة المسافات: من 15 إلى 34 حرفاً ورقم.
   - يسمح فقط بالأحرف والرموز الإنجليزية والأرقام `^[a-zA-Z0-9]+$`.
   - يقوم السيرفر والموبايل تلقائياً بتنظيف المسافات والشرطات وتحويل الحروف إلى Capital:
     `KW82 NBOK 0000 1234 5678 9012` ➡️ `KW82NBOK0000123456789012`.

---

## ➡️ الانتقال التالي
- للانتقال إلى شاشة طلب سحب الأرباح والمحفظة: [03-withdrawals-and-wallet.md](./03-withdrawals-and-wallet.md)
- للعودة إلى لوحة التحكم الرئيسية: [01-influencer-dashboard.md](./01-influencer-dashboard.md)
