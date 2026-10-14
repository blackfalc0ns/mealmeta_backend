# عقد إدخال الكوبون — handoff موبايل 2026-10-13

## التدفق
إدخال الرمز في شاشة العرض/checkout → **فحص من الخادم** → عرض أثره داخل quote →
حجز عند إنشاء checkout → تأكيد عند نجاح الدفع → إلغاء الحجز عند فشل/انتهاء.

## 1) الفحص العام
`POST /api/v1/promo_referral_discount_codes/validate {code}` (AllowAnonymous،
rate limit 10/د لكل IP).

الاستجابة (موسعة إضافيًا — الأعمدة القديمة كما هي):
```json
{
  "isValid": true,
  "code": "SAVE20",
  "codeType": "PromoCode",            // PromoCode | ReferralCode
  "calculatedDiscountKwd": 0.0,       // 0 للنِسَب: المبلغ يُحسب في quote فقط
  "failureReason": null,
  "discountPercent": 0.20,            // قاعدة الرمز إن كانت نسبة
  "eligibilityCode": "rule_only_amount_set_at_quote"
}
```

**قواعد حاسمة للموبايل:**
- الخادم **لا يأخذ أي مبلغ من العميل** في validate (`TotalAmountKwd` و
  `SubscriptionId` المتاحان في الطلب **مهمَلان عمدًا** لأسباب أمنية — لا
  تعتمد عليهما ولا ترسلهما في تكامل جديد).
- المبلغ الفعلي للخصم يظهر في **quote** من الخادم فقط؛ لا تعرض رسالة نجاح
  قبل quote يحسب الأثر.
- `isValid=false` مع `failureReason` ⇒ اعرض رسالة الخطأ المترجمة حسب
  `codeType`/السبب؛ لا تكرر الإرسال تلقائيًا (rate limit).
- رمز إحالة بلا سياسة مكافأة يعود `isValid=false` وسبب
  "Referral reward policy is not configured" — حالة مشروعة وليست خطأ شبكة.

## 2) الأثر النهائي
أثر الكوبون (والتجميع مع خصم الحملة/الإحالة) تقرره السياسة المعتمدة في
الخادم عند quote: `STACKING_NOT_ALLOWED` يرفض الكوبون بلا مساس بسعر الحملة،
والنتيجة النهائية لا تكون سالبة أبدًا. المعاينة (preview) **لا تحرق**
استخدام الرمز.

## 3) حالات يجب تغطيتها في الشاشة
- رمز غير موجود / غير نشط / منتهي / بلغ حد الاستخدام (رسائل `failureReason`
  الموجودة مترجمة محليًا).
- انتهاء الكوبون أثناء checkout مفتوح ⇒ quote مُعاد حسابه قبل الدفع
  (لا دفع بمبلغ قديم).
- `Idempotency.Conflict` عند تكرار key بمحتوى مختلف.
