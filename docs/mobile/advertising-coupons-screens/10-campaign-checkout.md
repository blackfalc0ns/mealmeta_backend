# 10 — الدفع لحملة

شراء الاشتراك ضمن حملة مسار مستقل عن إدخال قسيمة الاشتراك الفردي.

## معاينة السعر والأهلية

`POST /api/v1/customer/campaigns/price-preview` يقبل `GetCampaignPricePreviewQuery`:

| الحقل | النوع | مطلوب؟ |
|---|---|---:|
| `campaignId` | UUID | نعم |
| `bundleId` | UUID | نعم |
| `durationDays` | عدد صحيح | نعم |
| `customerId` | UUID | لا |
| `governorate` | نص | لا |

يعيد `CampaignPricePreviewDto`: `campaignId`, `campaignNameAr`, `campaignNameEn`, `bundleId`, `durationDays`, `referenceCustomerPrice`, `totalDiscountPct`, `totalDiscountAmount`, `restaurantContributionPct`, `restaurantContributionAmount`, `platformContributionPct`, `platformContributionAmount`, `finalCustomerPrice`, `pricePerDay`, `currency`, `isCustomerEligible`, `ineligibilityReasons`. المسار `[AllowAnonymous]`؛ أرسل سياق العميل المتاح، لكن نتيجة المعاينة لا تحجز السعة. اعرض الأسعار من الاستجابة كما هي.

## إنشاء جلسة الحجز

`POST /api/v1/customer/campaigns/checkout-session` يتطلب عميلًا مسجلًا. جسم `CreateCampaignCheckoutSessionCommand`:

| الحقل | النوع | مطلوب؟ | الملاحظة |
|---|---|---:|---|
| `idempotencyKey` | نص | نعم | ثابت للمحاولة المنطقية نفسها |
| `campaignId` | UUID | نعم | معرف الحملة |
| `bundleId` | UUID | نعم | معرف الباقة |
| `customerId` | UUID | نعم | يبدله الخادم بهوية العميل المسجل |
| `durationDays` | عدد صحيح | نعم | المدة المختارة |
| `requestedServiceDates` | مصفوفة `DateOnly` | نعم | تواريخ الخدمة المطلوبة |
| `deliveryGovernorate` | نص أو null | لا | المحافظة |

### قالب طلب بمعرفات وتواريخ رمزية

لا ترسل القيم بين الأقواس كما هي. استخدم معرفات وتواريخ صالحة من حالة التطبيق:

```http
POST /api/v1/customer/campaigns/checkout-session
Authorization: Bearer {CUSTOMER_ACCESS_TOKEN}
Content-Type: application/json
X-Bilingual: true

{
  "idempotencyKey": "{NEW_UUID_FOR_THIS_LOGICAL_ATTEMPT}",
  "campaignId": "{CAMPAIGN_ID}",
  "bundleId": "{CAMPAIGN_BUNDLE_ID}",
  "customerId": "{SIGNED_IN_CUSTOMER_ID}",
  "durationDays": 30,
  "requestedServiceDates": ["2026-11-01", "2026-11-02"],
  "deliveryGovernorate": "{GOVERNORATE_IF_APPLICABLE}"
}
```

استجابة النجاح `CampaignCheckoutSessionDto`: `sessionId`, `campaignId`, `bundleId`, `customerId`, `durationDays`, `finalCustomerPrice`, `currency`, `expiresAtUtc`, `status`, `allocations`. يتضمن كل تخصيص `serviceDate`, `restaurantId`, `units`. تنشئ الجلسة حجز سعة مدته 15 دقيقة؛ إعادة الطلب بالمفتاح نفسه والحمولة نفسها تعيد النتيجة المخزنة، أما تغيير الحمولة مع المفتاح نفسه فينتج تعارضًا. استخدم مفتاحًا جديدًا للمحاولة الجديدة، وأعد استخدام المفتاح والحمولة نفسيهما فقط لإعادة محاولة الطلب نفسه.

## فجوة بدء الدفع وتأكيده

هذه الفجوة تخص دفع الحملة وحده، وهي مستقلة عن دفع الاشتراك الفردي باستخدام القسيمة الذي له مسار بدء وحالة موثقان في الشاشتين [08](08-payment.md) و[09](09-payment-result-and-resume.md). لا يعني إكمال تدفقات الإعلانات والقسائم أن دفع الحملة قد نُفّذ.

مسارات العميل الموجودة لا تنشئ جلسة دفع للمزود ولا تعيد `paymentUrl`، كما لا يوجد مسار عميل لاستعلام حالة جلسة الحملة. استجابة إنشاء الجلسة وحدها لا تعني أن الدفع بدأ أو نجح. مسار `POST /api/v1/webhooks/campaign-payment/callback` مخصص لاتصال الخادم بمزود الدفع، ويتطلب `X-Webhook-Secret`؛ لا يستدعيه تطبيق الجوال ولا يضع السر فيه. لذلك لا تخترع مسار دفع أو حالة نجاح للعميل. يجب أن يوفّر الخادم عقد بدء دفع واستعلام حالة قبل إكمال هذا التدفق في الجوال.

إذا كانت معاينة الحملة غير مؤهلة، حدّث البيانات واعرض السبب المترجم. عند امتلاء السعة أو تعارض الحجز أعد تحميل المعاينة والتوفر. عالج انتهاء الجلسة وفشل الشبكة بنصوص منفصلة مترجمة. لا تطبق خصم الحملة تلقائيًا داخل تدفق القسيمة الفردية.

**مصادر التنفيذ:** [CustomerCollaborativeCampaignsController.cs](../../../src/MealMate.Api/Controllers/Customer/CustomerCollaborativeCampaignsController.cs)، [GetCampaignPricePreviewQuery.cs](../../../src/MealMate.Application/Features/CollaborativeCampaigns/Queries/GetCampaignPricePreviewQuery.cs)، [CreateCampaignCheckoutSessionCommand.cs](../../../src/MealMate.Application/Features/CollaborativeCampaigns/Commands/CreateCampaignCheckoutSessionCommand.cs)، [CampaignCheckoutSessionDto.cs](../../../src/MealMate.Application/Features/CollaborativeCampaigns/DTOs/CampaignCheckoutSessionDto.cs)، [CampaignPaymentWebhookController.cs](../../../src/MealMate.Api/Controllers/Webhooks/CampaignPaymentWebhookController.cs).
