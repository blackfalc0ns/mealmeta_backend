# شاشة 4 — إدارة الوصول (08.20)

**السابق:** [تفاصيل الفرد](./03-member-detail.md) · **التالي:** [الصلاحيات](./05-permissions.md)

## الهدف

تشغيل حساب منفصل للفرد، إيقافه، أو حذفه من العيلة. الدخول **بموبايل + باسورد** مش إيميل.

## عناصر الشاشة

- رقم الموبايل للعرض فقط (`phoneNumber`) — مش حقل إيميل
- تفعيل الحساب المنفصل (`hasSeparateAccount`)
- إيقاف (`isPaused`)
- زر أحمر: حذف الفرد (مش للمدير)
- زر حفظ

**مفيش** حقل باسورد يكتبه المدير. السيرفر يولّد الباسبورد.

## APIs

### قراءة

```http
GET /api/v1/customer/family/members/{memberId}/access
```

```json
{
  "memberId": "11111111-1111-4111-8111-111111111111",
  "hasSeparateAccount": false,
  "linkedCustomerUserId": null,
  "isPaused": false,
  "pausedAtUtc": null,
  "removedAtUtc": null,
  "inviteDeepLink": null,
  "inviteExpiresAtUtc": null,
  "phoneNumber": "+96550000000",
  "hasExistingLogin": false,
  "generatedPassword": null
}
```

`GET` **عمرُه** ما يرجع باسورد. `generatedPassword` دايمًا `null` هنا.

### حفظ

```http
PUT /api/v1/customer/family/members/{memberId}/access
Content-Type: application/json

{
  "hasSeparateAccount": true,
  "isPaused": false,
  "remove": null
}
```

الحقول اختيارية. ابعت اللي اتغيّر:

| الحقل | المعنى |
|--------|--------|
| `hasSeparateAccount: true` | فعّل حساب منفصل |
| `hasSeparateAccount: false` | عطّل الحساب المنفصل (من غير باسورد جديد) |
| `isPaused: true/false` | إيقاف / تشغيل |
| `remove: true` | حذف من العيلة (غير المدير) |

## باسورد مرة واحدة

لما التفعيل ينجح على حساب **من غير دخول سابق**:

```json
{
  "hasSeparateAccount": true,
  "linkedCustomerUserId": "22222222-2222-4222-8222-222222222222",
  "phoneNumber": "+96550000000",
  "hasExistingLogin": false,
  "generatedPassword": "aB3kLm9xQ2wR"
}
```

اعرض `generatedPassword` في ديالوج **فورًا** (انسخ / شارك مع الفرد).  
بعدها امسحه من الذاكرة. `GET` مش هيرجّعه. الدخول العضو: رقم الهاتف + كلمة المرور المعادة.

### حساب كان بيدخل قبل كده

```json
{
  "hasExistingLogin": true,
  "generatedPassword": null
}
```

**متغيّرش** باسورده. قول للمدير: صاحب الرقم عنده حساب MealMate — يدخل بنفس باسورده.

## حذف

```json
{ "remove": true }
```

المدير → `409 family.cannot_remove_manager`.

## قواعد

- متبنيش فورم إيميل/باسورد زي بعض التصاميم. العقد: موبايل + باسورد مولَّد.
- `inviteDeepLink` اختياري قديم. شاشة 08.20 تعتمد على الباسبورد.
- بعد الحذف الفرد يختفي من `GET members` بعد نجاح العملية. لا تفترض أن حذف العضو يلغي/يرد مبالغ أو ينهي خدمة تلقائياً؛ اعرض نتيجة التسوية/الطلب إذا أعادها endpoint. الإيقاف لا يعني تجميد الاشتراك.

## أخطاء

| code | HTTP | UI |
|------|------|-----|
| `family.cannot_remove_manager` | 409 | متشيلش المدير |
| `family.member_not_found` | 404 | الفرد مش موجود |
| `family.not_manager` | 403 | مش مدير |`n| `family.settlement_dependency_missing` | 422 | تعذر تنفيذ التسوية؛ لا توحِ بنجاح الإزالة |

## التالي

حفظ ناجح → ارجع لـ [03-member-detail.md](./03-member-detail.md) أو [05-permissions.md](./05-permissions.md)

## اللغة والتنسيق

كل copy جديد في قاموس عربي/إنجليزي reactive؛ اختبر `ar → en → ar` دون reload مع RTL/LTR. أرسل `Accept-Language`. قد يعيد BilingualJsonModifier الحقل الموافق للغة وحقل الاسم unsuffixed بدلاً من الحقلين `nameAr` و`nameEn`؛ لا تشترط وجودهما معاً. اختر اللغة النشطة ثم البديل غير الفارغ ثم label مترجم. التاريخ التشغيلي Asia/Kuwait؛ KWD بثلاث منازل عشرية.



