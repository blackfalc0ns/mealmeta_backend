# شاشة 1 — لوحة التحكم الرئيسية للمؤثر (01-influencer-dashboard)

**الملف السابق:** [README](./README.md) · **التالي:** [إعداد الحساب البنكي](./02-bank-account-setup.md) | [المحفظة وسحب الأرباح](./03-withdrawals-and-wallet.md)

---

## 🎯 الهدف من الشاشة

تعتبر لوحة التحكم (Home Dashboard) هي الواجهة المركزية لشريك التسويق والمؤثر في تطبيق MealMate. تتيح له:
1. متابعة أدائه التسويقي الفوري (النقرات، الاشتراكات المحققة، نسبة التحويل، الأرباح الكلية).
2. نسخ كود الخصم ورابط الإحالة المباشر ومشاركتهما عبر التطبيقات (WhatsApp, Instagram, Snapchat..).
3. عرض رمز الاستجابة السريعة (QR Code) لمشاركته في الفيديوهات أو الستوري أو البوسترات الترويجية.
4. الاطلاع المباشر على المحفظة والسياسة الديناميكية لسحب الأرباح.

---

## 🖼️ المكونات البصرية للشاشة (UI Components)

1. **الهيدر الترحيبي (Greeting & Profile Badge):**
   - صورة البروفايل أو الأفاتار (`avatarUrl` أو افتراضي).
   - الاسم التجاري للمؤثر (`displayName`).
   - بادج نسبة العمولة الفعالة (مثلاً: `عمولة 10% لكل اشتراك`).
2. **كروت الإحصائيات السريعة (Quick Performance Metrics):**
   - **الرصيد المتاح للسحب:** يظهر بخط بارز بالعملة `currencyCode` (مثل: `145.500 KWD`) مع زر سريع `طلب سحب`.
   - **إجمالي الأرباح المكتسبة:** إجمالي العمولات التاريخية.
   - **عدد الاشتراكات المحققة:** عدد الاشتراكات الناجحة التي تمت عبر كوده.
   - **إجمالي النقرات ونسبة التحويل (Conversion Rate):** لمتابعة تفاعل المتابعين.
3. **صندوق أدوات المشاركة الترويجية (Share & Referral Toolkit):**
   - **كود الخصم:** كود نصي كبير وواضح مع زر `نسخ` (Copy to Clipboard) مع تأثير Haptic وتأكيد بصري.
   - **رابط الإحالة (Referral Link):** زر `مشاركة الرابط` لتفعيل الـ Native Share Dialog.
   - **رمز الـ QR المخصص:** زر منبثق أو كارت لعرض الـ QR Code التفاعلي وتنزيله.
4. **شريط التنبيهات الذكي (Smart Action Banner):**
   - إذا كان `bankInfo == null` أو `bankInfo.isConfigured == false`، يظهر شريط تحذيري:  
     *"⚠️ لم تقم بإضافة حساب بنكي لاستلام أرباحك حتى الآن"* + زر `إضافة حساب بنكي`.
5. **قائمة أحدث الأنشطة (Recent Activities):**
   - آخر العمليات مع زر `عرض كل الأنشطة` (ينتقل إلى قائمة الأنشطة المرقّمة).

---

## 🌐 تفاصيل الـ APIs

### 1) جلب بيانات لوحة التحكم الرئيسية

```http
GET /api/v1/influencer-partner/me/home
Authorization: Bearer <customer_jwt_token>
```

#### نموذج الاستجابة المتوقعة (Response 200 OK):
```json
{
  "totalCommissions": {
    "amount": 480.000,
    "currency": "KWD"
  },
  "thisWeekCommissionsDiff": {
    "amount": 45.000,
    "currency": "KWD"
  },
  "kpis": {
    "registeredCustomers": 48,
    "registeredCustomersDiff": 2,
    "completedSubscriptions": 48,
    "completedSubscriptionsDiff": 1,
    "linkClicks": 1250,
    "linkClicksDiff": 5,
    "conversionRate": 3.84,
    "conversionRateDiff": 1.2
  },
  "influencerCode": "FAHAD10",
  "avatarUrl": null,
  "currencyCode": "KWD",
  "referralLink": "https://mealmate.com/ref/FAHAD10",
  "qrCodeData": "https://mealmate.com/ref/FAHAD10",
  "bankInfo": {
    "bankName": "National Bank of Kuwait",
    "accountHolderName": "Fahad Al-Mutawa",
    "iban": "KW82 •••• •••• 9012",
    "maskedIban": "KW82 •••• •••• 9012",
    "isConfigured": true
  },
  "wallet": {
    "availableBalance": { "amount": 145.500, "currency": "KWD" },
    "pendingBalance": { "amount": 35.000, "currency": "KWD" },
    "lifetimeEarnings": { "amount": 480.000, "currency": "KWD" },
    "thisMonthEarned": { "amount": 45.000, "currency": "KWD" },
    "thisMonthWithdrawn": { "amount": 100.000, "currency": "KWD" },
    "currencyCode": "KWD",
    "hasActiveWithdrawalRequest": false,
    "withdrawalPolicy": {
      "minimumAmount": 10.000,
      "maximumAmount": 145.500,
      "currencyCode": "KWD",
      "canWithdraw": true,
      "disabledReason": null
    }
  },
  "recentActivities": [
    {
      "activityId": "e1f13579-2468-4a0b-9cde-f01234567890",
      "activityCode": "subscription_purchased",
      "referenceId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
      "commissionAmount": { "amount": 12.500, "currency": "KWD" },
      "occurredAtUtc": "2026-09-14T14:20:00Z"
    }
  ]
}
```

---

### 2) جلب قائمة الأنشطة الكاملة مع الترقيم (Activities Pagination)

```http
GET /api/v1/influencer-partner/me/activities?page=1&pageSize=20
Authorization: Bearer <customer_jwt_token>
```

#### نموذج الاستجابة (Response 200 OK):
```json
{
  "items": [
    {
      "activityId": "e1f13579-2468-4a0b-9cde-f01234567890",
      "activityCode": "subscription_purchased",
      "referenceId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
      "commissionAmount": { "amount": 12.500, "currency": "KWD" },
      "occurredAtUtc": "2026-09-14T14:20:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 48,
  "totalPages": 3,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

---

## ➡️ الانتقال التالي
- للانتقال إلى شاشة إضافة/تعديل الحساب البنكي: [02-bank-account-setup.md](./02-bank-account-setup.md)
- للانتقال إلى شاشة المحفظة وسحب الأرباح: [03-withdrawals-and-wallet.md](./03-withdrawals-and-wallet.md)
