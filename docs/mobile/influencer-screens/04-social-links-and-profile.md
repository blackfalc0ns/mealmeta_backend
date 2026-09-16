# شاشة 4 — الملف الشخصي وروابط التواصل (04-social-links-and-profile)

**الملف السابق:** [03-withdrawals-and-wallet.md](./03-withdrawals-and-wallet.md) · **التالي:** [التحليلات والأداء](./05-campaigns-and-analytics.md)

---

## 🎯 الهدف من الشاشة

تمكين المؤثر من إدارة بياناته التسويقية وتحديث قنواته على وسائل التواصل الاجتماعي والتأكد من تفعيل حسابه:
1. عرض البيانات الأساسية: الاسم التجاري، كود المؤثر المعتمد، نسبة العمولة، ورابط شروط الشراكة (`partnerTermsUrl`).
2. عرض حالة تفعيل الحساب الصريحة (`status`: `Pending` / `Active` / `Suspended` / `Rejected`).
3. إضافة وتعديل حسابات وقنوات التواصل (TikTok, Instagram, Snapchat, WhatsApp, YouTube, Telegram) مع التنظيف التلقائي للأحرف والروابط.
4. الحصول على الاستجابة المباشرة المحدثة بعد الحفظ (`InfluencerSocialLinksResponseDto`).

---

## 🖼️ المكونات البصرية للشاشة (UI Components)

### 1) هيدر الملف الشخصي (Profile Identity Header):
- **الأفاتار والاسم:** صورة المؤثر (`avatarUrl`) واسمه الترويجي المعتمد (`displayName`).
- **كود الخصم:** كود الإحالة مع زر نسخ سريع (`FAHAD10`).
- **شريط نسبة العمولة:** كارت مميز يوضح النسبة الحالية: `عمولتك: 10.0% من كل اشتراك`.
- **حالة الحساب (`status`):** بادج لوني صريح (`Active` أخضر / `Pending` برتقالي / `Suspended` أحمر).

---

### 2) نموذج قنوات التواصل الاجتماعي (Social Media Channels Form):
- **تيك توك (TikTok):** يقبل اسم المستخدم فقط أو الرابط كاملاً ويقوم السيرفر بتنظيفه وحفظ المعرف.
- **انستغرام (Instagram):** يقبل المعرف أو الرابط.
- **سناب شات (Snapchat):** اسم المستخدم في سناب شات.
- **واتساب (WhatsApp):** رقم الهاتف بصيغة دولية E.164 (مثال: `+96550000001`).
- **يوتيوب (YouTube):** رابط أو معرف القناة.
- **تليجرام (Telegram):** معرف الحساب أو القناة.
- **زر الحفظ:** `حفظ تحديثات قنوات التواصل`.

---

### 3) اختصارات الإعدادات وشروط البرنامج (Settings & Terms):
- زر `إدارة الحساب البنكي لاستلام الأرباح` (ينقل إلى [02-bank-account-setup.md](./02-bank-account-setup.md)).
- زر `سجل الحوالات والمحفظة` (ينقل إلى [03-withdrawals-and-wallet.md](./03-withdrawals-and-wallet.md)).
- زر `شروط وأحكام برنامج الشركاء` (يفتح الرابط الخارجي `partnerTermsUrl` في المتصفح).

---

## 🌐 تفاصيل الـ APIs

### 1) جلب الملف الشخصي الكامل للمؤثر

```http
GET /api/v1/influencer-partner/me
Authorization: Bearer <customer_jwt_token>
```

#### نموذج الاستجابة (200 OK):
```json
{
  "influencerProfileId": "7c8e6a12-88f2-4933-912b-31405b0d0100",
  "displayName": "فهد المطوع - فتنس",
  "influencerCode": "FAHAD10",
  "commissionRate": 10.00,
  "isActive": true,
  "status": "Active",
  "avatarUrl": null,
  "hasCompletedSocialOnboarding": true,
  "partnerTermsUrl": "https://mealmate.app/terms/influencer-partner-program",
  "bankInfo": {
    "bankName": "National Bank of Kuwait",
    "accountHolderName": "Fahad Al-Mutawa",
    "iban": "KW82 •••• •••• 9012",
    "maskedIban": "KW82 •••• •••• 9012",
    "isConfigured": true
  },
  "socialLinks": {
    "tiktok": "fahad_fitness",
    "instagram": "fahad.meals",
    "whatsapp": "+96550000001",
    "telegram": "fahad_deals",
    "youtube": "c/FahadFitnessKW",
    "snapchat": "fahad_snap"
  }
}
```

---

### 2) تحديث روابط قنوات التواصل الاجتماعي

```http
PUT /api/v1/influencer-partner/me/social-links
Content-Type: application/json
Authorization: Bearer <customer_jwt_token>

{
  "tiktok": "@fahad_fitness",
  "instagram": "https://instagram.com/fahad.meals",
  "whatsapp": "50000001",
  "telegram": "@fahad_deals",
  "youtube": "c/FahadFitnessKW",
  "snapchat": "fahad_snap"
}
```

#### نموذج الاستجابة بعد التنظيف والتحديث (200 OK):
```json
{
  "socialLinks": {
    "tiktok": "fahad_fitness",
    "instagram": "fahad.meals",
    "whatsapp": "+96550000001",
    "telegram": "fahad_deals",
    "youtube": "c/FahadFitnessKW",
    "snapchat": "fahad_snap"
  },
  "hasCompletedSocialOnboarding": true
}
```

---

## 🛡️ إرشادات التنظيف وقواعد الحقول (Cleaning Rules)

1. **إزالة المسافات والرموز:** يقوم السيرفر والموديل تلقائياً بإزالة بادئة `@` وروابط البروتوكول (`https://`, `http://`, `www.`) لحفظ المعرف النظيف.
2. **رقم الواتساب:** إذا أُدخل رقم كويتي من 8 أرقام (مثل `50000001`)، يقوم السيرفر بتحويله تلقائياً لصيغة E.164 (`+96550000001`).
3. **مسح رابط:** لمسح أي قناة سابقة، يرسل الموبايل `null` أو `""` للحقل المعني.
4. **اكتمال التسجيل (`hasCompletedSocialOnboarding`):** يصبح `true` بمجرد توفر قناة تواصل واحدة على الأقل.

---

## ➡️ الانتقال التالي
- للانتقال إلى شاشة التحليلات التفصيلية: [05-campaigns-and-analytics.md](./05-campaigns-and-analytics.md)
- للعودة إلى لوحة التحكم الرئيسية: [01-influencer-dashboard.md](./01-influencer-dashboard.md)
