# شاشات اشتراك العيلة — عقد الموبايل

كل شاشة في ملف لوحدها. ابنِ الشاشة من ملفها؛ لا تعتمد على الدليل القديم المختلط.

**Base:** `/api/v1/customer/family-subscription-flow`  
**Auth:** `Authorization: Bearer <customer JWT>`  
**JSON:** camelCase · العملة KWD · RTL عربي

التقدم في الـ Figma: **1 الأعضاء → 2 البرنامج والباقة → 3 الحساسية والموقع → 4 الدفع**  
من الخطوة 2 فيه شريط أفراد أفقي. إضافة الفرد **إدارة عيلة** مش جزء من معالج الدفع.

| # | الشاشة | Figma | الملف |
|---|--------|-------|--------|
| 1 | إعداد العيلة | 07.01 | [01-family-setup.md](./01-family-setup.md) |
| 2 | إضافة فرد (موبايل أولاً) | شيت داخل 07.01 | [02-add-member.md](./02-add-member.md) |
| 3 | البرنامج | 07.04 | [03-program.md](./03-program.md) |
| 4 | الباقة والأيام والمدة والتصنيف | 07.05 | [04-bundle-days-duration-tier.md](./04-bundle-days-duration-tier.md) |
| 5 | الحساسية | 07.02 | [05-allergies.md](./05-allergies.md) |
| 6 | عنوان التوصيل | الموقع | [06-address.md](./06-address.md) |
| 7 | الدفع | 07.06 | [07-payment.md](./07-payment.md) |

## قواعد ثابتة لكل الشاشات

- عيلة واحدة لكل عميل.
- الفرد ممكن يبقى في العيلة **من غير اشتراك**. مشترك = عنده `memberSubscriptionId`.
- المدير يدفع **مرة واحدة** لسلة الأفراد المختارين فقط.
- لا تجمع `finalPrice` على الموبايل للدفع — السيرفر يعيد الحساب.
- لا باسورد عند الإضافة. لا زرار «مستخدم حالي». لا شاشة تفضيل غذائي في فلو العيلة.
- Select / Plus / Max في التصميم = `1` Basic / `2` Platinum / `3` Elite.
- كتالوج البرامج/الباقات **نفس الفردي** (نشط + عرض مطعم). المدد والتصنيفات من `subscription-flow`.
- بعد العيلة `Active` نفس الشاشات تُستخدم لإضافة أفراد جدد ثم دفع الإضافة فقط.

## Enums (أرقام System.Text.Json)

| الحقل | قيم |
|--------|------|
| `status` | `1` Draft · `2` CheckoutPending · `3` Active |
| `role` | `1` Manager · `2` Member |
| `setupStatus` | `1` NotStarted · `2` InProgress · `3` Ready |
| `gender` | `1` Male · `2` Female |
| `selectedTier` / `tier` | `1` Basic · `2` Platinum · `3` Elite |

## بطاقة الفرد (`members[]`)

تظهر في `GET current` وكل أوامر الأعضاء:

`memberId`, `role`, `displayName`, `relationshipCode`, `gender`, `ageYears`, `deliveryAddressId`, `setupStatus`, `programId`, `bundleId`, `selectedTier`, `durationDays`, `includeFriday`, `isCurrentUser`, `phoneNumber`, `occupation`, `startDate`

## أخطاء شائعة

| code | HTTP | المعنى |
|------|------|--------|
| `family.phone_required` | 400 | مفيش موبايل |
| `family.already_in_family` | 409 | الرقم في عيلة تانية أو مكرر هنا |
| `family.max_members` | 409 | سقف الباقة |
| `family.allergies_required` | 409 | فرد في السلة من غير حفظ حساسية |
| `family.member_incomplete` | 409 | مفيش فرد جاهز للدفع |
| `conversion.convert_required` | 409 | حول الاشتراك الفردي الأول |
| `auth.forbidden` | 403 | مش عميل |
