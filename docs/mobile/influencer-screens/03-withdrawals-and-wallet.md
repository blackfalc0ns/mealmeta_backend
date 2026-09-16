# شاشة 3 — المحفظة وسحب الأرباح (03-withdrawals-and-wallet)

**الملف السابق:** [02-bank-account-setup.md](./02-bank-account-setup.md) · **التالي:** [الملف الشخصي والسوشيال](./04-social-links-and-profile.md)

---

## 🎯 الهدف من الشاشة

إدارة المحفظة المالية للمؤثر وتقديم طلبات سحب الأرباح ومتابعة سجل الحوالات البنكية بدقة وشفافية:
1. استعراض الرصيد المتاح للسحب الفوري، الرصيد المعلق، وإجمالي الأرباح التاريخية بالعملة `currencyCode: "KWD"`.
2. الاعتماد على كائن **سياسة السحب الديناميكية (`withdrawalPolicy`)** القادم من السيرفر لمعرفة إمكانية السحب والسبب في حال التعطيل.
3. تقديم طلب سحب جديد باستخدام خاصية **السحب الذكي (Smart Payout Fallback)** بنقرة واحدة بالاعتماد على الحساب البنكي المحفوظ مع دعم `correlationId`.
4. إمكانية **إلغاء طلب السحب المعلق (Cancel Pending Withdrawal)** واسترجاع الرصيد المتاح فوراً.
5. استعراض سجل طلبات السحب مع خاصية **الترقيم والتصفية (Pagination & Status Filters)**.

---

## 🖼️ المكونات البصرية للشاشة (UI Components)

### 1) كروت أرصدة المحفظة وسياسة السحب (Wallet Balance & Policy):
- **الرصيد المتاح للسحب (Available Balance):** كارت رئيسي بلون أخضر زمردي بارز يوضح المبلغ القابل للسحب بالـ `KWD` مع زر عريض `طلب سحب أرباح`.
  - إذا كان `withdrawalPolicy.canWithdraw == false`، يتم تعطيل الزر وعرض رسالة توضيحية حسب `withdrawalPolicy.disabledReason`:
    - `PendingWithdrawalExists`: "يوجد طلب سحب قيد المراجعة بالفعل".
    - `NoBankAccountConfigured`: "يرجى إضافة حساب بنكي أولاً لاستلام الأرباح".
    - `InsufficientBalance`: "الرصيد أقل من الحد الأدنى للسحب (10 د.ك)".
- **الرصيد المعلق (Pending Balance):** العمولات الناتجة عن اشتراكات جديدة قيد التأكيد النهائي.
- **إجمالي العمولات المستلمة (Lifetime Earnings):** مجموع كل المبالغ المسحوبة والمحققة منذ إنشاء الحساب.

---

### 2) نافذة طلب السحب الذكي (Smart Withdrawal Modal / Bottom Sheet):
- **حقل المبلغ المطلوب سحبه (Amount Input):**
  - الحد الأدنى والأقصى مأخوذان ديناميكياً من `withdrawalPolicy.minimumAmount` و `withdrawalPolicy.maximumAmount`.
  - زر اختياري `سحب كامل الرصيد المتاح` (Max Amount).
- **معلومات الحساب البنكي المستقبل (Payout Destination):**
  - **الحالة 1 (يوجد حساب محفوظ):**
    - كارت مضغوط يوضح اسم البنك والآيبان المقنع: `بنك الكويت الوطني •••• 9012`.
    - زر فرعي `استخدام حساب بنكي آخر لهذه الحوالة فقط`.
  - **الحالة 2 (لا يوجد حساب محفوظ أو رغبة في التغيير):**
    - حقول إدخال اسم البنك، اسم المستفيد، ورقم الـ IBAN.
    - خيار `☑️ حفظ كحساب بنكي افتراضي للمرات القادمة` (`saveAsDefaultBank: true/false`).
- **زر التأكيد النهائي (Confirm Payout):**
  - `تأكيد وإرسال طلب السحب`.

---

### 3) سجل طلبات السحب (Paginated Withdrawals History):
- **شريط التصفية السريعة (Filter Chips):**
  - `الكل` · `Requested` · `Approved` · `Paid` · `Rejected` · `Cancelled`.
- **بطاقة كل عملية سحب (Withdrawal Item Card):**
  - المبلغ (مثال: `150.000 د.ك`).
  - التاريخ والوقت ورقم المرجع `referenceNumber`.
  - بادج الحالة اللوني مع زر `إلغاء الطلب` في حال كانت الحالة `Requested` / `Pending`.

---

## 🌐 تفاصيل الـ APIs

### 1) جلب بيانات المحفظة وسياسة السحب

```http
GET /api/v1/influencer-partner/me/wallet
Authorization: Bearer <customer_jwt_token>
```

#### نموذج الاستجابة (200 OK):
```json
{
  "availableBalance": { "amount": 240.000, "currency": "KWD" },
  "pendingBalance": { "amount": 45.000, "currency": "KWD" },
  "lifetimeEarnings": { "amount": 850.000, "currency": "KWD" },
  "thisMonthEarned": { "amount": 65.000, "currency": "KWD" },
  "thisMonthWithdrawn": { "amount": 100.000, "currency": "KWD" },
  "currencyCode": "KWD",
  "hasActiveWithdrawalRequest": false,
  "withdrawalPolicy": {
    "minimumAmount": 10.000,
    "maximumAmount": 240.000,
    "currencyCode": "KWD",
    "canWithdraw": true,
    "disabledReason": null
  },
  "bankInfo": {
    "bankName": "National Bank of Kuwait",
    "accountHolderName": "Fahad Al-Mutawa",
    "iban": "KW82 •••• •••• 9012",
    "maskedIban": "KW82 •••• •••• 9012",
    "isConfigured": true
  },
  "recentTransactions": []
}
```

---

### 2) تقديم طلب سحب أرباح (Smart Payout with CorrelationId)

```http
POST /api/v1/influencer-partner/me/withdrawals
Content-Type: application/json
Authorization: Bearer <customer_jwt_token>

{
  "amount": 100.000,
  "withdrawalMethod": "BankTransfer",
  "saveAsDefaultBank": false,
  "correlationId": "c3e981e4-42f8-48dc-8bc0-cf77d337d402"
}
```

#### نموذج الاستجابة بعد إنشاء الطلب (201 Created):
```json
{
  "withdrawalId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "referenceNumber": "WD-2026-09-14-0012",
  "amount": { "amount": 100.000, "currency": "KWD" },
  "transferFee": { "amount": 0.000, "currency": "KWD" },
  "netAmount": { "amount": 100.000, "currency": "KWD" },
  "status": "Requested",
  "method": "BankTransfer",
  "currencyCode": "KWD",
  "bankDetails": {
    "accountHolderName": "Fahad Al-Mutawa",
    "bankName": "National Bank of Kuwait",
    "iban": "KW82 •••• •••• 9012",
    "maskedIban": "KW82 •••• •••• 9012",
    "isConfigured": true
  },
  "requestedAtUtc": "2026-09-14T15:30:00Z"
}
```

---

### 3) جلب سجل طلبات السحب مع الترقيم والفلترة (Pagination)

```http
GET /api/v1/influencer-partner/me/withdrawals?status=Completed&page=1&pageSize=20
Authorization: Bearer <customer_jwt_token>
```

#### نموذج الاستجابة (200 OK):
```json
{
  "items": [
    {
      "withdrawalId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
      "referenceNumber": "WD-2026-09-10-0008",
      "amount": { "amount": 100.000, "currency": "KWD" },
      "transferFee": { "amount": 0.000, "currency": "KWD" },
      "netAmount": { "amount": 100.000, "currency": "KWD" },
      "status": "Completed",
      "method": "BankTransfer",
      "currencyCode": "KWD",
      "bankDetails": {
        "accountHolderName": "Fahad Al-Mutawa",
        "bankName": "National Bank of Kuwait",
        "maskedIban": "KW82 •••• •••• 9012",
        "isConfigured": true
      },
      "requestedAtUtc": "2026-09-10T10:00:00Z",
      "reviewedAtUtc": "2026-09-11T12:30:00Z",
      "completedAtUtc": "2026-09-11T12:30:00Z",
      "rejectionReason": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

---

### 4) إلغاء طلب سحب معلق (Cancel Pending Withdrawal)

```http
POST /api/v1/influencer-partner/me/withdrawals/f47ac10b-58cc-4372-a567-0e02b2c3d479/cancel
Authorization: Bearer <customer_jwt_token>
```

#### نموذج الاستجابة بعد الإلغاء (200 OK):
```json
{
  "withdrawalId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "status": "Cancelled",
  "rejectionReason": "Cancelled by influencer"
}
```

---

## 🛡️ شروط وقواعد السحب والخصم

1. **الخصم والحجز:** عند تقديم طلب السحب، يتم خصم المبلغ من الرصيد المتاح `availableBalance` وحجزه لحين اكتمال التحويل أو رفضه/إلغائه (في حال الرفض أو الإلغاء يعود فوراً للرصيد المتاح).
2. **منع التكرار:** لا يمكن تقديم طلب سحب جديد في حال وجود طلب سابق بحالة `Requested` أو `UnderReview` أو `Approved`.
3. **الإلغاء:** مسموح فقط طالما أن الطلب في حالة `Requested` أو `UnderReview`.

---

## ➡️ الانتقال التالي
- للانتقال إلى شاشة الملف الشخصي وإعدادات السوشيال: [04-social-links-and-profile.md](./04-social-links-and-profile.md)
- للعودة إلى لوحة التحكم الرئيسية: [01-influencer-dashboard.md](./01-influencer-dashboard.md)
