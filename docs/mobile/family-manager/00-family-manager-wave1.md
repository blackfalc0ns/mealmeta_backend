# إدارة العائلة (مدير العائلة) — الموجة 1

> **الجمهور:** فريق الموبايل Flutter.  
> **المصدر:** `docs/superpowers/specs/2026-08-25-family-manager-wave1-design.md`  
> **الدخول:** تبويب العائلة → **08.01** بعد عائلة مدفوعة نشطة.

## قواعد مقفولة

- تقويم العضو = **نفس الفردي** (مسودة → تم → إرسال للمطعم عند −72س فقط).
- شاشات Hub (home / members / access / permissions) = **مدير العائلة فقط**.
- العضو المرتبط يعدّل وجباته عبر مسارات التقويم إذا `editDailyMeals` وغير موقوف.
- اختصارات عناوين/اشتراكات/مدفوعات/تقارير في 08.01 تظهر `enabled: false` في الموجة 1.

## Hub APIs

Base: `/api/v1/customer/family`  
Authorization: Bearer customer JWT (المدير).

| الشاشة | Method | المسار |
|--------|--------|--------|
| 08.01 | GET | `/home` |
| 08.02 | GET | `/members` |
| 08.03 | GET | `/members/{memberId}` |
| إضافة عضو | POST | `/members` (إضافة عضو للروستر مع رفع الحد الأقصى لديناميكية الباقات) |
| 08.20 | GET/PUT | `/members/{memberId}/access` |
| 08.21 | GET/PUT | `/members/{memberId}/permissions` |
| قبول دعوة | POST | `/invites/{token}/accept` |

### 08.03 تفاصيل العضو (GET `/members/{memberId}`)
الرد أصبح يتضمن الحقول الحيوية التالية:
```json
{
  "memberSubscriptionId": "00000000-0000-0000-0000-000000000000",
  "phoneNumber": "010xxxxxxxx",
  "remainingDays": 14,
  "startDate": "2026-09-10",
  "endDate": "2026-10-07",
  "canRenew": true,
  "renewalPath": "/api/v1/customer/subscriptions/.../renewal-requests",
  "todayNutrition": {
    "calories": 1850,
    "proteinG": 130,
    "carbsG": 160,
    "fatG": 45
  }
}
```

### Access PUT (08.20)

```json
{ "hasSeparateAccount": true, "isPaused": false, "remove": false }
```

- **تفعيل حساب منفصل (`hasSeparateAccount: true`):**
  - إذا كان للعضو حساب مسجل مسبقاً، يتم ربطه مباشرة (`hasExistingLogin: true`).
  - إذا كان مستخدماً جديداً، يقوم السيرفر بإنشاء كلمة مرور مؤقتة وترجع في الرد (`generatedPassword`) مع `hasExistingLogin: false` ليسهل على المدير تسليمها للعضو، بالإضافة إلى رابط الدعوة العميق `inviteDeepLink`.
- **حذف العضو (`remove: true`):**
  - يتم فوراً إلغاء اشتراك العضو (`Cancelled`) مع حماية من هدر الطعام (إلغاء وجبات الأيام المستقبلية التي تتجاوز مهلة الـ 72 ساعة تلقائياً).

### إدارة اشتراكات وفواتير العائلة من قبل المدير
المدير أصبح يملك كامل الصلاحيات لإدارة اشتراكات أفراد العائلة عبر:
- `GET /api/v1/customer/subscriptions`: تظهر جميع اشتراكات أفراد العائلة للمدير.
- `POST /api/v1/customer/subscriptions/{memberSubId}/freeze-requests`: تجميد اشتراك أي فرد من العائلة.
- `POST /api/v1/customer/subscriptions/{memberSubId}/cancellation-requests`: إلغاء اشتراك الفرد (ممنوع على الأعضاء إلغاء اشتراك المدير: `family.manager_only_action`).
- `GET /api/v1/customer/subscriptions/invoices`: استعراض فواتير وسندات سداد العائلة الموحدة والفرعية.

## تقويم العضو

Base: `/api/v1/customer/subscriptions/{memberSubscriptionId}`  
نفس عقد الفردي: `calendar`, `days/{date}/restaurants`, `meals`, `restaurant`, `meals/slot`, `complete`, `auto-select`.

تبديل الأعضاء في 08.04: من قائمة `/home` أو `/members` مرّر `memberSubscriptionId`.

## أخطاء شائعة

| كود | معنى |
|-----|------|
| `family.not_manager` | ليس مدير المجموعة النشطة |
| `family.member_paused` | العضو موقوف |
| `family.permission_denied` | بدون `editDailyMeals` |
| `family.manager_only_action` | العضو يحاول إلغاء أو ترقية اشتراك المدير |
| `family.invite_invalid` / `invite_conflict` | دعوة غير صالحة أو مستخدمة |
| `calendar.selection_locked` | بعد −72س |

## لا تفعل

- لا تفترض إرسال مبكر للمطعم قبل −72س.
- لا تستخدم تقويم المدير الفردي القديم لوجبات الأعضاء — استخدم `memberSubscriptionId`.
- لا ترسل رقم هاتف إجباري للأطفال التابعين (Shadow Accounts مدعومة تلقائياً).

