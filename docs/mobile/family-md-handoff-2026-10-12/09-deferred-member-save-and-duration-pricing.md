# تعديل الموبايل: فرد مضاف بدون اشتراك + الحفظ الفوري + أسعار المدد

هذا الملف يثبت عقد الموبايل بعد commit `18c5f5e` على فرع `dev`.

## 1. إضافة فرد بدون اشتراك

إضافة الفرد لا تعني شراء اشتراك له. بعد `POST /api/v1/customer/family/members` يجب أن تعرض الشاشة الفرد في إدارة العيلة بهذه الحالة:

```json
{
  "memberId": "<UUID>",
  "memberSubscriptionId": null,
  "setupStatus": "NotStarted",
  "lifecycle": {
    "status": "Unsubscribed",
    "checkoutSelected": false,
    "paymentStatus": null
  }
}
```

الفرد يظل ظاهرًا في الإدارة ويمكن تعديل بياناته. لا تضفه إلى ملخص الدفع أو إجمالي السعر طالما `checkoutSelected=false` و`memberSubscriptionId=null`.

## 2. الاشتراك لاحقًا

عندما يقرر المدير الاشتراك للفرد:

1. احفظ الخطة:

```http
PUT /api/v1/customer/family/members/<memberId>/plan
```

2. فعّل الاشتراك لهذا الفرد فقط:

```http
PUT /api/v1/customer/family/checkout-selection/<memberId>
Content-Type: application/json
```

```json
{
  "subscribeNow": true
}
```

اختيار الخطة وحده لا يدفع ولا يفعّل الاشتراك. الدفع يبدأ فقط من checkout بعد مراجعة الملخص وتأكيد المدير.

لإزالة الفرد من عملية الدفع قبل الدفع:

```json
{
  "subscribeNow": false
}
```

## 3. الحفظ الفوري للتفضيلات

نفّذ طلب الحفظ مباشرة بعد نجاح كل اختيار أو إلغاء اختيار، ولا تنتظر زر Next. أرسل القائمة كاملة الحالية حتى يمثل الطلب الحالة النهائية:

### الحساسية

```http
PUT /api/v1/customer/family/members/<memberId>/allergies
```

```json
{
  "selectedCodes": ["MILK", "PEANUT"]
}
```

عند عدم وجود حساسية:

```json
{
  "selectedCodes": []
}
```

### التفضيلات الغذائية

```http
PUT /api/v1/customer/family/members/<memberId>/dietary
```

```json
{
  "selectedCodes": ["VEGETARIAN"]
}
```

يقبل الباك أيضًا `dietaryPreferenceCodes` أو `selected` لنفس الطلب. استخدم اسمًا واحدًا ثابتًا في التطبيق ويفضل `selectedCodes`.

### أطعمة غير مرغوبة

```http
PUT /api/v1/customer/family/members/<memberId>/disliked-ingredients
```

```json
{
  "selectedCodes": ["INGREDIENT:<UUID>"]
}
```

بعد كل حفظ حدّث الحالة المحلية من response أو أعد `GET /api/v1/customer/family/current`. لا تعتبر الاختيار محفوظًا قبل وصول HTTP 200.

## 4. عرض أسعار التصنيفات قبل اختيار المدة

اطلب التصنيفات مع `familyMemberId`. إذا لم يحدد المستخدم مدة، اترك `durationDays` فارغًا:

```http
GET /api/v1/customer/family/programs/<programId>/bundles/<bundleId>/tiers?familyMemberId=<memberId>&includeFriday=false
```

كل تصنيف يعيد الآن قائمة أسعار المدد:

```json
{
  "tier": "Basic",
  "durationDays": null,
  "totalPrice": null,
  "currencyCode": "KWD",
  "durationPrices": [
    {
      "durationDays": 12,
      "totalPrice": 45.000,
      "dailyPrice": 3.750,
      "currencyCode": "KWD"
    },
    {
      "durationDays": 26,
      "totalPrice": 90.000,
      "dailyPrice": 3.462,
      "currencyCode": "KWD"
    }
  ]
}
```

اعرض `durationPrices` كبطاقات أو خيارات مدة داخل التصنيف. لا تعرض `totalPrice` كرقم نهائي عندما تكون `durationDays=null`.

بعد اختيار مدة، أعد طلب التصنيفات مع:

```http
GET .../tiers?familyMemberId=<memberId>&durationDays=26&includeFriday=false
```

عندها يكون `totalPrice` و`dailyPrice` سعر المدة المختارة، ويجب أن يعرض الموبايلهما في ملخص التصنيف.

## 5. حالات الواجهة المطلوبة

| الحالة | العرض | يدخل الدفع؟ |
|---|---|---|
| `Unsubscribed` | فرد موجود وإجراء “اشترك له لاحقًا” | لا |
| `SetupIncomplete` | توجد خطة ناقصة أو بيانات لازمة | لا |
| `ReadyToPay` مع `checkoutSelected=false` | جاهز لكن مؤجل | لا |
| `ReadyToPay` مع `checkoutSelected=true` | محدد في checkout | نعم بعد التأكيد |
| `Paid/Active` | اشتراك قائم | يظهر في الاشتراكات والتقويم حسب الصلاحيات |

## 6. قيد الحساب غير المرتبط

البيانات الأساسية للفرد يمكن حفظها بدون حساب مستقل. أما العنوان والحساسية والتفضيلات الغذائية حاليًا فتحتاج فردًا مرتبطًا بحساب؛ إذا رجع الباك `family.member_account_required` اعرض دعوة ربط الحساب ولا تعتبر الحفظ ناجحًا.

