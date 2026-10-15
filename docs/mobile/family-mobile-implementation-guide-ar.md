# دليل تنفيذ فلو العيلة للموبايل

هذا الملف هو عقد التنفيذ لفريق Flutter. الباك إند هو مصدر الحقيقة للصلاحيات والحالات؛ الموبايل يعرض ما يرجعه السيرفر ولا يعيد تنفيذ قواعد السماح محليًا.

## القاعدة الأساسية

يوجد مدخل دخول واحد لكل العملاء:

```text
POST /api/v1/auth/customers/login
```

لا تنشئ شاشة دخول منفصلة للعضو. المدير، العضو المُدار، العضو المفصول، والعميل العادي يستخدمون نفس الشاشة.

إنشاء حساب العضو أو إصدار كلمة مؤقتة لا يفصل العضو من العيلة. الفصل عملية مستقلة ينفذها المدير صراحة.

## الفلو الكامل

### 1. المدير يجهز دخول العضو

```http
PUT /api/v1/customer/family/members/{memberId}/access
```

```json
{
  "hasSeparateAccount": true,
  "generateTemporaryPassword": true,
  "credentialOperationKey": "uuid-or-unique-operation-key"
}
```

قواعد التطبيق:

- `credentialOperationKey` مطلوب عند التفعيل أو إصدار كلمة مؤقتة، وطوله من 16 إلى 200 حرف.
- استخدم مفتاحًا جديدًا لكل عملية مقصودة.
- عند إعادة إرسال نفس الطلب بسبب timeout استخدم نفس المفتاح.
- لا تعرض كلمة السر إلا من قيمة `generatedPassword` في أول استجابة ناجحة.
- لا تحفظ كلمة السر في local storage أو analytics أو crash reports.
- إذا رجع `passwordAlreadyIssued: true` فلا تحاول استخراج كلمة قديمة؛ اعرض زر إصدار جديد بمفتاح جديد.
- لا تجعل زر التفعيل يغيّر `DetachedFromFamilyManagement`.

الاستجابة المهمة:

```json
{
  "hasSeparateAccount": true,
  "requiresPasswordSetup": true,
  "detachedFromFamilyManagement": false,
  "generatedPassword": "...",
  "passwordAlreadyIssued": false,
  "requiresExplicitReset": false,
  "phoneNumber": "+965...",
  "hasExistingLogin": false
}
```

### 2. العضو يدخل من شاشة الدخول العادية

يرسل التطبيق الهاتف وكلمة السر المؤقتة إلى login العادي.

إذا كانت هذه أول مرة، لا تحفظ access token أو refresh token. الاستجابة تكون مثل:

```json
{
  "isAuthenticated": false,
  "nextStep": "family_verify_otp",
  "loginChallengeToken": "...",
  "loginChallengeExpiresAtUtc": "..."
}
```

### 3. التحقق من OTP

```http
POST /api/v1/auth/customers/family-first-login/verify-otp
```

بعد النجاح:

```json
{
  "isAuthenticated": false,
  "nextStep": "family_set_password",
  "loginChallengeToken": "new-one-time-token"
}
```

لا تستخدم challenge token القديم بعد النجاح.

### 4. وضع كلمة السر الجديدة

```http
POST /api/v1/auth/customers/family-first-login/set-password
```

بعد النجاح يرجع login result عاديًا مع access token وrefresh token. انتقل مباشرة إلى تحميل سياقات العيلة.

### 5. إعادة إرسال OTP

```http
POST /api/v1/auth/customers/family-first-login/resend-otp
```

أرسل challenge token الحالي. الاستجابة تعطي challenge token جديدًا. أوقف زر الإرسال أثناء rate limit، ولا تعرض نصًا يفترض أن WhatsApp أرسل الرسالة إذا رجع السيرفر delivery failure.

### 6. تحميل السياقات والصلاحيات

```http
GET /api/v1/customer/family/me
```

الاستجابة تحتوي سياقًا لكل علاقة صالحة:

```json
{
  "groupId": "...",
  "memberId": "...",
  "memberSubscriptionId": "...",
  "isManager": false,
  "isDetachedFromFamilyManagement": false,
  "requiresPasswordSetup": false,
  "capabilities": [
    {
      "action": "ViewCalendar",
      "allowed": true,
      "reasonCode": null,
      "requiredPermission": "ViewCalendar"
    }
  ]
}
```

استخدم `groupId + memberId + memberSubscriptionId` كمفتاح السياق. لا تستخدم اسم العضو أو current subscription عام لتحديد السياق.

## اختيار الشاشة بعد الدخول

- `isManager = true`: اعرض لوحة إدارة العيلة وسياقات الإدارة.
- `isManager = false` و`isDetachedFromFamilyManagement = false`: اعرض خدمات العضو حسب `capabilities`.
- `isDetachedFromFamilyManagement = true`: اعرض حسابه واشتراكه وتقويمه وخدماته الشخصية حسب الصلاحيات المدعومة؛ لا تعرض sibling family scope.
- `requiresPasswordSetup = true`: أعد العضو إلى مسار إعداد كلمة السر ولا تسمح بجلسة عادية.
- السياق paused أو disabled يظهر حالته ورسالة مناسبة؛ لا تعتبر ذلك logout تلقائيًا.

## جدول الصلاحيات

| action | ما يظهر عند السماح | ملاحظات |
|---|---|---|
| `ViewCalendar` | التقويم وأيام الاشتراك | قراءة فقط حسب grants والسياق |
| `ViewProgram` / `ViewPlan` | البرنامج والخطة والاشتراك | قراءة بيانات الخدمة |
| `ViewInvoices` | الفواتير والمدفوعات | مستقلة عن ViewPlan |
| `SelectRestaurant` | اختيار المطعم | يحتاج قراءة التقويم والخطة |
| `EditDailyMeals` | تعديل الوجبات | يحتاج قراءة التقويم والخطة |
| `ConfirmMeals` | تأكيد الوجبات | يحتاج قراءة التقويم والخطة |
| `EditAddress` | تعديل عنوان التوصيل | يحتاج قراءة التقويم والخطة |
| `ChangeDeliveryTime` | تغيير وقت التوصيل | لا تعرضه إذا action غير مدعوم |
| `RequestFreeze` | طلب تجميد | السيرفر يعيد فحص الصلاحية بعد locks |
| `RequestRenewal` | التجديد/الموافقة | لا يعني تفعيل billing تلقائيًا |
| `ChangePassword` | تغيير كلمة السر | ملكية العميل، وليست grant للمدير |

أي action غير موجود أو `isSupported = false` لا يظهر. `allowed` هو القرار النهائي للعرض، والسيرفر يعيد التحقق عند التنفيذ.

## تحديث الصلاحيات من المدير

```http
GET /api/v1/customer/family/members/{memberId}/permissions
PUT /api/v1/customer/family/members/{memberId}/permissions
```

أرسل الحقول التي تغيرت فقط، مع `expectedVersion`:

```json
{
  "expectedVersion": 4,
  "viewCalendar": true,
  "editDailyMeals": null
}
```

`null` يعني اترك القيمة كما هي. عند `family.permissions_changed` أعد تحميل الصلاحيات واعرض تعارضًا؛ لا تعمل overwrite صامت.

## فصل العضو عن العيلة

```http
POST /api/v1/customer/family/members/{memberId}/detach
```

الفصل:

- لا يحذف العضو من لوحة المدير.
- لا يلغي الاشتراك أو الأيام أو snapshots التوصيل.
- يمنح العضو كل أفعاله الشخصية المدعومة تلقائيًا.
- يمنع المدير من تعديل الحساب أو الصلاحيات أو الملف أو الخدمة.
- يبقي للمدير قراءات الخدمة/الروستر المعتمدة فقط.
- يمنع sibling scope.
- لا يعيد ربط العضو تلقائيًا إذا فُعّل الحساب مرة أخرى.
- العملية idempotent؛ إعادة تنفيذها لا تزيد permission version.

قبل عرض زر الفصل، اعرض سبب الرفض إذا كان هناك payment/upgrade/renewal/freeze intent مانع. بعد النجاح امسح cache السياقات والصلاحيات وأعد طلب `/family/me`.

## الأخطاء المطلوبة

اربط `code` بقاموس عربي/إنجليزي، ولا تعرض `detail` الخام القادم من السيرفر.

| code | معنى الموبايل |
|---|---|
| `family.credential_operation_key_required` | أدخل مفتاح عملية صالحًا |
| `family.credential_operation_replayed` | العملية تمت سابقًا؛ اطلب reset جديدًا بمفتاح جديد |
| `family.member_phone_change_requires_verification` | تغيير هاتف الدخول يحتاج تحقق هوية |
| `family.member_detached` | العضو مفصول وهذه العملية لم تعد متاحة للمدير |
| `family.access_paused` | وصول العيلة متوقف مؤقتًا |
| `family.access_disabled` | وصول العيلة غير مفعّل |
| `family.password_setup_required` | أكمل إعداد كلمة السر أولًا |
| `family.permission_required` | الصلاحية المطلوبة غير ممنوحة |
| `family.permission_not_supported` | هذه الوظيفة غير مدعومة لهذا السياق |
| `family.permissions_changed` | تم تعديل الصلاحيات من جلسة أخرى |
| `subscription.service_unavailable` | أعد المحاولة لاحقًا |

احترم HTTP status: لا تحول كل الأخطاء إلى 400. خصوصًا 401، 403، 404، 409، و429.

## Cache وتبديل الحسابات

- خزّن capabilities لكل `groupId/memberId` فقط.
- امسحها بعد login، logout، detach، enable access، permission update، pause، resume، أو تغيير كلمة السر.
- عند فتح الاشتراك استخدم ID القادم من السياق، وليس آخر اشتراك محفوظ عالميًا.
- current selection للعضو يفضل سياقه الشخصي على sibling context.
- لا تعرض بيانات أسعار أو roster للعضو العادي إلا إذا رجعها endpoint المسموح.

## اللغة والأمان

- كل النصوص الجديدة في قاموس عربي/إنجليزي.
- يجب أن يتغير النص فورًا عند `ar → en → ar` بدون إعادة تشغيل.
- طبّق RTL للعربية وLTR للإنجليزية.
- لا تسجل كلمات السر أو OTP أو access/refresh/challenge tokens.
- التواريخ حسب Asia/Kuwait، والعملة KWD.
- لا تعتمد على إخفاء الزر كوسيلة حماية؛ endpoint هو مصدر التفويض.

## Acceptance checklist

- [ ] المدير يفعّل الحساب ولا يتغير detached status.
- [ ] كلمة السر تظهر مرة واحدة فقط.
- [ ] retry بنفس operation key لا يصدر كلمة ثانية.
- [ ] الدخول يتم من شاشة login العادية.
- [ ] OTP وset-password يستخدمان challenge token الجديد في كل خطوة.
- [ ] العضو يرى سياقه وصلاحياته من `/family/me`.
- [ ] العضو المتوقف لا يفقد credential ownership.
- [ ] المدير لا يعدل عضوًا مفصولًا.
- [ ] العضو المفصول يحتفظ بالاشتراك والأيام والتقويم المسموح.
- [ ] الصلاحيات غير المدعومة لا تظهر ولا تنفذ.
- [ ] sibling context مرفوض عند عدم وجود grant.
- [ ] التجميد يعاد تفويضه بعد locks.
- [ ] خطأ 409 في الصلاحيات يظهر كتعارض ويعيد التحميل.
- [ ] العربية والإنجليزية وتبديل اللغة يعملان لكل loading/empty/error/success state.
- [ ] كل اختبارات Flutter integration وAPI وPostgreSQL تمر قبل اعتماد الإصدار.

## ملاحظة الجاهزية

هذا الملف يشرح عقد الباك إند الجاهز للتنفيذ. لا يُعتبر فلو الموبايل جاهزًا للإصدار حتى يمر فريق Flutter بقائمة القبول كاملة على بيئة staging، ثم يتم نشر نسخة الباك إند والـmigration المتوافق معها.
