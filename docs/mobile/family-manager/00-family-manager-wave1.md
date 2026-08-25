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
| إضافة عضو | POST | `/members` → حالياً `409 family.member_requires_payment` (دفع الموجة 2) |
| 08.20 | GET/PUT | `/members/{memberId}/access` |
| 08.21 | GET/PUT | `/members/{memberId}/permissions` |
| قبول دعوة | POST | `/invites/{token}/accept` |

### Access PUT

```json
{ "hasSeparateAccount": true, "isPaused": false, "remove": false }
```

عند تفعيل حساب منفصل بدون ربط: الرد يتضمن `inviteDeepLink` مثل `mealmate://family/invite/{token}`.

### Permissions PUT

ثمانية boolean: `editDailyMeals`, `changeDeliveryTime`, `viewProgram`, `changeProgram`, `editMealsAndDays`, `editAddress`, `unitExchange`, `changePassword`.

لا تُعدَّل صلاحيات المدير → `family.manager_permissions_immutable`.

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
| `family.member_requires_payment` | إضافة عضو بعد الدفع |
| `family.invite_invalid` / `invite_conflict` | دعوة |
| `calendar.selection_locked` | بعد −72س |

## لا تفعل

- لا تفترض إرسال مبكر للمطعم قبل −72س.
- لا تستخدم تقويم المدير الفردي القديم لوجبات الأعضاء — استخدم `memberSubscriptionId`.
- لا تعتمد على اختصارات 08.01 كصفحات جاهزة في الموجة 1.
