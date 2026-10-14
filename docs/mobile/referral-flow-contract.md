# عقد الإحالات للعميل — handoff موبايل 2026-10-13

## النقاط
- `POST /api/v1/customer/referrals/attribute {code}` (Authorize، عميل)
  → إسناد **مرة واحدة** لكل عميل. أخطاء مستقرة:
  - `Referral.SelfReferral` — استخدام كودك أنت مرفوض.
  - `Referral.AlreadyAttributed` — لديك إسناد سابق (مرة واحدة للأبد).
  - `Referral.CodeNotFound` — الكود غير موجود/غير نشط.
- `GET /api/v1/customer/referrals/summary` (Authorize)
  → `{code, shareUrl, hasRewardPolicy, rewardPercent, attributedCount,
  rewardedCount, eligibilityCode}`.
  - `shareUrl` يُبنى من `PublicBaseUrl` المضبوط على الخادم؛ قد يكون null
    (اعرض الكود نصًا في هذه الحالة).
  - لا تُرجع النقطة **أي بيانات** عن المدعوين (أسماء/هواتف) — لا تصمم شاشة
    تعتمدها.

## قواعد المكافأة
- الإسناد وحده **لا يمنح مكافأة**. الاستحقاق: أول اشتراك مدفوع فعليًا
  للمُحال بعد نافذة التسوية المعتمدة — بحسب سياسة مالية معتمدة على الخادم.
- `hasRewardPolicy=false` ⇒ لا تعرض أي نسبة مئوية أو وعد خصم (لا "خصم 30%"
  إلا إذا كانت السياسة المحسوبة تحققها فعلاً — `rewardPercent` من الخادم
  هو المرجع).
- `eligibilityCode: "reward_pending_first_paid_subscription"` ⇒ اعرض حالة
  "المكافأة قيد الاستحقاق" مع شرط الأهلية، بلا عدّاد زمني وهمي.
- مكافأة غير مصروفة لا تُخصم من محفظة العميل؛ رصيد المحفظة الفعلي من
  نقاط المحفظة الموجودة، ومن الـ ledger عند التفعيل.

## deep link
رابط المشاركة `…/r/{CODE}` — عند فتحه: خزّن الكود محليًا ثم أكمل
register/login بالمسارات الحالية؛ بعد الدخول استدعِ `attribute` مرة واحدة
(وأظهر فشل SelfReferral/AlreadyAttributed كرسالة واضحة لا كراش).
